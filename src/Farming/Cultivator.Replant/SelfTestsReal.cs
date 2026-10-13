#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Self tests that do the real thing where an older test used a shortcut (same class as
// SelfTests.cs), and the registration of every test added after the first sets (RegisterNew, called from
// SelfTests.Register):
//   replant.trunk         T14: the new Yggdrasil tree plays the game's grow animation (trunk hidden, a growing copy of
//                         it goes from small to full size, trunk shown again), read frame by frame
//   replant.dungeon       T24: one trip to the nearest Black Forest dungeon of the world (Burial Chamber or Troll
//                         Cave), in through its real entrance: E digs a yellow mushroom up, the transplant ghost is
//                         refused and the place button plants nothing
//   replant.e.planteasily.dungeon  E04 c: the same trip with PlantEasily's placement patches on: ghost still refused,
//                         the place button plants nothing, no grid copy either
//   replant.helm.water    T33: a Raft on real water at the nearest coast, the player on board takes the helm the
//                         game's way and gets the cultivator with a hotbar key: no hint over a bush on the shore, E
//                         lets go and digs nothing, next E digs; gamepad X at the helm digs nothing
//   replant.helm.lox      T33 (optional part): the same in the saddle of a tame, saddled Lox with its AI on
//   replant.mp.noserver   M13 (scenario vanilla-server, registered at load: the mod is inactive there): no Replant
//                         hint, E digs nothing, no transplant in the build menu, cultivator back to 3 levels
//   replant.mp.offgrow    M14 (scenario modded): the server's clock is skipped two days (what skiptime does); a young
//                         transplant stays young on the server's rules, then this game turns the mod off and it grows
//                         on this player's own GrowTimeMultiplier 0.1
internal static partial class SelfTests
{
    private const string TrunkName = "replant.trunk";
    private const string DungeonName = "replant.dungeon";
    private const string EasyDungeonName = "replant.e.planteasily.dungeon";
    private const string HelmWaterName = "replant.helm.water";
    private const string HelmLoxName = "replant.helm.lox";
    private const string MpNoServerName = "replant.mp.noserver";
    private const string MpOffGrowName = "replant.mp.offgrow";
    private const string SkipStep = "replant.mp.s.skip";

    private const string NoServerScenario = "vanilla-server";

