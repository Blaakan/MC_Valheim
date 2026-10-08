#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Self tests that plant through the vanilla placement ghost (piece picked in the build table,
// crosshair on the ground, place button) and let saplings grow on the clock (vanilla Plant.SUpdate, plant time moved
// back like "skiptime"):
//   replant.plant       T03: transplants only once had, at the end of the list; placed on untilled ground for one
//                       transplant; young plant hover; refused on a wooden floor
//   replant.cloudberry  T11: level 4 dig, plant the transplant, it grows into a ripe cloudberry bush
//   replant.rootplace   T13: Yggdrasil ghost refused away from a root, fine a few metres from it; against the root it
//                       can be placed but has no room; RootRange minimum
//   replant.nobuild     T24: no-build zone: dig works, planting refused
//   replant.clock       T04, T35, T26 d: grows on the clock into the ripe vanilla plant, pick, regrow; a grow time
//                       change reaches a planted sapling (waited, not forced)
//   replant.sap         T14, T15: sap cost 40: hover, growth drains the root, second one waits, root refills, grows
//   replant.range       T16 + T38: planted 15 m from a root with range 20, range back to 6 = no root (also with the
//                       root already remembered by the hover), never grows, dug back
//   replant.vanilla     T08: cultivate, carrot seed and Grass of the vanilla cultivator still work
internal static partial class SelfTests
{
    private const string PlantName = "replant.plant";
    private const string CloudberryName = "replant.cloudberry";
    private const string RootPlaceName = "replant.rootplace";
    private const string NoBuildName = "replant.nobuild";
    private const string ClockName = "replant.clock";
    private const string SapName = "replant.sap";
    private const string RangeName = "replant.range";
    private const string VanillaName = "replant.vanilla";

    private static Piece SaplingPiece(PlantKind kind)
    {
        var go = TransplantContent.SaplingPrefab(kind);
        return go != null ? go.GetComponent<Piece>() : null;
    }

    private static bool Untilled(Vector3 p)
    {
        var hm = Heightmap.FindHeightmap(p);
        return hm != null && !hm.IsCultivated(p);
    }

    private static string PlantHover(string name, string statusToken) => L(name + " ( " + statusToken + " )");