    private static readonly KeyValuePair<string, Func<IEnumerator>>[] NewTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(TrunkName, RunTrunk),
        new KeyValuePair<string, Func<IEnumerator>>(IdolRefineName, RunIdolRefine),
        new KeyValuePair<string, Func<IEnumerator>>(SearchName, RunSearch),
        new KeyValuePair<string, Func<IEnumerator>>(EncyclopediaName, RunEncyclopedia),
        new KeyValuePair<string, Func<IEnumerator>>(LootFilterName, RunLootFilter),
        new KeyValuePair<string, Func<IEnumerator>>(SpyglassViewName, RunSpyglassView),
        new KeyValuePair<string, Func<IEnumerator>>(EasyGridName, RunEasyGrid),
        new KeyValuePair<string, Func<IEnumerator>>(EasyRootName, RunEasyRoot),
        new KeyValuePair<string, Func<IEnumerator>>(InputPadName, RunInputPad),
        new KeyValuePair<string, Func<IEnumerator>>(InputLayoutsName, RunInputLayouts),
        new KeyValuePair<string, Func<IEnumerator>>(InputRebindName, RunInputRebind),
        new KeyValuePair<string, Func<IEnumerator>>(HelmLoxName, RunHelmLox),
        new KeyValuePair<string, Func<IEnumerator>>(HelmWaterName, RunHelmWater),
        new KeyValuePair<string, Func<IEnumerator>>(DungeonName, RunDungeon),
        new KeyValuePair<string, Func<IEnumerator>>(EasyDungeonName, RunEasyDungeon),
    };

    // SelfTests.Register (OnActivated), after the older sets.
    private static void RegisterNew()
    {
        foreach (var test in NewTests)
        {
            SelfTest.Register(test.Key, test.Value);
        }
        // replant.log reads what the whole run logged: it stays the last one.
        SelfTest.Register(LogName, RunLog);
        RegisterStaged();
        SelfTest.RegisterMultiplayer(MpOffGrowName, SelfTest.Modded, RunMpOffGrow);
        SelfTest.RegisterServerStep(SkipStep, ServerSkipStep);
    }

    private static void UnregisterNew()
    {
        foreach (var test in NewTests)
        {
            SelfTest.Unregister(test.Key);
        }
        UnregisterStaged();
        SelfTest.UnregisterMultiplayer(MpOffGrowName);
        SelfTest.UnregisterServerStep(SkipStep);
    }

    // Plugin.BindConfig (RegisterAtLoad): on a server without the mod this mod never activates.
    private static void RegisterNoServerTest()
    {
        SelfTest.RegisterMultiplayer(MpNoServerName, NoServerScenario, RunMpNoServer);
    }

    // ---------- tools ----------

    // Item drops that were not there at the snapshot, within radius of center: destroyed (a far test place is out of
    // the Rig's reach, it cleans around the test start only).
    private static void DestroyNewDrops(HashSet<ItemDrop> before, Vector3 center, float radius)
    {
        if (before == null || ZNetScene.instance == null)
        {
            return;
        }
        foreach (var drop in ItemDrop.s_instances.ToArray())
        {
            if (drop != null && !before.Contains(drop) && (drop.transform.position - center).sqrMagnitude < radius * radius)
            {
                DestroyOwned(drop.gameObject);
            }
        }
    }

    // What the game's own hover ray (crosshair, interact reach) hits: its networked object, or null.
    private static ZNetView UnderCrosshair(Player p)
    {
        p.FindHoverObject(out var hover, out _);
        return hover != null ? hover.GetComponentInParent<ZNetView>() : null;
    }

    private static void GoHomeIfCutShort(Player player, Vector3 home, bool away)
    {
        if (away && player != null && (player.transform.position - home).sqrMagnitude > 30f * 30f && !player.IsTeleporting())
        {
            // Cut short away from home (timeout, error): at least start the trip back.
            player.TeleportTo(home, player.transform.rotation, true);
        }
    }

    // ---------- replant.trunk (T14: the grow animation of the new tree) ----------

    private static Transform TrunkCopy(TreeBase tree)
    {
        if (tree == null || tree.m_trunk == null)
        {
            return null;
        }
        var want = tree.m_trunk.name + "(Clone)";
        foreach (Transform child in tree.transform)
        {
            if (child != null && child.gameObject != tree.m_trunk && child.name == want)
            {
                return child;
            }
        }
        return null;
    }

    private static IEnumerator RunTrunk()
    {
        if (!WorldReady(TrunkName))
        {
            yield break;
        }
        var c = new Checks(TrunkName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, TrunkName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            yield return GrowReady(rig, c);
            RootGate.ClearLedger();
            var ygg = PlantCatalog.ByKey("YggaShoot");
            ServerRules.TestRules = new CultivatorRules { GrowTimeMultiplier = 0.1f, RootSapCost = 40 };
            TransplantContent.Rebuild();
            var spots = new List<Vector3>();
            if (!SpawnRoot(rig, c, taken, out _, out var root, out var rootSpot))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            if (!c.Check(SpotsNearRoot(rootSpot, rig.Origin, root, 6f, ygg.GrowRadius + 0.3f, 1, spots), "a spot in reach of the root"))
            {
                c.Report();
                yield break;
            }
            var sapling = rig.Spawn(ygg.SaplingName, spots[0], Quaternion.identity);
            yield return Settle();
            var view = sapling != null ? sapling.GetComponent<ZNetView>() : null;
            if (!c.Check(Alive(view), "Yggdrasil transplant planted near a full root"))
            {
                c.Report();
                yield break;
            }
            var existing = LoadedOf(GrownHashes(ygg));
            var hashes = GrownHashes(ygg).ToArray();
            Age(view, 900.0);

            // Every frame: the moment the tree stands there, its trunk is looked at.
            ZNetView treeView = null;
            var start = Time.time;
            while (Time.time - start < 26f && treeView == null)
            {
                if (!Alive(view))
                {
                    treeView = FindNear(hashes, spots[0], 0.8f, existing);
                }
                if (treeView == null)
                {
                    yield return null;
                }
            }
            if (!c.Check(treeView != null, $"it grew into a Yggdrasil shoot tree ({F(Time.time - start)} s after 900 s on the clock)"))
            {
                c.Report();
                yield break;
            }
            rig.Track(treeView.gameObject);
            var tree = treeView.GetComponent<TreeBase>();
            var trunk = tree != null ? tree.m_trunk : null;
            if (!c.Check(trunk != null, $"the new tree {Utils.GetPrefabName(treeView.gameObject)} has a trunk"))
            {
                c.Report();
                yield break;
            }
            var full = Mathf.Max(0.0001f, trunk.transform.localScale.x);
            var hidden = 0;
            var copies = 0;
            var smallest = float.MaxValue;
            var largest = 0f;
            var last = -1f;
            var rising = true;
            // Copy is a thing the camera can draw, where the trunk stands: object on, a renderer on, same place.
            var drawn = true;
            var began = Time.time;
            var took = 0f;
            while (Time.time - began < 2f)
            {
                if (!trunk.activeSelf)
                {
                    hidden++;
                    var copy = TrunkCopy(tree);
                    if (copy != null)
                    {
                        copies++;
                        drawn &= copy.gameObject.activeInHierarchy && copy.GetComponentsInChildren<Renderer>().Any(r => r != null && r.enabled)
                                 && Vector3.Distance(copy.position, trunk.transform.position) < 0.05f;
                        var size = copy.localScale.x / full;
                        smallest = Mathf.Min(smallest, size);
                        largest = Mathf.Max(largest, size);
                        rising &= size >= last - 0.001f;
                        last = size;
                    }
                }
                else if (hidden > 0)
                {
                    took = Time.time - began;
                    break;
                }
                yield return null;
            }
            yield return null;
            c.Note($"trunk hidden for {hidden} frame(s) ({F(took)} s), growing copy seen in {copies}, size {F(smallest)} -> {F(largest)} of the trunk");
            c.Check(hidden >= 2 && copies >= 2, $"the grow animation runs on the new tree: trunk hidden while a copy of it grows ({hidden} frame(s), copy seen {copies} time(s))");
            c.Check(copies >= 2 && rising && smallest < 0.6f && largest > smallest && largest <= 1.01f,
                $"the copy grows from small to the trunk's size, never shrinking ({F(smallest)} -> {F(largest)}, rising {rising})");
            c.Check(copies >= 2 && drawn, $"the growing copy is a drawn object at the trunk's place in every frame seen (shown, with a renderer that is on: {drawn})");
            c.Check(trunk.activeSelf && TrunkCopy(tree) == null && took > 0f && took < 1.5f, $"then the trunk itself stands there at full size ({F(took)} s after the tree appeared)");
            c.Report();
        }
        finally
        {
            RootGate.ClearLedger();
            rig.Restore();
        }
    }

    // ---------- replant.dungeon (T24, E04 c) ----------

    // What a far test leaves at its place, for the clean-up before the trip home.
    private sealed class FarPlace
    {
        internal HashSet<ItemDrop> DropsBefore;
        internal Vector3 At;
        internal readonly List<ZNetView> Planted = new List<ZNetView>();
    }

    private static void CleanFarPlace(Rig rig, FarPlace place, float radius)
    {
        foreach (var view in place.Planted.Where(Alive).ToList())
        {
            DestroyOwned(view.gameObject);
        }
        place.Planted.Clear();
        rig.Restore();
        DestroyNewDrops(place.DropsBefore, place.At, radius);
        place.DropsBefore = null;
    }

    // Nearest Black Forest location with an interior (Burial Chamber, Troll Cave). Hildir's tomb (a boss lives in it)
    // left out.
    private static bool NearestDungeon(Vector3 from, out ZoneSystem.LocationInstance found, out float distance)
    {
        found = default;
        distance = float.MaxValue;
        var have = false;
        foreach (var instance in ZoneSystem.instance.m_locationInstances.Values)
        {
            var location = instance.m_location;
            if (location == null || (location.m_biome & Heightmap.Biome.BlackForest) == 0)
            {
                continue;
            }
            var prefab = (location.m_prefabName ?? "").ToLowerInvariant();
            var interior = location.m_interiorRadius > 0f || prefab.Contains("crypt") || prefab.Contains("trollcave");
            if (!interior || prefab.Contains("hildir"))
            {
                continue;
            }
            var d = Utils.DistanceXZ(instance.m_position, from);
            if (d < distance)
            {
                distance = d;
                found = instance;
                have = true;
            }
        }
        return have;
    }

    // Floor point in front of the player inside a dungeon: about level with the feet, free, in sight of the eye.
    private static bool DungeonFloor(Player p, List<Vector3> used, out Vector3 floor)
    {
        floor = Vector3.zero;
        var mask = RoofMask;
        var feet = p.transform.position;
        var forward = p.transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
        foreach (var distance in new[] { 2f, 2.4f, 1.7f, 2.8f })
        {
            for (var step = 0; step < 24; step++)
            {
                var angle = (step % 2 == 0 ? 1f : -1f) * ((step + 1) / 2) * 15f;
                var at = feet + Quaternion.Euler(0f, angle, 0f) * forward * distance;
                if (!Physics.Raycast(at + Vector3.up * 1.2f, Vector3.down, out var hit, 2.4f, mask) || Mathf.Abs(hit.point.y - feet.y) > 0.6f || hit.normal.y < 0.8f)
                {
                    continue;
                }
                var point = hit.point;
                if (used.Any(u => (u - point).sqrMagnitude < 1f))
                {
                    continue;
                }
                if (Physics.CheckSphere(point + Vector3.up * 0.6f, 0.35f, SpaceMask, QueryTriggerInteraction.Ignore)
                    || Physics.Linecast(p.m_eye.position, point + Vector3.up * 0.3f, mask))
                {
                    continue;
                }
                floor = point;
                used.Add(point);
                return true;
            }
        }
        return false;
    }

    // Crosshair on something the game can judge a placement on: same ray, layers and reach as vanilla
    // Player.PieceRayTest. False = the game would say "no ray hit" and a refused ghost there would prove nothing.
    private static bool PlaceRayHits(Player player, Piece piece, out string what)
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
        var reach = player.m_maxPlaceDistance + (piece != null ? piece.m_extraPlacementDistance : 0);
        var far = Vector3.Distance(player.m_eye.position, hit.point);
        what = $"{hit.collider.name}, {F(far)} m from the eye, reach {F(reach)} m";
        return hit.collider.attachedRigidbody == null && far < reach;
    }

    private static IEnumerator RunDungeon() => DungeonTrip(DungeonName, false);

    // E04 c: own test, PlantEasily is another author's mod (missing = FAIL, nothing checked).
    private static IEnumerator RunEasyDungeon()
    {
        if (!NeedPlantEasily(EasyDungeonName, "E04 c", out _, out _))
        {
            yield break;
        }
        yield return DungeonTrip(EasyDungeonName, true);
    }

    // One trip to the nearest dungeon and back. easy = the PlantEasily part (planting refused with its placement
    // patches on) instead of the dig.
    private static IEnumerator DungeonTrip(string test, bool easy)
    {
        if (!WorldReady(test))
        {
            yield break;
        }
        var c = new Checks(test);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, test);
        var home = rig.Origin;
        var away = false;
        var place = new FarPlace();
        try
        {
            PrepareInput(rig);
            var kind = PlantCatalog.ByKey("MushroomYellow");
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            if (!c.Check(tool != null && SaplingPiece(kind) != null && WorldGenerator.instance != null, "cultivator given")
                || !c.Check(NearestDungeon(home, out var dungeon, out var far), "this world has a Black Forest location with an interior (Burial Chamber or Troll Cave)"))
            {
                c.Report();
                yield break;
            }
            c.Note($"nearest: {dungeon.m_location.m_prefabName}, {F(far)} m from the start");
            var toHome = home - dungeon.m_position;
            toHome.y = 0f;
            toHome = toHome.sqrMagnitude > 1f ? toHome.normalized : Vector3.forward;
            var arrive = dungeon.m_position + toHome * 10f;
            arrive.y = Mathf.Max(WorldGenerator.instance.GetHeight(arrive.x, arrive.z), ZoneSystem.instance.m_waterLevel) + 1.5f;
            var trip = new Trip();
            away = true;
            yield return Travel(player, arrive, trip);
            if (c.Check(trip.Ok, $"travelled to the dungeon ({F(trip.Seconds)} s)" + (trip.Ok ? "" : ": " + trip.Why)))
            {
                yield return DungeonBody(c, rig, dungeon.m_position, tool, kind, place, easy);
            }
            // Things of the test go before the place unloads, then the trip home.
            CleanFarPlace(rig, place, 40f);
            var homeTrip = new Trip();
            yield return Travel(player, home, homeTrip);
            away = !homeTrip.Ok;
            c.Check(homeTrip.Ok, "travelled back to the start" + (homeTrip.Ok ? $" ({F(homeTrip.Seconds)} s)" : ": " + homeTrip.Why));
            c.Report();
        }
        finally
        {
            CleanFarPlace(rig, place, 40f);
            GoHomeIfCutShort(player, home, away);
        }
    }

    // At the dungeon's door. A failed check that makes the rest pointless ends the body: the caller cleans up, goes
    // home and reports.
    private static IEnumerator DungeonBody(Checks c, Rig rig, Vector3 at, ItemDrop.ItemData tool, PlantKind kind, FarPlace place, bool easy)
    {
        var player = rig.Player;
        var name = kind.ItemDisplayName;
        var piece = SaplingPiece(kind);
        RemoveCreatures(player, 50f);
        yield return WaitFor(() => Location.GetZoneLocation(at) != null, 12f);
        var location = Location.GetZoneLocation(at);
        var doors = location != null ? location.GetComponentsInChildren<Teleport>(true) : new Teleport[0];
        var entrance = doors.FirstOrDefault(t => t != null && !Character.InInterior(t.transform.position) && t.m_targetPoint != null
                                                 && Character.InInterior(t.m_targetPoint.transform.position));
        if (!c.Check(location != null && location.m_hasInterior && entrance != null,
                $"the location stands there with an interior and an entrance ({(location != null ? location.name : "not loaded")}, interior {(location != null && location.m_hasInterior)}, {doors.Length} door(s))"))
        {
            yield break;
        }

        // In through the real entrance (what E on it, or walking into it, calls).
        var enterUntil = Time.realtimeSinceStartup + 6f;
        while (!player.IsTeleporting() && Time.realtimeSinceStartup < enterUntil && !entrance.Interact(player, false, false))
        {
            yield return null;
        }
        yield return WaitFor(() => !player.IsTeleporting() && player.InInterior(), 20f);
        if (!c.Check(player.InInterior() && !player.IsTeleporting(), $"inside the dungeon through its entrance (height {F(player.transform.position.y)} m)"))
        {
            yield break;
        }
        yield return new WaitForSeconds(1f);
        place.At = player.transform.position;
        place.DropsBefore = new HashSet<ItemDrop>(ItemDrop.s_instances);
        c.Note($"{RemoveCreatures(player, 40f)} creature(s) removed inside; no-build zone here: {Location.IsInsideNoBuildLocation(place.At)}");
        yield return Hold(rig, tool);

        var used = new List<Vector3>();
        if (!easy)
        {
            // Dig a yellow mushroom up, with E, the crosshair on it.
            ZNetView wildView = null;
            var why = "";
            for (var attempt = 0; attempt < 4 && wildView == null; attempt++)
            {
                if (!DungeonFloor(player, used, out var floor))
                {
                    why += " (no free floor in sight)";
                    break;
                }
                var wild = rig.Spawn("Pickable_Mushroom_yellow", floor, Quaternion.identity);
                yield return Settle();
                yield return rig.AimAt(AimPoint(wild));
                yield return Frames(3);
                var target = Replant.Target;
                if (target.IsFresh && ReferenceEquals(target.View, wild.GetComponent<ZNetView>()))
                {
                    wildView = target.View;
                    break;
                }
                why += $" (try {attempt + 1}: {RayNote(player)})";
                rig.Destroy(wild);
                yield return Settle();
            }
            if (!c.Check(wildView != null, "a yellow mushroom on the dungeon floor is under the crosshair within reach" + why))
            {
                yield break;
            }
            c.Check(HintText().Contains("Replant") && CrosshairYellow(), $"inside the dungeon: Replant hint over it ('{OneLine(HintText())}')");
            ClearCenter();
            yield return ReadyForKey(player);
            yield return Tap(UseAndSnap);
            yield return WaitGone(wildView, 1.5f);
            c.Check(!Alive(wildView) && CountByName(rig.Inv, name) == 1, $"E digs the yellow mushroom up inside the dungeon ({CountByName(rig.Inv, name)} transplant)");
            c.Check(CenterText() != L("$msg_nobuildzone"), $"no 'cannot build here' refusal ('{CenterText()}')");
        }
        else if (!c.Check(DungeonFloor(player, used, out _), "a free point of the dungeon floor in sight"))
        {
            yield break;
        }

        // Planting a transplant there: refused by the game as usual.
        if (CountByName(rig.Inv, name) < 1)
        {
            rig.Give(kind.ItemName, 1);
        }
        var count = CountByName(rig.Inv, name);
        var hashes = new[] { kind.SaplingHash };
        var standing = LoadedOf(hashes);
        var spot = used[used.Count - 1];
        player.UpdateKnownRecipesList();
        player.UpdateAvailablePiecesList();
        var picked = player.SetSelectedPiece(piece);
        var who = "";
        if (easy)
        {
            // E04 c: PlantEasily's placement patches on again (PrepareInput took them off). What it alone says of the
            // spot (this mod's second look off for a moment) is only noted: in a dungeon it may hide the ghost.
            var back = PutPlantEasilyBack(rig.Name);
            who = "PlantEasily on: ";
            GhostVerdict.TestOff = true;
            yield return rig.AimAt(spot);
            yield return Frames(8);
            c.Note($"{back} PlantEasily patches back; without this mod's second look the ghost in the dungeon has status {player.m_placementStatus}");
            GhostVerdict.TestOff = false;
        }
        yield return rig.AimAt(spot);
        yield return Frames(easy ? 8 : 4);
        var status = player.m_placementStatus;
        // Control: the crosshair really is on the floor within placing reach. Aimed at nothing, the ghost is "refused"
        // too and the place button plants nothing anywhere: that would prove nothing about the dungeon.
        var onFloor = PlaceRayHits(player, piece, out var rayOn);
        c.Check(onFloor && player.InInterior(), $"{who}the crosshair is on the dungeon floor within placing reach, player still inside ({rayOn})");
        // Game alone (PlantEasily's patches off): a ghost must be there and judged on that floor, so "no ray hit" is
        // not a refusal of the dungeon. PlantEasily on: it may move the ghost out of sight, any status but Valid.
        var judged = easy || (player.m_placementGhost != null && status != Player.PlacementStatus.NoRayHits);
        c.Check(picked && judged && status != Player.PlacementStatus.Valid,
            $"{who}transplant picked for planting, crosshair on the dungeon floor: the ghost is refused (status {status}; inside a dungeon the game says NotInDungeon, NoBuildZone or Invalid)");
        ClearCenter();
        yield return ReadyForKey(player);
        yield return Tap("Attack");
        yield return new WaitForSeconds(easy ? 1.5f : 0.3f);
        place.Planted.AddRange(LoadedOf(hashes).Where(v => !standing.Contains(v)));
        c.Check(place.Planted.Count == 0 && CountByName(rig.Inv, name) == count,
            $"{who}the place button plants nothing in the dungeon{(easy ? ", no grid copy either," : "")} and uses no transplant ({place.Planted.Count} planted, {count} -> {CountByName(rig.Inv, name)}; centre text '{CenterText()}')");
    }

    // ---------- replant.helm.water (T33: a ship on real water) ----------

    private sealed class Coast
    {
        internal bool Ok;
        // Waterline point and the flat direction from the land out to sea there.
        internal Vector3 Shore;
        internal Vector3 Out;
        internal float Far;
    }

    // Nearest coast from the world generator (no zone loaded): rings around center, 24 directions; where one step of a
    // ring goes from dry land to water, the waterline is found by halving. One ring per frame, 8 s at most.
    private static IEnumerator FindCoast(Vector3 center, Coast coast)
    {
        coast.Ok = false;
        var wg = WorldGenerator.instance;
        if (wg == null || ZoneSystem.instance == null)
        {
            yield break;
        }
        var water = ZoneSystem.instance.m_waterLevel;
        var until = Time.realtimeSinceStartup + 8f;
        const float ring = 40f;
        for (var r = ring; r <= 4000f; r += ring)
        {
            for (var a = 0; a < 24; a++)
            {
                var dir = Quaternion.Euler(0f, a * 15f, 0f) * Vector3.forward;
                var land = center + dir * (r - ring);
                var sea = center + dir * r;
                if (wg.GetHeight(land.x, land.z) < water + 1f || wg.GetHeight(sea.x, sea.z) > water - 1f)
                {
                    continue;
                }
                var lo = r - ring;
                var hi = r;
                for (var i = 0; i < 9; i++)
                {
                    var mid = (lo + hi) * 0.5f;
                    var p = center + dir * mid;
                    if (wg.GetHeight(p.x, p.z) > water)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }
                var shore = center + dir * hi;
                var dry = shore - dir * 6f;
                var wet = shore + dir * 5f;
                if (wg.GetHeight(dry.x, dry.z) < water + 0.2f || wg.GetHeight(wet.x, wet.z) > water - 0.2f)
                {
                    continue;
                }
                shore.y = water;
                coast.Shore = shore;
                coast.Out = dir;
                coast.Far = hi;
                coast.Ok = true;
                yield break;
            }
            yield return null;
            if (Time.realtimeSinceStartup > until)
            {
                yield break;
            }
        }
    }

    private static IEnumerator RunHelmWater()
    {
        if (!WorldReady(HelmWaterName))
        {
            yield break;
        }
        var c = new Checks(HelmWaterName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, HelmWaterName);
        var home = rig.Origin;
        var away = false;
        var place = new FarPlace();
        try
        {
            PrepareInput(rig);
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var shipName = ZNetScene.instance.GetPrefab("Raft") != null ? "Raft" : "Karve";
            var shipPrefab = ZNetScene.instance.GetPrefab(shipName);
            var prefabShip = shipPrefab != null ? shipPrefab.GetComponent<Ship>() : null;
            var prefabHelm = prefabShip != null ? prefabShip.m_shipControlls : null;
            var coast = new Coast();
            yield return FindCoast(home, coast);
            if (!c.Check(tool != null && prefabHelm != null && prefabHelm.m_attachPoint != null && WorldGenerator.instance != null, $"cultivator given, ship {shipName} with a helm known to the game")
                || !c.Check(coast.Ok, "a coast (dry land next to water) found in the world"))
            {
                c.Report();
                yield break;
            }
            var water = ZoneSystem.instance.m_waterLevel;
            var arrive = coast.Shore - coast.Out * 6f;
            arrive.y = Mathf.Max(WorldGenerator.instance.GetHeight(arrive.x, arrive.z), water) + 1.5f;
            var trip = new Trip();
            away = true;
            yield return Travel(player, arrive, trip);
            c.Note($"coast {F(coast.Far)} m from the start, travelled there in {F(trip.Seconds)} s");
            if (c.Check(trip.Ok, "arrived at the coast" + (trip.Ok ? "" : ": " + trip.Why)))
            {
                place.At = player.transform.position;
                place.DropsBefore = new HashSet<ItemDrop>(ItemDrop.s_instances);
                yield return HelmWaterBody(c, rig, coast, tool, shipName, place);
            }
            if (player.m_doodadController != null)
            {
                player.StopDoodadControl();
            }
            // Things of the test go before the place unloads, then the trip home.
            CleanFarPlace(rig, place, 60f);
            var homeTrip = new Trip();
            yield return Travel(player, home, homeTrip);
            away = !homeTrip.Ok;
            c.Check(homeTrip.Ok, "travelled back to the start" + (homeTrip.Ok ? $" ({F(homeTrip.Seconds)} s)" : ": " + homeTrip.Why));
            c.Report();
        }
        finally
        {
            if (player != null && player.m_doodadController != null)
            {
                player.StopDoodadControl();
            }
            CleanFarPlace(rig, place, 60f);
            GoHomeIfCutShort(player, home, away);
        }
    }

    // At the coast. A failed check that makes the rest pointless ends the body: the caller cleans up, goes home and
    // reports.
    private static IEnumerator HelmWaterBody(Checks c, Rig rig, Coast coast, ItemDrop.ItemData tool, string shipName, FarPlace place)
    {
        var player = rig.Player;
        var inv = rig.Inv;
        var rasp = PlantCatalog.ByKey("RaspberryBush");
        var name = rasp.ItemDisplayName;
        var water = ZoneSystem.instance.m_waterLevel;
        var shipPrefab = ZNetScene.instance.GetPrefab(shipName);
        var prefabHelm = shipPrefab.GetComponent<Ship>().m_shipControlls;
        RemoveCreatures(player, 50f);
        rig.Noon();
        yield return new WaitForSeconds(0.5f);

        // Real waterline on the loaded ground, along the line from the land out to sea.
        var line = float.NaN;
        for (var s = -8f; s <= 25f; s += 0.25f)
        {
            if (Ground(coast.Shore + coast.Out * s) < water - 0.05f)
            {
                line = s;
                break;
            }
        }
        if (!c.Check(!float.IsNaN(line), "the ground goes under water along the shore line found"))
        {
            yield break;
        }
        // Ship: helm 1.2 m out from the waterline, on the land side of the hull.
        var helmWant = coast.Shore + coast.Out * (line + 1.2f);
        var local = shipPrefab.transform.InverseTransformPoint(prefabHelm.m_attachPoint.position);
        local.y = 0f;
        var yaw = local.magnitude > 0.3f ? Vector3.SignedAngle(local, -coast.Out, Vector3.up) : Quaternion.LookRotation(coast.Out).eulerAngles.y;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        var shipAt = helmWant - rot * local;
        shipAt.y = water + 0.3f;
        var shipGo = rig.Spawn(shipName, shipAt, rot);
        var ship = shipGo != null ? shipGo.GetComponent<Ship>() : null;
        var helm = ship != null ? ship.m_shipControlls : null;
        if (!c.Check(helm != null && helm.m_attachPoint != null, $"{shipName} spawned on the water at the shore"))
        {
            yield break;
        }
        yield return new WaitForSeconds(3f);
        var under = Ground(shipGo.transform.position);
        c.Check(under < water && ship.m_body != null && !ship.m_body.isKinematic && Mathf.Abs(shipGo.transform.position.y - water) < 1.5f,
            $"the {shipName} lies on water, free to move: ground {F(water - under)} m below the surface under its middle, hull {F(shipGo.transform.position.y - water)} m from the surface");

        // On board, then the helm the game's way: crosshair on it, Use.
        var right = player.GetRightItem();
        if (right != null)
        {
            player.UnequipItem(right, false);
        }
        Put(player, helm.m_attachPoint.position + Vector3.up * 0.4f);
        yield return WaitFor(() => player.GetStandingOnShip() == ship && ship.IsPlayerInBoat(player), 5f);
        if (!c.Check(player.GetStandingOnShip() == ship && ship.IsPlayerInBoat(player),
                $"the player stands on the {shipName} (standing on it {player.GetStandingOnShip() == ship}, on board {ship.IsPlayerInBoat(player)})"))
        {
            yield break;
        }
        yield return rig.AimAt(AimPoint(helm.gameObject));
        yield return Frames(3);
        var hovered = player.m_hovering != null && player.m_hovering.GetComponentInParent<ShipControlls>() == helm;
        if (hovered)
        {
            yield return ReadyForKey(player);
            yield return Tap(Replant.KeyboardButton);
            yield return WaitFor(() => player.m_doodadController != null, 1.5f);
        }
        if (player.m_doodadController == null)
        {
            c.Note($"E with the crosshair on the helm did not take it (helm under the crosshair: {hovered}): the helm's own use call is made instead");
            helm.Interact(player, false, false);
            yield return WaitFor(() => player.m_doodadController != null, 1.5f);
        }
        if (!c.Check(ReferenceEquals(player.m_doodadController, helm) && player.IsAttachedToShip(), "the player sits at the helm, attached to the ship"))
        {
            yield break;
        }

        // Cultivator with a hotbar key.
        var slot = -1;
        for (var x = 0; x < Mathf.Min(8, inv.GetWidth()) && slot < 0; x++)
        {
            var at = inv.GetItemAt(x, 0);
            if (at == null || ReferenceEquals(at, tool))
            {
                slot = x;
            }
        }
        if (c.Check(slot >= 0, "a free hotbar slot for the cultivator"))
        {
            tool.m_gridPos = new Vector2i(slot, 0);
            inv.Changed();
            yield return Frames(2);
            yield return Tap("Hotbar" + (slot + 1));
            yield return WaitFor(() => ReferenceEquals(player.GetRightItem(), tool), 4f);
        }
        if (!c.Check(ReferenceEquals(player.GetRightItem(), tool), "the hotbar key puts the cultivator in hand at the helm"))
        {
            yield return Hold(rig, tool);
        }
        if (!c.Check(player.m_doodadController != null && player.InPlaceMode() && ReferenceEquals(player.GetRightItem(), tool), "still at the helm, cultivator in hand, build mode"))
        {
            yield break;
        }

        // Dry spots around the helm, nearest first; the bush goes on the first one where the game's own hover ray
        // (crosshair, interact reach) really finds it from the helm: hull, rudder and mast may stand in the way.
        var helmAt = helm.m_attachPoint.position;
        var dry = new List<Vector3>();
        for (var r = 1.5f; r <= 4.2f; r += 0.3f)
        {
            for (var a = 0; a < 24; a++)
            {
                var q = helmAt + Quaternion.Euler(0f, a * 15f, 0f) * Vector3.forward * r;
                q.y = Ground(q);
                if (q.y >= water + 0.12f && !dry.Any(d => (d - q).sqrMagnitude < 1.2f * 1.2f))
                {
                    dry.Add(q);
                }
            }
        }
        if (!c.Check(dry.Count > 0, "dry ground within reach of the helm (4.2 m)"))
        {
            yield break;
        }
        var found = new SeatBush();
        yield return BushFromSeat(rig, dry, helmAt, 6, found);
        if (!c.Check(found.Go != null, $"from the helm a bush on the shore is under the crosshair within reach ({dry.Count} dry spot(s) near the helm)" + found.Sight))
        {
            yield break;
        }
        var bush = found.Go;
        var bushView = found.View;
        var bushSpot = found.Spot;
        c.Note($"bush on the shore {F(Utils.DistanceXZ(bushSpot, helmAt))} m from the helm");
        c.Check(found.Seated && !found.Hinted, $"steering: no Replant target, no hint, crosshair not yellow ('{OneLine(HintText())}')");

        var before = CountByName(inv, name);
        yield return ReadyForKey(player);
        // Control, at the press: ship may have moved since the look. Bush still under the game's hover ray, cultivator
        // still in hand at the helm; else "digs nothing" would prove nothing.
        var pressOnBush = ReferenceEquals(UnderCrosshair(player), bushView) && player.m_doodadController != null
                          && player.InPlaceMode() && ReferenceEquals(player.GetRightItem(), tool);
        yield return Tap(UseAndSnap);
        yield return Frames(3);
        c.Check(player.m_doodadController == null && !player.IsAttached(), "E lets go of the helm");
        c.Check(pressOnBush && Alive(bushView) && CountByName(inv, name) == before,
            $"the same E digs nothing (when pressed: at the helm, cultivator in hand, bush under the crosshair: {pressOnBush})");
        yield return new WaitForSeconds(0.4f);
        yield return rig.AimAt(AimPoint(bush));
        yield return Frames(3);
        if (c.Check(Replant.Target.IsFresh && ReferenceEquals(Replant.Target.View, bushView) && HintText().Contains("Replant"),
                $"off the helm, still on board: the hint is back ('{OneLine(HintText())}'; {RayNote(player)})"))
        {
            yield return ReadyForKey(player);
            yield return Tap(UseAndSnap);
            yield return WaitGone(bushView, 1.5f);
            c.Check(!Alive(bushView) && CountByName(inv, name) == before + 1, "the next E digs the bush up");
        }

        // Gamepad X while steering: digs nothing. Second bush on the same ground; helm taken by its use call (in
        // build mode the game shows no hover to press E on).
        yield return new WaitForSeconds(0.6f);
        if (Alive(bushView))
        {
            rig.Destroy(bush);
            yield return Settle();
        }
        var again = rig.Spawn("RaspberryBush", bushSpot, Quaternion.identity);
        yield return Settle();
        var againView = again.GetComponent<ZNetView>();
        var carried = CountByName(inv, name);
        helm.Interact(player, false, false);
        yield return WaitFor(() => player.m_doodadController != null, 2f);
        if (c.Check(player.m_doodadController != null, $"at the helm again (standing on the ship {player.GetStandingOnShip() == ship})"))
        {
            yield return rig.AimAt(AimPoint(again));
            yield return Frames(3);
            yield return ReadyForKey(player);
            // Control, at the press: bush under the game's hover ray, still at the helm with the cultivator in hand.
            var padSeen = UnderCrosshair(player);
            var padAtHelm = player.m_doodadController != null && player.InPlaceMode() && ReferenceEquals(player.GetRightItem(), tool);
            var padHint = false;
            yield return Tap(new[] { Replant.PadButtonX, Replant.PadAlias }, () => padHint |= Replant.Target.IsFresh || HintText().Length > 0);
            yield return Frames(3);
            c.Check(ReferenceEquals(padSeen, againView) && padAtHelm && Alive(againView) && CountByName(inv, name) == carried && !padHint,
                $"gamepad X at the helm, cultivator in hand ({padAtHelm}), bush under the crosshair ({ReferenceEquals(padSeen, againView)}): nothing dug, no hint");
        }
    }

    // A bush the seated player (ship helm, lox saddle) has under the crosshair, and what showed while looking at it.
    private sealed class SeatBush
    {
        internal GameObject Go;
        internal ZNetView View;
        internal Vector3 Spot;
        // Still at the helm / in the saddle during the whole look.
        internal bool Seated;
        // Any Replant target, hint text or yellow crosshair during the look.
        internal bool Hinted;
        internal string Sight = "";
    }

    // Raspberry bush on the first of these spots where the game's own hover ray (crosshair, interact reach) finds it
    // from the seat; hull, rudder, mast or the lox itself may stand in the way of the others. At most tries spots.
    private static IEnumerator BushFromSeat(Rig rig, List<Vector3> spots, Vector3 seat, int tries, SeatBush result)
    {
        var player = rig.Player;
        result.Go = null;
        result.View = null;
        foreach (var candidate in spots.Take(tries))
        {
            var go = rig.Spawn("RaspberryBush", candidate, Quaternion.identity);
            yield return Settle();
            yield return rig.AimAt(AimPoint(go));
            result.Seated = true;
            result.Hinted = false;
            for (var i = 0; i < 12; i++)
            {
                yield return null;
                result.Seated &= player.m_doodadController != null;
                result.Hinted |= Replant.Target.IsFresh || HintText().Length > 0 || CrosshairYellow();
            }
            if (ReferenceEquals(UnderCrosshair(player), go.GetComponent<ZNetView>()))
            {
                result.Go = go;
                result.View = go.GetComponent<ZNetView>();
                result.Spot = candidate;
                yield break;
            }
            result.Sight += $" ({F(Utils.DistanceXZ(candidate, seat))} m: {RayNote(player)})";
            rig.Destroy(go);
            yield return Settle();
        }
    }

    // ---------- replant.helm.lox (T33, optional part: saddled, tamed lox) ----------

    private static IEnumerator RunHelmLox()
    {
        if (!WorldReady(HelmLoxName))
        {
            yield break;
        }
        var c = new Checks(HelmLoxName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, HelmLoxName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var name = rasp.ItemDisplayName;
            var inv = rig.Inv;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var loxSpot = Vector3.zero;
            if (!c.Check(tool != null && FindPlantSpot(rig, taken, 3.5f, out loxSpot), "cultivator given, open ground for a lox"))
            {
                c.Report();
                yield break;
            }
            var loxGo = rig.Spawn("Lox", loxSpot + Vector3.up * 0.2f, Quaternion.identity);
            yield return Frames(3);
            var tame = loxGo != null ? loxGo.GetComponent<Tameable>() : null;
            var ai = loxGo != null ? loxGo.GetComponent<MonsterAI>() : null;
            var beast = loxGo != null ? loxGo.GetComponent<Character>() : null;
            if (!c.Check(tame != null && ai != null && beast != null && tame.m_saddle != null && tame.m_nview != null && tame.m_nview.IsValid(), "a Lox that can carry a saddle spawned"))
            {
                c.Report();
                yield break;
            }
            // Tame and saddled the game's way: its own tame call and its own saddle message (no stat counted).
            ai.MakeTame();
            tame.m_nview.InvokeRPC("AddSaddle");
            yield return WaitFor(() => beast.IsTamed() && tame.HaveSaddle(), 2f);
            yield return new WaitForSeconds(0.5f);
            var saddle = tame.m_saddle;
            if (!c.Check(beast.IsTamed() && tame.HaveSaddle() && saddle.gameObject.activeInHierarchy && saddle.m_attachPoint != null, "the lox is tame and saddled"))
            {
                c.Report();
                yield break;
            }
            MoveTo(rig, StandPoint(rig.Origin, loxGo.transform.position, 4f) + Vector3.up * 0.1f);
            yield return Frames(3);
            yield return Hold(rig, tool);
            saddle.Interact(player, false, false);
            yield return WaitFor(() => player.m_doodadController != null, 2f);
            if (!c.Check(ReferenceEquals(player.m_doodadController, saddle) && player.IsRiding() && player.IsAttached(), "the player rides the lox (the saddle's own use call)"))
            {
                c.Report();
                yield break;
            }
            c.Check(player.InPlaceMode() && ReferenceEquals(player.GetRightItem(), tool), "cultivator in hand in the saddle, build mode");

            // Free ground spots around the saddle, nearest first.
            var seat = saddle.m_attachPoint.position;
            var water = ZoneSystem.instance.m_waterLevel;
            var spots = new List<Vector3>();
            for (var r = 2.5f; r <= 4.4f; r += 0.3f)
            {
                for (var a = 0; a < 16; a++)
                {
                    var q = seat + Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward * r;
                    q.y = Ground(q);
                    if (q.y < water + 0.3f || spots.Any(s => (s - q).sqrMagnitude < 1.5f * 1.5f)
                        || Physics.CheckSphere(q + Vector3.up * 0.7f, 0.6f, SpaceMask | LayerMask.GetMask("character", "character_net"), QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }
                    spots.Add(q);
                }
            }
            var found = new SeatBush();
            yield return BushFromSeat(rig, spots, seat, 8, found);
            if (!c.Check(found.Go != null, $"from the saddle a bush is under the crosshair within reach ({spots.Count} free spot(s) around the lox)" + found.Sight))
            {
                c.Report();
                yield break;
            }
            c.Note($"bush {F(Utils.DistanceXZ(found.Spot, seat))} m from the saddle");
            c.Check(found.Seated && !found.Hinted, $"riding: no Replant target, no hint, crosshair not yellow ('{OneLine(HintText())}')");

            var before = CountByName(inv, name);
            yield return ReadyForKey(player);
            // Control, at the press: lox may have moved since the look. Bush still under the game's hover ray,
            // cultivator still in hand in the saddle; else "digs nothing" would prove nothing.
            var pressOnBush = ReferenceEquals(UnderCrosshair(player), found.View) && player.m_doodadController != null
                              && player.InPlaceMode() && ReferenceEquals(player.GetRightItem(), tool);
            yield return Tap(UseAndSnap);
            yield return Frames(3);
            c.Check(player.m_doodadController == null && !player.IsAttached(), "E lets go of the saddle");
            c.Check(pressOnBush && Alive(found.View) && CountByName(inv, name) == before,
                $"the same E digs nothing (when pressed: in the saddle, cultivator in hand, bush under the crosshair: {pressOnBush})");
            yield return new WaitForSeconds(0.6f);
            yield return rig.AimAt(AimPoint(found.Go));
            yield return Frames(3);
            if (!(Replant.Target.IsFresh && ReferenceEquals(Replant.Target.View, found.View)))
            {
                c.Note($"off the lox the bush is not under the crosshair ({RayNote(player)}): the player steps to it");
                yield return Stand(rig, found.Spot);
                yield return rig.AimAt(AimPoint(found.Go));
                yield return Frames(3);
            }
            if (c.Check(Replant.Target.IsFresh && ReferenceEquals(Replant.Target.View, found.View) && HintText().Contains("Replant"), $"off the lox: the hint is back ('{OneLine(HintText())}'; {RayNote(player)})"))
            {
                yield return ReadyForKey(player);
                yield return Tap(UseAndSnap);
                yield return WaitGone(found.View, 1.5f);
                c.Check(!Alive(found.View) && CountByName(inv, name) == before + 1, "the next E digs the bush up");
            }

            // Gamepad X in the saddle: digs nothing.
            yield return new WaitForSeconds(0.6f);
            MoveTo(rig, StandPoint(rig.Origin, loxGo.transform.position, 4f) + Vector3.up * 0.1f);
            yield return Frames(3);
            saddle.Interact(player, false, false);
            yield return WaitFor(() => player.m_doodadController != null, 2f);
            if (c.Check(player.m_doodadController != null, "in the saddle again"))
            {
                var again = new SeatBush();
                var carried = CountByName(inv, name);
                yield return BushFromSeat(rig, spots, saddle.m_attachPoint.position, 8, again);
                if (c.Check(again.Go != null, "a second bush is under the crosshair from the saddle" + again.Sight))
                {
                    yield return ReadyForKey(player);
                    // Control, at the press: bush under the game's hover ray, still riding with the cultivator in hand.
                    var padReady = ReferenceEquals(UnderCrosshair(player), again.View) && player.m_doodadController != null
                                   && player.InPlaceMode() && ReferenceEquals(player.GetRightItem(), tool);
                    var padHint = false;
                    yield return Tap(new[] { Replant.PadButtonX, Replant.PadAlias }, () => padHint |= Replant.Target.IsFresh || HintText().Length > 0);
                    yield return Frames(3);
                    c.Check(padReady && again.Seated && Alive(again.View) && CountByName(inv, name) == carried && !padHint && !again.Hinted,
                        $"gamepad X in the saddle (when pressed: riding, cultivator in hand, bush under the crosshair: {padReady}): nothing dug, no hint");
                }
            }
            c.Report();
        }
        finally
        {
            if (player != null && player.m_doodadController != null)
            {
                player.StopDoodadControl();
            }
            rig.Restore();
        }
    }

    // ---------- replant.mp.noserver (M13: server without the mod) ----------

    private static IEnumerator RunMpNoServer()
    {
        if (!MpReady(MpNoServerName))
        {
            yield break;
        }
        var c = new Checks(MpNoServerName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, MpNoServerName);
        var taken = new List<Vector3>();
        try
        {
            var me = FeatureRegistry.Find(ModInfo.Guid);
            c.Note($"scenario '{SelfTest.Scenario}': state {(me != null ? me.Value.State : "?")}, status '{(me != null ? me.Value.Status : "?")}'");
            if (!c.Check(SelfTest.Scenario == NoServerScenario && me != null && me.Value.State == nameof(ModState.ServerMissing) && !Plugin.FeatureActive && !FeaturePatched(),
                    "joined a server that does not have the mod: the mod is inactive for that reason, its feature patches are not applied"))
            {
                c.Report();
                yield break;
            }
            c.Check(me.Value.Status.Length > 0 && me.Value.Status.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0, $"its status line names the server ('{me.Value.Status}')");
            PrepareInput(rig, false);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            rig.Give(rasp.ItemName, 2);
            yield return Hold(rig, tool);
            var shared = CultivatorTiers.CultivatorDrop != null ? CultivatorTiers.CultivatorDrop.m_itemData.m_shared : null;
            c.Check(shared != null && shared.m_maxQuality == PlantCatalog.VanillaMaxQuality && !CultivatorTiers.InForce, $"the cultivator has its 3 vanilla levels ({(shared != null ? shared.m_maxQuality : -1)})");
            player.UpdateKnownRecipesList();
            player.UpdateAvailablePiecesList();
            var pieces = player.m_buildPieces != null ? player.m_buildPieces.m_availablePieces.Count : -1;
            c.Check(player.InPlaceMode() && pieces > 0 && SaplingPiecesOffered(player) == 0,
                $"cultivator in hand, two transplants carried: build menu with {pieces} piece(s), no transplant among them ({SaplingPiecesOffered(player)})");

            // A ripe bush under the crosshair: no hint, E digs nothing.
            ZNetView bushView = null;
            var why = "";
            for (var attempt = 0; attempt < 3 && bushView == null; attempt++)
            {
                if (!FindPlantSpot(rig, taken, 1.2f, out var spot))
                {
                    why += " (no free spot)";
                    break;
                }
                var go = rig.Spawn("RaspberryBush", spot, Quaternion.identity);
                yield return Settle();
                yield return Stand(rig, spot);
                yield return rig.AimAt(AimPoint(go));
                yield return Frames(3);
                var view = go.GetComponent<ZNetView>();
                if (ReferenceEquals(UnderCrosshair(player), view))
                {
                    bushView = view;
                    break;
                }
                why += $" (try {attempt + 1}: {RayNote(player)})";
                rig.Destroy(go);
                yield return Settle();
            }
            if (c.Check(bushView != null, "a ripe raspberry bush is under the crosshair within reach" + why))
            {
                c.Check(!Replant.Target.IsFresh && !HintText().Contains("Replant") && !CrosshairYellow(), $"no Replant hint over it ('{OneLine(HintText())}')");
                var count = CountByName(rig.Inv, rasp.ItemDisplayName);
                yield return ReadyForKey(player);
                yield return Tap(UseAndSnap);
                yield return new WaitForSeconds(0.5f);
                c.Check(Alive(bushView) && CountByName(rig.Inv, rasp.ItemDisplayName) == count, "E digs nothing");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.mp.offgrow (M14: real growth on the clock with the mod turned off) ----------

    // Server: world clock skipped by arg seconds (what "skiptime" does on a host). Answer = clock before and after.
    private static IEnumerator ServerSkipStep(string arg, object[] reply)
    {
        yield return null;
        var net = ZNet.instance;
        if (net == null || !net.IsServer() || !double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0.0 || seconds > 200000.0)
        {
            SelfTest.Answer(reply, false, "not a server, or no valid number of seconds");
            yield break;
        }
        var before = net.GetTimeSeconds();
        net.SetNetTime(before + seconds);
        SelfTest.Answer(reply, true, $"clock {before.ToString("0", CultureInfo.InvariantCulture)} s -> {net.GetTimeSeconds().ToString("0", CultureInfo.InvariantCulture)} s");
    }

    private static IEnumerator RunMpOffGrow()
    {
        if (!MpReady(MpOffGrowName))
        {
            yield break;
        }
        var c = new Checks(MpOffGrowName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, MpOffGrowName);
        var taken = new List<Vector3>();
        var enabled = Plugin.TestEnabledEntry;
        try
        {
            PrepareInput(rig, false);
            var ok = new bool[1];
            yield return ServerDefaults(c, ok);
            yield return SlowLoopRuns(rig, c);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var spot = Vector3.zero;
            if (!ok[0] || !c.Check(enabled != null && enabled.Value && FindPlantSpot(rig, taken, 1.4f, out spot), "mod on, a free spot"))
            {
                c.Report();
                yield break;
            }
            // This player's own setting (this run's own config file): 0.1 = 30 min for a bush. The server keeps 1 (5 h).
            Plugin.GrowTimeMultiplier.Value = 0.1f;
            var sapling = rig.Spawn(rasp.SaplingName, spot, Quaternion.identity);
            yield return Settle();
            var view = sapling != null ? sapling.GetComponent<ZNetView>() : null;
            var plant = sapling != null ? sapling.GetComponent<Plant>() : null;
            if (!c.Check(Alive(view) && plant != null && ServerRules.UsingServer && GrowTimesAre(plant, rasp, 1f),
                    $"young raspberry transplant planted with the mod on: it is on the server's grow time ({GrowTimesText(plant)}, {GrowTimesOf(rasp, 1f)})"))
            {
                c.Report();
                yield break;
            }
            // Two days on the server's clock (3600 s: the hour of day stays), like skiptime on the host.
            var clock = ZNet.instance.GetTimeSeconds();
            var skipped = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(SkipStep, "3600", skipped);
            yield return WaitFor(() => ZNet.instance.GetTimeSeconds() >= clock + 3500.0, 8f);
            c.Check(skipped.Answered && skipped.Ok && ZNet.instance.GetTimeSeconds() >= clock + 3500.0 && plant.TimeSincePlanted() >= 3500.0,
                $"the server skipped 3600 s on its clock ({skipped}); the transplant now counts {F((float)plant.TimeSincePlanted())} s since planting");
            // Its first 10 s over, one growth pass: still young on the server's rules (they need 16200 s or more).
            yield return WaitFor(() => Time.time - plant.m_spawnTime > 10.5f, 12f);
            yield return NextPlantPass(plant, 13f);
            c.Check(Alive(view) && Plugin.FeatureActive, "mod on: with 3600 s on its clock it stays a young plant (the server's x1 needs 5 h)");

            // Mod off on this game (the server lets such players stay for this test): own grow time, and it grows.
            var allow = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(AllowStep, "on", allow);
            c.Check(allow.Answered && allow.Ok && Field(allow, "allow") == "True", $"server lets players stay who cannot play by its rules ({allow})");
            enabled.Value = false;
            yield return WaitFor(() => !Plugin.FeatureActive, 3f);
            c.Check(!Plugin.FeatureActive && GrowTimesAre(plant, rasp, 0.1f),
                $"mod turned off on this game: the transplant is on this player's own grow time ({GrowTimesText(plant)}, {GrowTimesOf(rasp, 0.1f)})");
            var grown = new Grown();
            yield return WaitGrown(rig, rasp, view, spot, 16f, grown);
            c.Check(grown.Ok, $"and it grows on this game ({F(grown.Waited)} s waited): the own x0.1 needs 30 min, the clock says 60");
            c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && ZNet.instance.GetServerPeer() != null, "still connected");

            // Back on; the server refuses such players again.
            enabled.Value = true;
            yield return WaitFor(() => Plugin.FeatureActive && ServerRules.UsingServer, 8f);
            c.Check(Plugin.FeatureActive && ServerRules.UsingServer, "mod on again, server rules back");
            var again = new SelfTest.ServerReply();
            for (var i = 0; i < 8; i++)
            {
                yield return SelfTest.CallServer(AllowStep, "peer", again);
                if (Field(again, "compatible") == "True")
                {
                    break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (c.Check(Field(again, "compatible") == "True", $"server sees the player as fine again ({again})"))
            {
                var off = new SelfTest.ServerReply();
                yield return SelfTest.CallServer(AllowStep, "off", off);
                c.Check(off.Ok && Field(off, "allow") == "False", $"server refuses such players again ({off})");
            }
            c.Report();
        }
        finally
        {
            Reset(Plugin.GrowTimeMultiplier);
            if (enabled != null && !enabled.Value)
            {
                enabled.Value = true;
            }
            rig.Restore();
        }
    }
}
#endif