    // Nothing lies on the ground at p: the first thing under a point 3 m above it (layers of the placement ray) is the
    // terrain itself, about level.
    private static bool BareGround(Player player, Vector3 p)
    {
        return Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, player.m_placeRayMask)
               && hit.collider.GetComponent<Heightmap>() != null && hit.normal.y >= 0.85f;
    }

    // What the crosshair ray of the placement ghost hits right now: same ray and layers as vanilla Player.PieceRayTest.
    // True = the terrain (a ground-only piece needs that), what = words for the check text.
    private static bool PlaceRayOnGround(Player player, Piece piece, out string what)
    {
        what = "nothing";
        var cam = GameCamera.instance;
        if (cam == null)
        {
            return false;
        }
        var mask = piece != null && (piece.m_waterPiece || piece.m_noInWater) ? player.m_placeWaterRayMask : player.m_placeRayMask;
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 50f, mask) || hit.collider == null)
        {
            return false;
        }
        var ground = hit.collider.GetComponent<Heightmap>() != null;
        what = (ground ? "the ground" : hit.collider.name) + $", slope normal {F(hit.normal.y)}";
        return ground;
    }

    // ---------- replant.plant (T03) ----------

    private static IEnumerator RunPlant()
    {
        if (!WorldReady(PlantName))
        {
            yield break;
        }
        var c = new Checks(PlantName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, PlantName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var name = rasp.ItemDisplayName;
            var piece = SaplingPiece(rasp);
            var inv = rig.Inv;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            if (!c.Check(tool != null && piece != null, "cultivator given, raspberry sapling piece built"))
            {
                c.Report();
                yield break;
            }
            c.Check(!player.m_noPlacementCost && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.AllPiecesUnlocked), "no build cheat on (it lists every piece)");

            // Only transplants the player has had: raspberry forgotten first.
            var knewMaterial = player.m_knownMaterial.Remove(name);
            var knewPiece = player.m_knownRecipes.Remove(name);
            rig.Undo(() =>
            {
                if (knewMaterial)
                {
                    player.m_knownMaterial.Add(name);
                }
                if (knewPiece)
                {
                    player.m_knownRecipes.Add(name);
                }
            });
            yield return Hold(rig, tool);
            player.UpdateKnownRecipesList();
            player.UpdateAvailablePiecesList();
            var table = player.m_buildPieces;
            if (!c.Check(table != null && ReferenceEquals(table, TransplantContent.CultivatorTable), "cultivator build table open"))
            {
                c.Report();
                yield break;
            }
            c.Check(!table.m_availablePieces.Contains(piece), "raspberry transplant never had: not in the build menu");
            foreach (var kind in PlantCatalog.All)
            {
                rig.Give(kind.ItemName, ReferenceEquals(kind, rasp) ? 2 : 1);
            }
            player.UpdateKnownRecipesList();
            player.UpdateAvailablePiecesList();
            c.Check(table.m_availablePieces.Contains(piece) && SaplingPiecesOffered(player) == PlantCatalog.All.Count,
                $"transplants in the inventory: their pieces are in the build menu ({SaplingPiecesOffered(player)} of {PlantCatalog.All.Count})");
            // At the end of the list: in every category list, no vanilla piece after our first one.
            var ours = new HashSet<Piece>(PlantCatalog.All.Select(SaplingPiece).Where(p => p != null));
            var listsWithOurs = 0;
            var atEnd = true;
            foreach (var list in table.m_availablePiecesByCategory)
            {
                var first = list.FindIndex(p => ours.Contains(p));
                if (first < 0)
                {
                    continue;
                }
                listsWithOurs++;
                for (var i = first; i < list.Count; i++)
                {
                    atEnd &= ours.Contains(list[i]);
                }
                atEnd &= first > 0;
            }
            c.Check(listsWithOurs > 0 && atEnd, $"transplants are the last entries of the list, after the vanilla pieces ({listsWithOurs} list(s))");
            var carried = inv.GetAllItems().First(i => ReferenceEquals(TransplantContent.KindOfItem(i), rasp));
            c.Check(piece.m_icon != null && ReferenceEquals(piece.m_icon, carried.GetIcon()) && IconPainter.IsPainted(piece.m_icon),
                "menu entry shows the transplant icon");
            c.Check(piece.m_description != null && piece.m_description.Contains(WhereText(rasp)), $"its description says where it grows ('{piece.m_description}')");

            // Placed on plain, untilled ground: one transplant used, a young plant stands there.
            if (!c.Check(FindPlantSpot(rig, taken, 1.2f, out var spot, p => Buildable(p) && Untilled(p)),
                    "free untilled spot outside any no-build zone"))
            {
                c.Report();
                yield break;
            }
            var placing = new Placing();
            yield return AimGhost(rig, piece, spot, placing);
            c.Check(placing.Status == Player.PlacementStatus.Valid, $"ghost can be placed on untilled ground (status {placing.Status})");
            var before = CountByName(inv, name);
            yield return PressPlace(rig, rasp.SaplingHash, placing);
            if (c.Check(placing.Placed, $"young plant placed (status {placing.Status})"))
            {
                c.Check(CountByName(inv, name) == before - 1, $"one transplant used ({before} -> {CountByName(inv, name)})");
                var plant = placing.Go.GetComponent<Plant>();
                var placed = placing.Go.GetComponent<Piece>();
                c.Check(placed != null && placed.IsCreator(), "planted by this player");
                c.Check(plant != null && plant.GetStatus() == Plant.Status.Healthy && plant.GetHoverText() == PlantHover(name, "$piece_plant_healthy"),
                    $"young plant: '{(plant != null ? plant.GetHoverText() : "none")}'");
                c.Check(Untilled(placing.Go.transform.position), "ground under it still untilled");
                // Cultivator put away: the game's own hover shows it.
                player.UnequipItem(tool, false);
                yield return Frames(3);
                yield return rig.AimAt(placing.Go.transform.position + Vector3.up * 0.3f);
                yield return Frames(4);
                c.Check(HintText().Contains(name) && HintText().Contains(L("$piece_plant_healthy")), $"hover with the cultivator away: '{OneLine(HintText())}'");
                SelfTest.Screenshot(PlantName, "young-plant");
                yield return Frames(2);
                yield return Hold(rig, tool);
            }

            // Wooden floor: refused.
            var floorName = ZNetScene.instance.GetPrefab("wood_floor") != null ? "wood_floor" : null;
            if (c.Check(floorName != null, "vanilla wood_floor piece exists")
                && c.Check(FindPlantSpot(rig, taken, 2f, out var floorSpot, p => Buildable(p)), "free spot for a wooden floor"))
            {
                var floor = rig.Spawn(floorName, floorSpot + Vector3.up * 0.05f, Quaternion.identity);
                yield return Settle();
                var top = AimPoint(floor);
                var onFloor = new Placing();
                yield return AimGhost(rig, piece, top, onFloor);
                c.Check(onFloor.Status == Player.PlacementStatus.Invalid, $"ghost on a wooden floor is refused (status {onFloor.Status})");
                var count = CountByName(inv, name);
                ClearCenter();
                yield return PressPlace(rig, rasp.SaplingHash, onFloor);
                c.Check(!onFloor.Placed && CountByName(inv, name) == count, "nothing placed on the floor, no transplant used");
                c.Check(CenterText() == L("$msg_invalidplacement"), $"centre text '{CenterText()}'");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.cloudberry (T11) ----------

    private static IEnumerator RunCloudberry()
    {
        if (!WorldReady(CloudberryName))
        {
            yield break;
        }
        var c = new Checks(CloudberryName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, CloudberryName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            yield return GrowReady(rig, c);
            var cloud = PlantCatalog.ByKey("CloudberryBush");
            var name = cloud.ItemDisplayName;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 4);
            var plantSpot = Vector3.zero;
            var wildSpot = Vector3.zero;
            if (!c.Check(tool != null && FindPlantSpot(rig, taken, 1.2f, out wildSpot)
                         && FindPlantSpot(rig, taken, 1.4f, out plantSpot, p => Buildable(p)),
                    "level 4 cultivator and two free spots"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            yield return DigCheck(rig, c, tool, cloud, "CloudberryBush", wildSpot, "Cloudberry bush transplant", -1);
            if (!c.Check(CountByName(rig.Inv, name) == 1, "one cloudberry transplant to plant"))
            {
                c.Report();
                yield break;
            }
            var placing = new Placing();
            yield return AimGhost(rig, SaplingPiece(cloud), plantSpot, placing);
            c.Check(placing.Status == Player.PlacementStatus.Valid, $"ghost can be placed (status {placing.Status})");
            yield return PressPlace(rig, cloud.SaplingHash, placing);
            if (c.Check(placing.Placed && CountByName(rig.Inv, name) == 0, $"transplant planted and used up (status {placing.Status})"))
            {
                var at = placing.Go.transform.position;
                Age(placing.View, 20000.0);
                var grown = new Grown();
                yield return WaitGrown(rig, cloud, placing.View, at, 20f, grown);
                if (c.Check(grown.Ok, $"after 20000 s on the clock it grew ({F(grown.Waited)} s waited)"))
                {
                    var pick = grown.Go.GetComponent<Pickable>();
                    c.Check(Utils.GetPrefabName(grown.Go) == "CloudberryBush" && pick != null && pick.CanBePicked(), "a cloudberry bush with berries");
                }
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.rootplace (T13) ----------

    private static IEnumerator RunRootPlace()
    {
        if (!WorldReady(RootPlaceName))
        {
            yield break;
        }
        var c = new Checks(RootPlaceName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, RootPlaceName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var ygg = PlantCatalog.ByKey("YggaShoot");
            var name = ygg.ItemDisplayName;
            var piece = SaplingPiece(ygg);
            var inv = rig.Inv;
            var origin = rig.Origin;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 5);
            rig.Give(ygg.ItemName, 3);
            if (!c.Check(tool != null && piece != null && CountByName(inv, name) == 3, "level 5 cultivator and three Yggdrasil transplants given"))
            {
                c.Report();
                yield break;
            }
            // Read only: the setting cannot go below 4.
            var range = Plugin.RootRange != null ? Plugin.RootRange.Description.AcceptableValues as AcceptableValueRange<float> : null;
            c.Check(range != null && Near(range.MinValue, 4f), $"RootRange setting cannot go below 4 ({(range != null ? F(range.MinValue) : "no range")})");

            // Well clear of any no-build zone: the three planting spots lie around the root.
            if (!c.Check(FindSpot(origin, Vector3.forward, new[] { 18f, 22f, 26f, 30f, 34f, 38f, 42f, 14f }, 3f, taken, out var rootSpot, p => Buildable(p, 8f)),
                    "free spot for an Ancient Root"))
            {
                c.Report();
                yield break;
            }
            var rootGo = rig.Spawn(PlantCatalog.RootPrefab, rootSpot, Quaternion.Euler(0f, 20f, 0f));
            yield return Settle();
            var root = rootGo != null ? rootGo.GetComponent<ResourceRoot>() : null;
            if (!c.Check(root != null && root.m_nview.IsValid(), "root spawned"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            var toOrigin = origin - rootSpot;

            // Away from the root: red ghost, hint with both limits, nothing placed.
            if (c.Check(FindSpot(rootSpot, toOrigin, new[] { 12f, 14f, 16f, 18f, 20f, 22f }, 2.3f, taken, out var far,
                    p => RootGate.FindRoot(p, 7.5f) == null && StandOk(origin, p) && Buildable(p)), "ground spot away from the root"))
            {
                // Control: the spot itself can be planted on (a raspberry transplant ghost is fine there).
                var rasp = PlantCatalog.ByKey("RaspberryBush");
                rig.Give(rasp.ItemName, 1);
                var control = new Placing();
                yield return AimGhost(rig, SaplingPiece(rasp), far, control);
                c.Check(control.Status == Player.PlacementStatus.Valid, $"control: a raspberry transplant ghost on that spot can be placed (status {control.Status})");
                var placing = new Placing();
                yield return AimGhost(rig, piece, far, placing);
                c.Check(placing.Status == Player.PlacementStatus.Invalid, $"away from the root: ghost refused (status {placing.Status})");
                c.Check(HintText() == "Plant 2 to 6 m from an Ancient Root", $"hint '{OneLine(HintText())}'");
                ClearCenter();
                yield return PressPlace(rig, ygg.SaplingHash, placing);
                c.Check(!placing.Placed && CountByName(inv, name) == 3 && CenterText() == L("$msg_invalidplacement"),
                    $"nothing placed away from the root ('{CenterText()}')");
            }

            // A few metres from the root: green ghost, placed, healthy.
            if (c.Check(FindSpot(rootSpot, toOrigin, new[] { 4.5f, 5f, 5.5f, 6f, 7f, 8f, 9f, 10f, 11f }, ygg.GrowRadius + 0.3f, taken, out var good,
                    p => RootGate.FindRoot(p, 5.5f) == root && StandOk(origin, p) && Buildable(p)), "ground spot a few metres from the root"))
            {
                c.Note($"good spot {F(Vector3.Distance(good, rootSpot))} m from the root position");
                var placing = new Placing();
                yield return AimGhost(rig, piece, good, placing);
                c.Check(placing.Status == Player.PlacementStatus.Valid, $"a few metres from the root: ghost can be placed (status {placing.Status})");
                yield return PressPlace(rig, ygg.SaplingHash, placing);
                if (c.Check(placing.Placed && CountByName(inv, name) == 2, $"Yggdrasil transplant planted for one transplant (status {placing.Status})"))
                {
                    var plant = placing.Go.GetComponent<Plant>();
                    plant.UpdateHealth(100.0);
                    c.Check(plant.GetStatus() == Plant.Status.Healthy, $"it has room there ({plant.GetStatus()})");
                    c.Check((RootGate.HoverLines(plant) ?? "").Contains("Draws 20 sap"), "and it found the root");
                }
            }

            // Right against the root: can be placed too, but no room to grow; Replant gives it back.
            // "Ground" is the point: with the crosshair on the root itself the game refuses any ground-only plant
            // (Invalid, run of 2026-10-08), and a low part of the root mesh lying on the spot is enough for that. So
            // several spots are found, each bare from above, and the one used is the first where the crosshair ray of
            // the ghost really lands on the ground with the root mesh still closer than the grow radius.
            var nears = new List<Vector3>();
            var toward = toOrigin;
            toward.y = 0f;
            toward.Normalize();
            foreach (var r in new[] { 1.5f, 2f, 1f, 2.5f, 3f, 3.5f, 4f })
            {
                for (var step = 0; step < 16 && nears.Count < 8; step++)
                {
                    var angle = (step % 2 == 0 ? 1f : -1f) * ((step + 1) / 2) * 22.5f;
                    var p = rootSpot + Quaternion.Euler(0f, angle, 0f) * toward * r;
                    p.y = Ground(p);
                    if (Mathf.Abs(p.y - rootSpot.y) > 1.5f || RootGate.FindRoot(p, ygg.GrowRadius - 0.2f) != root)
                    {
                        continue;
                    }
                    // Open ground, not under or inside the root mesh.
                    if (Physics.CheckSphere(p + Vector3.up * 0.6f, 0.35f, SpaceMask, QueryTriggerInteraction.Ignore)
                        || Physics.Raycast(p + Vector3.up * 0.2f, Vector3.up, 100f, RoofMask) || !StandOk(origin, p) || !Buildable(p, 2f))
                    {
                        continue;
                    }
                    if (!BareGround(player, p) || nears.Any(n => (n - p).sqrMagnitude < 1f))
                    {
                        continue;
                    }
                    nears.Add(p);
                }
            }
            Placing againstRoot = null;
            var landedOn = "no spot found";
            foreach (var candidate in nears)
            {
                var attempt = new Placing();
                yield return AimGhost(rig, piece, candidate, attempt);
                var onGround = PlaceRayOnGround(player, piece, out landedOn);
                if (onGround && RootGate.FindRoot(attempt.GhostAt, ygg.GrowRadius - 0.2f) == root)
                {
                    againstRoot = attempt;
                    break;
                }
            }
            if (c.Check(againstRoot != null,
                    $"ground spot right against the root (root mesh closer than 2 m) with the crosshair on the ground ({nears.Count} spot(s) looked at; last crosshair on {landedOn})"))
            {
                var placing = againstRoot;
                c.Check(placing.Status == Player.PlacementStatus.Valid, $"against the root: ghost can be placed too (status {placing.Status}, crosshair on {landedOn})");
                yield return PressPlace(rig, ygg.SaplingHash, placing);
                if (c.Check(placing.Placed, $"planted against the root (status {placing.Status})"))
                {
                    var plant = placing.Go.GetComponent<Plant>();
                    plant.UpdateHealth(100.0);
                    c.Check(plant.GetStatus() == Plant.Status.NoSpace && plant.GetHoverText().StartsWith(PlantHover(name, "$piece_plant_nospace"), StringComparison.Ordinal),
                        $"it has no room to grow: '{OneLine(plant.GetHoverText())}'");
                    var count = CountByName(inv, name);
                    c.Check(Replant.TryReplant(player, tool, placing.View, ygg, true, out var why) && !Alive(placing.View) && CountByName(inv, name) == count + 1,
                        $"Replant gives it back ({why})");
                }
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.nobuild (T24) ----------

    private static IEnumerator RunNoBuild()
    {
        if (!WorldReady(NoBuildName))
        {
            yield break;
        }
        var c = new Checks(NoBuildName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, NoBuildName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var kind = PlantCatalog.ByKey("MushroomYellow");
            var name = kind.ItemDisplayName;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var origin = rig.Origin;
            // A no-build location around the player: the game's own (spawn stones) when there is one, else the nearest
            // location made one for this test. Two spots well inside it are needed (zone edge = ghost may sit outside).
            var probe = new List<Vector3>();
            if (!(FindPlantSpot(rig, probe, 1f, out _, p => DeepInNoBuild(p)) && FindPlantSpot(rig, probe, 1.2f, out _, p => DeepInNoBuild(p))))
            {
                Location nearest = null;
                foreach (var loc in Location.s_allLocations)
                {
                    if (loc != null && (nearest == null || (loc.transform.position - origin).sqrMagnitude < (nearest.transform.position - origin).sqrMagnitude))
                    {
                        nearest = loc;
                    }
                }
                if (nearest != null)
                {
                    var noBuild = nearest.m_noBuild;
                    var radius = nearest.m_noBuildRadiusOverride;
                    nearest.m_noBuild = true;
                    nearest.m_noBuildRadiusOverride = Vector3.Distance(nearest.transform.position, origin) + 60f;
                    rig.Undo(() =>
                    {
                        nearest.m_noBuild = noBuild;
                        nearest.m_noBuildRadiusOverride = radius;
                    });
                    c.Note($"no no-build zone at the spawn: location {nearest.name} made one for this test");
                }
            }
            else
            {
                c.Note("the spawn area is a no-build zone of the game");
            }
            var plantSpot = Vector3.zero;
            var wildSpot = Vector3.zero;
            if (!c.Check(tool != null && FindPlantSpot(rig, taken, 1f, out wildSpot, p => DeepInNoBuild(p))
                         && FindPlantSpot(rig, taken, 1.2f, out plantSpot, p => DeepInNoBuild(p)),
                    "two free spots well inside a no-build zone"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            var wild = rig.Spawn("Pickable_Mushroom_yellow", wildSpot, Quaternion.identity);
            yield return Settle();
            var view = wild.GetComponent<ZNetView>();
            ClearCenter();
            c.Check(Replant.TryReplant(player, tool, view, kind, false, out var why) && !Alive(view) && CountByName(rig.Inv, name) == 1,
                $"yellow mushroom dug up inside the no-build zone ({why})");
            c.Check(CenterText() != L("$msg_nobuildzone"), $"no 'cannot build here' refusal ('{CenterText()}')");

            var placing = new Placing();
            yield return AimGhost(rig, SaplingPiece(kind), plantSpot, placing);
            c.Check(placing.Status == Player.PlacementStatus.NoBuildZone, $"planting there: ghost refused as a no-build zone (status {placing.Status})");
            ClearCenter();
            yield return PressPlace(rig, kind.SaplingHash, placing);
            c.Check(!placing.Placed && CountByName(rig.Inv, name) == 1 && CenterText() == L("$msg_nobuildzone"),
                $"nothing planted, the game's refusal shown ('{CenterText()}')");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.clock (T04, T35, T26 d) ----------

    private static IEnumerator RunClock()
    {
        if (!WorldReady(ClockName))
        {
            yield break;
        }
        var c = new Checks(ClockName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, ClockName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            yield return GrowReady(rig, c);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var mush = PlantCatalog.ByKey("Mushroom");
            var spotD = Vector3.zero;
            var spotC = Vector3.zero;
            var spotB = Vector3.zero;
            if (!c.Check(FindPlantSpot(rig, taken, 1.4f, out var spotA) && FindPlantSpot(rig, taken, 1f, out spotB)
                         && FindPlantSpot(rig, taken, 1.4f, out spotC) && FindPlantSpot(rig, taken, 1.4f, out spotD), "four free spots"))
            {
                c.Report();
                yield break;
            }
            // Default rules. A and B (T04) are due at once; C (T35) waits for the grow time change.
            var a = rig.Spawn(rasp.SaplingName, spotA, Quaternion.identity);
            var b = rig.Spawn(mush.SaplingName, spotB, Quaternion.identity);
            var cGo = rig.Spawn(rasp.SaplingName, spotC, Quaternion.identity);
            yield return Settle();
            var aView = a.GetComponent<ZNetView>();
            var bView = b.GetComponent<ZNetView>();
            var cView = cGo.GetComponent<ZNetView>();
            var cPlant = cGo.GetComponent<Plant>();
            TransplantContent.GrowTimes(rasp, 1f, out var lo1, out var hi1);
            TransplantContent.GrowTimes(mush, 1f, out _, out var hiMush);
            TransplantContent.GrowTimes(rasp, 0.1f, out var lo01, out var hi01);
            c.Check(hi1 < 20000f && hiMush < 20000f && hi01 < 2000f && lo1 > 2000f,
                $"grow times: bush {F(lo1)}-{F(hi1)} s, mushroom up to {F(hiMush)} s, bush x0.1 up to {F(hi01)} s");
            Age(aView, 20000.0);
            Age(bView, 20000.0);
            Age(cView, 2000.0);

            var grownA = new Grown();
            yield return WaitGrown(rig, rasp, aView, spotA, 20f, grownA);
            if (c.Check(grownA.Ok, $"raspberry transplant grew on the clock ({F(grownA.Waited)} s after its time was set)"))
            {
                var pick = grownA.Go.GetComponent<Pickable>();
                var scale = grownA.Go.transform.localScale.x;
                c.Check(Utils.GetPrefabName(grownA.Go) == "RaspberryBush" && pick != null && pick.CanBePicked(), "it is a raspberry bush with berries");
                c.Check(scale >= rasp.MinScale - 0.001f && scale <= rasp.MaxScale + 0.001f, $"a little bigger or smaller than usual (scale {F(scale)})");
                if (pick != null)
                {
                    var want = PickAmount(pick);
                    pick.Interact(player, false, false);
                    yield return new WaitForSeconds(0.7f);
                    c.Check(pick.GetPicked() && StackSum(DropsNear(spotA, SharedName("Raspberry"))) == want, $"picked: {want} raspberries, like a wild bush");
                    yield return Regrow(pick);
                    c.Check(pick.CanBePicked(), "berries back after its regrow time");
                }
            }
            var grownB = new Grown();
            yield return WaitGrown(rig, mush, bView, spotB, 12f, grownB);
            if (c.Check(grownB.Ok, "mushroom transplant grew on the clock"))
            {
                var pick = grownB.Go.GetComponent<Pickable>();
                c.Check(Utils.GetPrefabName(grownB.Go) == "Pickable_Mushroom" && pick != null && pick.CanBePicked(), "it is a ripe mushroom");
                if (pick != null)
                {
                    var view = grownB.Go.GetComponent<ZNetView>();
                    pick.Interact(player, false, false);
                    yield return new WaitForSeconds(0.7f);
                    c.Check(pick.GetPicked() && Alive(view) && StackSum(DropsNear(spotB, SharedName("Mushroom"))) >= 1, "picked: a mushroom, the plant stays");
                    yield return Regrow(pick);
                    c.Check(pick.CanBePicked(), "mushroom back after its regrow time");
                }
            }

            // T35: C is 2000 s old and long past its first 10 s: not due with x1 (control). Then the order of the item:
            // grow time x0.1 through the rules (no Rebuild call here), the planted sapling gets the new time within
            // a second, and only then the 2000 s pass. C is made 1000 s old for the change (not due with x0.1
            // either): a sapling already due grows at its next slow update, a tenth of a second after the new time
            // reached it, and nobody could read the time on it any more (run of 2026-10-08: "gone").
            yield return new WaitForSeconds(1.5f);
            c.Check(Alive(cView) && Near(cPlant.m_growTime, lo1, 0.5f) && Time.time - cPlant.m_spawnTime > 10f,
                $"planted with grow time x1: 2000 s is not enough ({(Alive(cView) ? F(cPlant.m_growTime) + " s, loaded " + F(Time.time - cPlant.m_spawnTime) + " s ago" : "sapling gone")})");
            Age(cView, 1000.0);
            ServerRules.TestRules = new CultivatorRules { GrowTimeMultiplier = 0.1f };
            var changedAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - changedAt < 1f && Alive(cView) && !Near(cPlant.m_growTime, lo01, 0.5f))
            {
                yield return null;
            }
            var took = Time.realtimeSinceStartup - changedAt;
            c.Check(Alive(cView) && !TransplantContent.RebuildPending && Near(cPlant.m_growTime, lo01, 0.5f) && Near(cPlant.m_growTimeMax, hi01, 0.5f),
                $"grow time x0.1 reached the planted sapling within a second ({(Alive(cView) ? F(cPlant.m_growTime) + " s, " + F(took) + " s after the change" : "sapling gone")})");
            yield return new WaitForSeconds(0.5f);
            c.Check(Alive(cView), "1000 s old with grow time x0.1: not due yet, still a young plant");
            Age(cView, 2000.0);
            var grownC = new Grown();
            yield return WaitGrown(rig, rasp, cView, spotC, 12f, grownC);
            c.Check(grownC.Ok && Utils.GetPrefabName(grownC.Go) == "RaspberryBush", $"and it grew without leaving the area ({F(grownC.Waited)} s)");

            // T26 d: planted while x0.1 is in force, 2000 s later it is grown.
            var d = rig.Spawn(rasp.SaplingName, spotD, Quaternion.identity);
            yield return Settle();
            var dView = d.GetComponent<ZNetView>();
            c.Check(Near(d.GetComponent<Plant>().m_growTime, lo01, 0.5f), "planted with grow time x0.1");
            Age(dView, 2000.0);
            var grownD = new Grown();
            yield return WaitGrown(rig, rasp, dView, spotD, 20f, grownD);
            c.Check(grownD.Ok, $"it grows after 2000 s ({F(grownD.Waited)} s waited)");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.sap (T14, T15) ----------

    // Ancient Root at a free spot away from the test origin.
    // accept = extra test of the root spot (planting through the ghost near it: well clear of no-build zones).
    private static bool SpawnRoot(Rig rig, Checks c, List<Vector3> taken, out GameObject rootGo, out ResourceRoot root, out Vector3 rootSpot,
        Func<Vector3, bool> accept = null)
    {
        rootGo = null;
        root = null;
        rootSpot = Vector3.zero;
        if (!c.Check(FindSpot(rig.Origin, Vector3.forward, new[] { 18f, 22f, 26f, 30f, 34f, 38f, 42f, 14f }, 3f, taken, out rootSpot, accept), "free spot for an Ancient Root"))
        {
            return false;
        }
        rootGo = rig.Spawn(PlantCatalog.RootPrefab, rootSpot, Quaternion.Euler(0f, 20f, 0f));
        root = rootGo != null ? rootGo.GetComponent<ResourceRoot>() : null;
        return c.Check(root != null, "root spawned");
    }

    // accept = extra test of each spot (room for the player to stand next to it).
    private static bool SpotsNearRoot(Vector3 rootSpot, Vector3 origin, ResourceRoot root, float reach, float clear, int count, List<Vector3> sapSpots,
        Func<Vector3, bool> accept = null)
    {
        var used = new List<Vector3>();
        for (var i = 0; i < count; i++)
        {
            if (!FindSpot(rootSpot, origin - rootSpot, new[] { 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 3f }, clear, used, out var spot,
                    p => RootGate.FindRoot(p, reach) == root && (accept == null || accept(p))))
            {
                return false;
            }
            sapSpots.Add(spot);
        }
        return true;
    }

    private static IEnumerator RunSap()
    {
        if (!WorldReady(SapName))
        {
            yield break;
        }
        var c = new Checks(SapName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, SapName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            yield return GrowReady(rig, c);
            RootGate.ClearLedger();
            var ygg = PlantCatalog.ByKey("YggaShoot");
            ServerRules.TestRules = new CultivatorRules { GrowTimeMultiplier = 0.1f, RootSapCost = 40 };
            TransplantContent.Rebuild();
            TransplantContent.GrowTimes(ygg, 0.1f, out _, out var hi);
            c.Check(hi < 900f, $"Yggdrasil grow time x0.1 is under 900 s ({F(hi)} s)");
            var sapSpots = new List<Vector3>();
            if (!SpawnRoot(rig, c, taken, out var rootGo, out var root, out var rootSpot))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            // Spots 4 m apart (FindSpot rule), so the second sapling has room next to the first tree.
            if (!c.Check(SpotsNearRoot(rootSpot, rig.Origin, root, 6f, ygg.GrowRadius + 0.3f, 2, sapSpots), "two spots in reach of the root, 4 m apart"))
            {
                c.Report();
                yield break;
            }
            var max = Mathf.RoundToInt(root.m_maxLevel);
            c.Check(max == 50 && Near(root.GetLevel(), 50f, 0.01f), $"untapped root holds {F(root.GetLevel())} of {max}");
            var first = rig.Spawn(ygg.SaplingName, sapSpots[0], Quaternion.identity);
            var second = rig.Spawn(ygg.SaplingName, sapSpots[1], Quaternion.identity);
            yield return Settle();
            var firstView = first.GetComponent<ZNetView>();
            var secondView = second.GetComponent<ZNetView>();
            var firstPlant = first.GetComponent<Plant>();
            var secondPlant = second.GetComponent<Plant>();
            var hover = RootGate.HoverLines(firstPlant) ?? "(null)";
            c.Check(hover == "\nDraws 40 sap from the Ancient Root when grown (root: 50 / 50)", $"T14 hover '{hover.Trim()}'");
            c.Check(firstPlant.GetHoverText().EndsWith(hover, StringComparison.Ordinal), "the sapling's own hover text carries that line");

            // T14: 900 s later it grows and the root is 40 lower.
            Age(firstView, 900.0);
            var existing = LoadedOf(GrownHashes(ygg));
            var grown = new Grown();
            yield return WaitGrown(rig, ygg, firstView, sapSpots[0], 24f, grown, existing);
            if (!c.Check(grown.Ok, $"T14: it grew into a Yggdrasil shoot tree ({F(grown.Waited)} s after 900 s on the clock)"))
            {
                c.Report();
                yield break;
            }
            c.Check(ygg.GrownPrefabs.Contains(Utils.GetPrefabName(grown.Go)) && grown.Go.GetComponent<TreeBase>() != null, $"tree {Utils.GetPrefabName(grown.Go)}");
            var level = root.GetLevel();
            c.Check(level >= 10f && level < 11f, $"root 40 lower (level {F(level)})");

            // T15: the second one waits while the root is too low, stays healthy.
            var secondDrawer = second.GetComponent<RootDrawer>();
            var wait = RootGate.HoverLines(secondPlant) ?? "";
            c.Check(wait.Contains("(root: 10 / 50)") && wait.EndsWith(" - waiting for it to refill", StringComparison.Ordinal), $"T15 hover '{wait.Trim()}'");
            Age(secondView, 900.0);
            // Its first 10 s are over (spawned with the first one): the next passes try it.
            yield return new WaitForSeconds(3f);
            secondPlant.UpdateHealth(900.0);
            c.Check(Alive(secondView) && secondPlant.GetStatus() == Plant.Status.Healthy && secondDrawer.LastState == RootState.Waiting,
                $"due but the root is low: still a young plant, healthy, waiting ({secondPlant.GetStatus()}, {secondDrawer.LastState})");
            c.Check(Near(root.GetLevel(), level, 0.5f), "no sap taken while it waits");

            // Root refills over time (vanilla regeneration: last update moved back, its own tick).
            var regen = root.m_regenPerSec;
            var seconds = regen > 0f ? (42.25f - root.GetLevel()) / regen : 0f;
            c.Note($"root regains {F(regen * 3600f)} sap per hour; {F(seconds)} s moved back on its clock");
            root.m_nview.GetZDO().Set(ZDOVars.s_lastTime, TicksBack(seconds));
            root.UpdateTick();
            c.Check(root.GetLevel() > 40f && root.GetLevel() < 45f, $"root refilled to {F(root.GetLevel())}");
            existing = LoadedOf(GrownHashes(ygg));
            var grown2 = new Grown();
            yield return WaitGrown(rig, ygg, secondView, sapSpots[1], 22f, grown2, existing);
            c.Check(grown2.Ok, $"T15: with the root refilled it grows ({F(grown2.Waited)} s, one try per 10 s)");
            var left = root.GetLevel();
            c.Check(left >= 2f && left < 5f, $"root now about 2 (level {F(left)})");

            // A third one reads about 2 / 50.
            if (grown.Go != null)
            {
                rig.Destroy(grown.Go);
                yield return Settle();
            }
            var third = rig.Spawn(ygg.SaplingName, sapSpots[0], Quaternion.identity);
            yield return Settle();
            var thirdHover = RootGate.HoverLines(third.GetComponent<Plant>()) ?? "";
            c.Check(thirdHover.Contains($"(root: {Mathf.FloorToInt(left)} / 50)") && thirdHover.Contains("waiting for it to refill"), $"third transplant hover '{thirdHover.Trim()}'");
            c.Report();
        }
        finally
        {
            RootGate.ClearLedger();
            rig.Restore();
        }
    }

    // ---------- replant.range (T16, T38) ----------

    private static IEnumerator RunRange()
    {
        if (!WorldReady(RangeName))
        {
            yield break;
        }
        var c = new Checks(RangeName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, RangeName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            yield return GrowReady(rig, c);
            RootGate.ClearLedger();
            var ygg = PlantCatalog.ByKey("YggaShoot");
            var name = ygg.ItemDisplayName;
            var piece = SaplingPiece(ygg);
            var origin = rig.Origin;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 5);
            rig.Give(ygg.ItemName, 1);
            if (!c.Check(tool != null && piece != null, "level 5 cultivator and a Yggdrasil transplant given")
                || !SpawnRoot(rig, c, taken, out var rootGo, out var root, out var rootSpot, p => Buildable(p, 8f)))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            yield return Hold(rig, tool);

            ServerRules.TestRules = new CultivatorRules { RootRange = 20f };
            TransplantContent.Rebuild();
            c.Check(Near(piece.m_connectRadius, 20f), $"RootRange 20: the ghost looks for a root within {F(piece.m_connectRadius)} m");
            // About 15 m from the root: in reach of 20, out of reach of 6.
            if (!c.Check(FindSpot(rootSpot, origin - rootSpot, new[] { 15f, 14f, 16f, 13f, 17f, 18f, 19f, 20f, 22f }, ygg.GrowRadius + 0.3f, taken, out var spot,
                    p => RootGate.FindRoot(p, 19f) == root && RootGate.FindRoot(p, 8f) == null && StandOk(origin, p) && Buildable(p)),
                    "ground spot about 15 m from the root"))
            {
                c.Report();
                yield break;
            }
            c.Note($"spot {F(Vector3.Distance(spot, rootSpot))} m from the root position");
            var placing = new Placing();
            yield return AimGhost(rig, piece, spot, placing);
            c.Check(placing.Status == Player.PlacementStatus.Valid, $"RootRange 20: ghost can be placed 15 m from the root (status {placing.Status})");
            yield return PressPlace(rig, ygg.SaplingHash, placing);
            if (!c.Check(placing.Placed && CountByName(rig.Inv, name) == 0, $"planted there (status {placing.Status})"))
            {
                c.Report();
                yield break;
            }
            var plant = placing.Go.GetComponent<Plant>();
            var drawer = placing.Go.GetComponent<RootDrawer>();
            var level = root.GetLevel();

            // T38: hovered while the range is 20 (root remembered), then the range shrinks.
            var wide = RootGate.HoverLines(plant) ?? "";
            c.Check(wide == "\nDraws 20 sap from the Ancient Root when grown (root: 50 / 50)" && ReferenceEquals(drawer.Root, root),
                $"hovered with RootRange 20: '{wide.Trim()}', root remembered");
            ServerRules.TestRules = new CultivatorRules { RootRange = 6f };
            TransplantContent.Rebuild();
            var narrow = RootGate.HoverLines(plant) ?? "";
            c.Check(narrow == "\nNeeds an Ancient Root within 6 m", $"RootRange back to 6, hovered again at once: '{narrow.Trim()}'");
            yield return new WaitForSeconds(1.2f);
            narrow = RootGate.HoverLines(plant) ?? "";
            c.Check(narrow == "\nNeeds an Ancient Root within 6 m" && drawer.Root == null, $"and a second later (new look-up): '{narrow.Trim()}'");
            c.Check(plant.GetHoverText().Contains("Needs an Ancient Root within 6 m"), $"T16 hover '{OneLine(plant.GetHoverText())}'");

            // 8000 s later: still young, healthy, no sap taken.
            Age(placing.View, 8000.0);
            var until = Time.time + 13f;
            while (Time.time < until && Alive(placing.View) && drawer.LastState != RootState.NoRoot)
            {
                yield return new WaitForSeconds(0.25f);
            }
            yield return new WaitForSeconds(1f);
            c.Check(Alive(placing.View) && drawer.LastState == RootState.NoRoot && plant.GetStatus() == Plant.Status.Healthy,
                $"after 8000 s on the clock it is still a young plant (last grow try: {drawer.LastState}, {plant.GetStatus()})");
            c.Check(Near(root.GetLevel(), level, 0.5f), $"root untouched ({F(root.GetLevel())})");
            c.Check(Replant.TryReplant(player, tool, placing.View, ygg, true, out var why) && !Alive(placing.View) && CountByName(rig.Inv, name) == 1,
                $"Replant gives the transplant back ({why})");
            c.Report();
        }
        finally
        {
            RootGate.ClearLedger();
            rig.Restore();
        }
    }

    // ---------- replant.vanilla (T08: vanilla cultivator pieces) ----------

    private static Piece TablePiece(string prefabName)
    {
        var table = TransplantContent.CultivatorTable;
        if (table == null)
        {
            return null;
        }
        foreach (var go in table.m_pieces)
        {
            if (go != null && go.name == prefabName)
            {
                return go.GetComponent<Piece>();
            }
        }
        return null;
    }

    private static IEnumerator WaitTilled(Vector3 p, bool tilled, float seconds)
    {
        var until = Time.time + seconds;
        while (Time.time < until && Untilled(p) == tilled)
        {
            yield return new WaitForSeconds(0.2f);
        }
    }

    private static IEnumerator RunVanilla()
    {
        if (!WorldReady(VanillaName))
        {
            yield break;
        }
        var c = new Checks(VanillaName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, VanillaName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var cultivate = TablePiece("cultivate_v2");
            var grass = TablePiece("replant_v2");
            var carrot = TablePiece("sapling_carrot");
            var table = TransplantContent.CultivatorTable;
            c.Check(tool != null && cultivate != null && grass != null && carrot != null, "vanilla cultivator pieces cultivate_v2, replant_v2, sapling_carrot in its table");
            if (table != null && table.m_pieces.Count > 0)
            {
                var firstOurs = table.m_pieces.FindIndex(g => g != null && PlantCatalog.TryFind(g.name.GetStableHashCode(), out _, out var sap) && sap);
                c.Check(firstOurs > 0 && table.m_pieces.Skip(firstOurs).All(g => g != null && g.name.StartsWith("MC_Sapling_", StringComparison.Ordinal)),
                    $"vanilla entries first, ours added after them (first of ours at {firstOurs} of {table.m_pieces.Count})");
            }
            bool Free(Vector3 p) => Buildable(p) && Untilled(p);
            var wild = Vector3.zero;
            if (cultivate == null || grass == null || carrot == null
                || !c.Check(FindPlantSpot(rig, taken, 1.5f, out var spot, Free) && FindPlantSpot(rig, taken, 1.5f, out wild, Free), "two free untilled spots"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            var seeds = rig.Give("CarrotSeeds", 1);
            c.Check(seeds != null, "carrot seeds given");

            // Carrot seed needs tilled soil.
            var onWild = new Placing();
            yield return AimGhost(rig, carrot, wild, onWild);
            c.Check(onWild.Status == Player.PlacementStatus.NeedCultivated, $"carrot seed on untilled ground: needs cultivated ground (status {onWild.Status})");

            // Cultivate.
            var placing = new Placing();
            yield return AimGhost(rig, cultivate, spot, placing);
            c.Check(placing.Status == Player.PlacementStatus.Valid, $"cultivate ghost can be placed (status {placing.Status})");
            var wear = tool.m_durability;
            yield return ReadyForKey(player);
            yield return Tap("Attack");
            yield return WaitTilled(spot, true, 5f);
            c.Check(!Untilled(spot) && tool.m_durability < wear, "ground cultivated, cultivator worn");

            // Carrot seed on it.
            var onSoil = new Placing();
            yield return AimGhost(rig, carrot, spot, onSoil);
            c.Check(onSoil.Status == Player.PlacementStatus.Valid, $"carrot seed on cultivated ground can be placed (status {onSoil.Status})");
            yield return PressPlace(rig, "sapling_carrot".GetStableHashCode(), onSoil);
            c.Check(onSoil.Placed && Have(rig, "CarrotSeeds") == 0, "carrot planted for one seed");
            if (onSoil.Go != null)
            {
                rig.Destroy(onSoil.Go);
                yield return Settle();
            }

            // Grass.
            var regrass = new Placing();
            yield return AimGhost(rig, grass, spot, regrass);
            c.Check(regrass.Status == Player.PlacementStatus.Valid, $"Grass ghost can be placed (status {regrass.Status})");
            yield return ReadyForKey(player);
            yield return Tap("Attack");
            yield return WaitTilled(spot, false, 5f);
            c.Check(Untilled(spot), "Grass put the ground back to grass");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }
}
#endif
