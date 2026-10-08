#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Self tests in the far biomes (one real teleport there and back per test, like
// DeepNorth.Awakening's), and the scythe rule:
//   replant.far.mountain  T05 (cold), T29 (too cold one mown): raspberry transplant in the Mountains is too cold and
//                         waits; a scythe swing destroys it; then a second one is too cold too until a shield
//                         generator works beside it, and grows there
//   replant.far.ashlands  T05 (hot), T17: raspberry transplant too hot; fiddlehead ghost fine on ash and refused on
//                         lava; fiddlehead and smoke puff planted there grow outside any shield
//   replant.far.north     T18: lingonberry ghost fine in the Deep North, also on deep snow; planted there it grows
// Near the spawn (run with the other tests, registered in SelfTestsOff.MoreTests through RunMountain... names):
//   replant.wrongbiome    T17, T18: Ashlands and Deep North transplants have a refused ghost in the Meadows
//   replant.scythe        T29: a scythe swing leaves a Yggdrasil transplant that waits for sap, and mows one that
//                         cannot grow
// Creatures within 60 m of the far test place are removed first (throwaway world): god mode keeps the player alive but
// their area attacks would break the young plants under test.
internal static partial class SelfTests
{
    private const string MountainName = "replant.far.mountain";
    private const string AshlandsName = "replant.far.ashlands";
    private const string NorthName = "replant.far.north";
    private const string WrongBiomeName = "replant.wrongbiome";
    private const string ScytheName = "replant.scythe";

    private const float TravelSeconds = 40f;

    private sealed class Trip
    {
        internal bool Ok;
        internal string Why = "";
        internal float Seconds;
    }

    // Vanilla far teleport (loading screen), then the new area loaded. Real time: frames are long while loading.
    private static IEnumerator Travel(Player p, Vector3 target, Trip trip)
    {
        trip.Ok = false;
        trip.Why = "";
        var start = Time.realtimeSinceStartup;
        float Passed() => Time.realtimeSinceStartup - start;
        while (!p.TeleportTo(target, p.transform.rotation, true))
        {
            if (Passed() > 8f)
            {
                trip.Why = "the game refused the teleport";
                trip.Seconds = Passed();
                yield break;
            }
            yield return null;
        }
        while (p.IsTeleporting() && Passed() < TravelSeconds)
        {
            yield return null;
        }
        while (!p.IsTeleporting() && Passed() < TravelSeconds && (ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(p.transform.position)))
        {
            yield return null;
        }
        trip.Seconds = Passed();
        var flat = p.transform.position - target;
        flat.y = 0f;
        if (p.IsTeleporting())
        {
            trip.Why = $"still teleporting after {F(trip.Seconds)} s";
        }
        else if (flat.magnitude > 30f)
        {
            trip.Why = $"ended {F(flat.magnitude)} m from the target";
        }
        else
        {
            trip.Ok = true;
        }
    }

    private sealed class Land
    {
        internal bool Ok;
        internal Vector3 At;
        internal int InBiome;
        internal int Refined;
    }

    private static readonly Vector2[] LevelOffsets =
    {
        new Vector2(6f, 0f), new Vector2(-6f, 0f), new Vector2(0f, 6f), new Vector2(0f, -6f), new Vector2(12f, 12f), new Vector2(-12f, -12f),
    };

    // Dry land of this biome at (x, z), about level around it (world generator, no zone loaded).
    private static bool LevelAt(WorldGenerator wg, Heightmap.Biome biome, float x, float z, float water, float slope, out float h)
    {
        h = 0f;
        if (wg.GetBiome(x, z) != biome)
        {
            return false;
        }
        h = wg.GetHeight(x, z);
        if (h < water + 3f)
        {
            return false;
        }
        foreach (var d in LevelOffsets)
        {
            if (wg.GetBiome(x + d.x, z + d.y) != biome || Mathf.Abs(wg.GetHeight(x + d.x, z + d.y) - h) >= slope)
            {
                return false;
            }
        }
        return true;
    }

    // Flat dry land of this biome from the world generator: first hit of the sample points. refine > 0 (mountains:
    // steep nearly everywhere, a sample point alone never hit): around each sample of the biome that is not level, a
    // grid of points 8 m apart out to refine metres is tried too (at most 80 samples, one per frame, 8 s).
    private static IEnumerator FindFarLand(Heightmap.Biome biome, IEnumerable<Vector2> samples, float slope, float refine, Land land)
    {
        land.Ok = false;
        var wg = WorldGenerator.instance;
        if (wg == null || ZoneSystem.instance == null)
        {
            yield break;
        }
        var water = ZoneSystem.instance.m_waterLevel;
        var until = Time.realtimeSinceStartup + 8f;
        foreach (var s in samples)
        {
            if (wg.GetBiome(s.x, s.y) != biome)
            {
                continue;
            }
            land.InBiome++;
            if (LevelAt(wg, biome, s.x, s.y, water, slope, out var h))
            {
                land.At = new Vector3(s.x, h + 1f, s.y);
                land.Ok = true;
                yield break;
            }
            if (refine <= 0f || land.Refined >= 80)
            {
                continue;
            }
            land.Refined++;
            for (var dx = -refine; dx <= refine; dx += 8f)
            {
                for (var dz = -refine; dz <= refine; dz += 8f)
                {
                    if (LevelAt(wg, biome, s.x + dx, s.y + dz, water, slope, out h))
                    {
                        land.At = new Vector3(s.x + dx, h + 1f, s.y + dz);
                        land.Ok = true;
                        yield break;
                    }
                }
            }
            yield return null;
            if (Time.realtimeSinceStartup > until)
            {
                yield break;
            }
        }
    }

    private static IEnumerable<Vector2> Rings(Vector3 center, float from, float to, float step)
    {
        for (var r = from; r <= to; r += step)
        {
            for (var a = 0; a < 32; a++)
            {
                var angle = a * Mathf.PI * 2f / 32f;
                yield return new Vector2(center.x + Mathf.Sin(angle) * r, center.z + Mathf.Cos(angle) * r);
            }
        }
    }

    private static IEnumerable<Vector2> Band(float zFrom, float zTo)
    {
        var step = zTo > zFrom ? 100f : -100f;
        for (var z = zFrom; step > 0f ? z <= zTo : z >= zTo; z += step)
        {
            for (var i = 0; i <= 30; i++)
            {
                var x = (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 200f;
                yield return new Vector2(x, z);
            }
        }
    }

    private static int RemoveCreatures(Player p, float radius)
    {
        var list = new List<Character>();
        Character.GetCharactersInRange(p.transform.position, radius, list);
        var removed = 0;
        foreach (var ch in list)
        {
            if (ch == null || ch.IsPlayer())
            {
                continue;
            }
            var view = ch.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(ch.gameObject);
                removed++;
            }
        }
        return removed;
    }

    private static readonly float[] FarRings = { 3.5f, 5f, 6.5f, 8f, 10f, 12f, 14f };

    // Free spot near center in the far place, with room to stand next to it.
    private static bool FarSpot(Rig rig, Vector3 center, List<Vector3> taken, float clear, out Vector3 spot, Func<Vector3, bool> accept = null)
    {
        var origin = rig.Origin;
        return FindSpot(center, Vector3.forward, FarRings, clear, taken, out spot, p => StandOk(origin, p) && (accept == null || accept(p)));
    }

    // Common frame of a far test: go, run body at the place, always come home.
    private static IEnumerator FarTest(string name, Heightmap.Biome biome, IEnumerable<Vector2> samples, float slope, Func<Rig, Checks, Vector3, IEnumerator> body,
        float refine = 0f)
    {
        if (!WorldReady(name))
        {
            yield break;
        }
        var c = new Checks(name);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, name);
        var home = rig.Origin;
        var away = false;
        try
        {
            PrepareInput(rig);
            var found = new Land();
            yield return FindFarLand(biome, samples, slope, refine, found);
            if (!c.Check(found.Ok, $"flat {biome} land found in the world ({found.InBiome} sample point(s) in that biome, {found.Refined} looked at closely)"))
            {
                c.Report();
                yield break;
            }
            var land = found.At;
            var trip = new Trip();
            away = true;
            yield return Travel(player, land, trip);
            c.Note($"travelled to {F(land)} in {F(trip.Seconds)} s");
            if (c.Check(trip.Ok, $"arrived in the {biome}" + (trip.Ok ? "" : ": " + trip.Why)))
            {
                var here = player.transform.position;
                c.Check(Heightmap.FindBiome(here) == biome, $"standing in the {Heightmap.FindBiome(here)}");
                c.Note($"{RemoveCreatures(player, 60f)} creature(s) removed around the test place");
                rig.Noon();
                yield return new WaitForSeconds(0.5f);
                // After the loading screen: clock and the game's slow update loop (it grows the plants here).
                yield return GrowReady(rig, c);
                yield return body(rig, c, here);
                RemoveCreatures(player, 60f);
            }
            // Spawned things go before the area unloads.
            rig.Restore();
            var back = new Trip();
            yield return Travel(player, home, back);
            away = !back.Ok;
            c.Check(back.Ok, "travelled back to the start" + (back.Ok ? $" ({F(back.Seconds)} s)" : ": " + back.Why));
            c.Report();
        }
        finally
        {
            rig.Restore();
            if (away && player != null && (player.transform.position - home).sqrMagnitude > 200f * 200f && !player.IsTeleporting())
            {
                // Cut short far from home: at least start the trip back.
                player.TeleportTo(home, player.transform.rotation, true);
            }
        }
    }

    // Young plant of kind too hot or too cold here: status, hover, and it waits (never grows, never vanishes) even long
    // past its time. Its 10 first seconds must be over before the last look: caller waits, then calls StillYoung.
    private static GameObject SpawnMisplaced(Rig rig, Checks c, PlantKind kind, Vector3 spot, Plant.Status want, string token)
    {
        var go = rig.Spawn(kind.SaplingName, spot, Quaternion.identity);
        var plant = go != null ? go.GetComponent<Plant>() : null;
        if (!c.Check(plant != null, $"young {kind.DisplayName} transplant planted"))
        {
            return null;
        }
        plant.UpdateHealth(100.0);
        c.Check(plant.GetStatus() == want && plant.GetHoverText() == PlantHover(kind.ItemDisplayName, token),
            $"its hover says '{OneLine(plant.GetHoverText())}' (status {plant.GetStatus()}, want {want})");
        c.Check(!ShieldGenerator.IsInsideShield(spot), "no shield around it");
        Age(go.GetComponent<ZNetView>(), 20000.0);
        return go;
    }

    // ---------- replant.far.mountain (T05 cold, T29) ----------

    private static IEnumerator RunMountain()
    {
        var center = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
        // Mountains are steep: flat land only through the close look around each sample point (refine).
        return FarTest(MountainName, Heightmap.Biome.Mountain, Rings(center, 400f, 6000f, 150f), 1.5f, MountainBody, 48f);
    }

    private static readonly float[] MountainWideRings = { 16f, 18f, 20f, 23f, 26f, 30f };

    private static readonly Vector3[] GeneratorSides = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

    // Three parts one after the other, so ONE free spot is enough. Before (runs of 2026-10-08) shield generator and
    // too cold transplant stood there together: the dome of one fuel is 30 m wide in the game, the too cold one had
    // to stand 35 m and more from the generator, and the mountain had no second flat free spot that far out.
    //   1. T05: transplant is too cold and waits, with no shield generator anywhere.
    //   2. T29: scythe swing destroys it.
    //   3. T05 (optional part): the next transplant is too cold too, then a shield generator works beside it:
    //      healthy, and vanilla grows it.
    private static IEnumerator MountainBody(Rig rig, Checks c, Vector3 here)
    {
        var taken = new List<Vector3>();
        var rasp = PlantCatalog.ByKey("RaspberryBush");
        bool Mountain(Vector3 p) => Heightmap.FindBiome(p) == Heightmap.Biome.Mountain;
        // Close rings first; none there = look further out and a little higher or lower.
        var haveSpot = FarSpot(rig, here, taken, 1.4f, out var spot, Mountain)
                       || FindSpot(here, Vector3.forward, MountainWideRings, 1.2f, taken, out spot, p => StandOk(rig.Origin, p) && Mountain(p), 6f);
        if (!c.Check(haveSpot, "free spot on the mountain"))
        {
            yield break;
        }

        // 1. Too cold, and it waits.
        var cold = SpawnMisplaced(rig, c, rasp, spot, Plant.Status.TooCold, "$piece_plant_toocold");
        if (cold == null)
        {
            yield break;
        }
        var coldView = cold.GetComponent<ZNetView>();
        var coldPlant = cold.GetComponent<Plant>();
        // Vanilla Plant.SUpdate try to grow it once it stood 10 s and is past its grow time (it is: 20000 s). Wait
        // for a slow update that came after those 10 s: that one did the grow try (m_updateTime = its time + 10).
        bool GrowTried() => coldPlant.m_updateTime - 10f - coldPlant.m_spawnTime > 10f;
        var until = Time.time + 20f;
        while (Time.time < until && Alive(coldView) && !GrowTried())
        {
            yield return new WaitForSeconds(0.25f);
        }
        yield return new WaitForSeconds(0.5f);
        var tried = Alive(coldView) && GrowTried();
        c.Check(tried, "the game tried to grow the too cold transplant (a slow update after its first 10 s, 20000 s past planting)"
                       + (Alive(coldView) ? $": {YoungNote(coldView)}" : ": it is gone"));
        c.Check(!ShieldGenerator.IsInsideShield(spot), "still no shield dome around the too cold transplant");
        c.Check(Alive(coldView) && coldPlant.GetStatus() == Plant.Status.TooCold, "too cold and 20000 s past planting: still a young plant (it waits, it is not destroyed)");

        // 2. T29: a scythe swing destroys it (the game's rule for crops that cannot grow).
        var mown = new MowResult();
        yield return Mow(rig, c, cold, mown);
        c.Check(mown.Swung && !Alive(coldView), $"scythe swing over the too cold transplant destroys it (swing started {mown.Swung})");

        // 3. Inside a working shield a transplant is healthy and grows. Own spot when the mountain has one more,
        // else the spot of the mown one (made empty first). Creatures that walked in meanwhile go first.
        RemoveCreatures(rig.Player, 60f);
        if (Alive(coldView))
        {
            rig.Destroy(cold);
        }
        yield return Settle();
        if (!FarSpot(rig, here, taken, 1.4f, out var shieldSpot, Mountain))
        {
            shieldSpot = spot;
        }
        var second = rig.Spawn(rasp.SaplingName, shieldSpot, Quaternion.identity);
        var secondAt = Time.time;
        var plant = second != null ? second.GetComponent<Plant>() : null;
        if (!c.Check(plant != null, "second young raspberry transplant planted"))
        {
            yield break;
        }
        var secondView = second.GetComponent<ZNetView>();
        // Control before the generator is there (its dome could be up from the start): too cold like the first one.
        plant.UpdateHealth(100.0);
        c.Check(plant.GetStatus() == Plant.Status.TooCold && !ShieldGenerator.IsInsideShield(shieldSpot),
            $"control: with no shield generator the second transplant is too cold as well ({plant.GetStatus()})");
        // Generator 4.5 m beside it (well inside the smallest dome, and the machine's own colliders stay out of the
        // transplant's grow space), on the side away from the player (scythe swing left the player next to a spot).
        var playerAt = rig.Player.transform.position;
        var generatorAt = shieldSpot;
        var far = -1f;
        foreach (var side in GeneratorSides)
        {
            var p = shieldSpot + side * 4.5f;
            var d = new Vector2(p.x - playerAt.x, p.z - playerAt.z).sqrMagnitude;
            if (d > far)
            {
                far = d;
                generatorAt = p;
            }
        }
        generatorAt.y = Ground(generatorAt);
        var generator = rig.Spawn("piece_shieldgenerator", generatorAt, Quaternion.identity);
        // Generator's Start first (a frame after the spawn): it find the dome effect of the camera. A generator
        // destroyed before its Start throw in vanilla OnDestroy (run of 2026-10-08: that NullReferenceException was
        // logged when the test stopped right after the spawn).
        yield return Settle();
        var shield = generator != null ? generator.GetComponent<ShieldGenerator>() : null;
        // Fuel through the object's own ZNetView (ZDO there since Awake), never through the generator's m_nview field.
        var shieldView = generator != null ? generator.GetComponent<ZNetView>() : null;
        if (!c.Check(shield != null && Alive(shieldView) && Alive(secondView), "shield generator spawned 4.5 m beside the second transplant"))
        {
            yield break;
        }
        // One fuel = smallest working dome.
        shieldView.GetZDO().Set(ZDOVars.s_fuel, 1f);
        until = Time.time + 14f;
        while (Time.time < until && !ShieldGenerator.IsInsideShield(shieldSpot))
        {
            yield return new WaitForSeconds(0.25f);
        }
        if (Alive(secondView))
        {
            plant.UpdateHealth(100.0);
        }
        c.Check(Alive(secondView) && ShieldGenerator.IsInsideShield(shieldSpot) && plant.GetStatus() == Plant.Status.Healthy,
            $"inside a working shield it is healthy ({(Alive(secondView) ? plant.GetStatus().ToString() : "gone")}, inside {ShieldGenerator.IsInsideShield(shieldSpot)})");
        Age(secondView, 20000.0);
        var grown = new Grown();
        // Vanilla grow it once it stood 10 s: until 18 s after its spawn.
        yield return WaitGrown(rig, rasp, secondView, shieldSpot, Mathf.Max(5f, 18f - (Time.time - secondAt)), grown);
        c.Check(grown.Ok, "and vanilla grows it there, 20000 s past planting");
    }

    // ---------- replant.far.ashlands (T05 hot, T17) ----------

    private static IEnumerator RunAshlands()
    {
        return FarTest(AshlandsName, Heightmap.Biome.AshLands, Band(-8600f, -9900f), 1.6f, AshlandsBody);
    }

    private static float Vegetation(Vector3 p)
    {
        var hm = Heightmap.FindHeightmap(p);
        return hm != null ? hm.GetVegetationMask(p) : -1f;
    }

    private static IEnumerator AshlandsBody(Rig rig, Checks c, Vector3 here)
    {
        var player = rig.Player;
        var taken = new List<Vector3>();
        var rasp = PlantCatalog.ByKey("RaspberryBush");
        var fiddle = PlantCatalog.ByKey("Fiddlehead");
        var puff = PlantCatalog.ByKey("SmokePuff");
        var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 6);
        rig.Give(fiddle.ItemName, 2);
        rig.Give(puff.ItemName, 1);
        yield return Hold(rig, tool);
        bool Ash(Vector3 p) => Vegetation(p) >= 0f && Vegetation(p) <= 0.1f && Heightmap.FindBiome(p) == Heightmap.Biome.AshLands && Buildable(p);
        var fiddleSpot = Vector3.zero;
        var puffSpot = Vector3.zero;
        var hotSpot = Vector3.zero;
        if (!c.Check(tool != null && FarSpot(rig, here, taken, 1.2f, out fiddleSpot, Ash) && FarSpot(rig, here, taken, 1.2f, out puffSpot, Ash)
                     && FarSpot(rig, here, taken, 1.4f, out hotSpot, Ash), "three free spots on ash ground"))
        {
            yield break;
        }
        var spawnedAt = Time.time;
        var hot = SpawnMisplaced(rig, c, rasp, hotSpot, Plant.Status.TooHot, "$piece_plant_toohot");

        // T17: green on ash, planted; red on lava.
        var placedAt = new Dictionary<PlantKind, Placing>();
        foreach (var pair in new[] { new KeyValuePair<PlantKind, Vector3>(fiddle, fiddleSpot), new KeyValuePair<PlantKind, Vector3>(puff, puffSpot) })
        {
            var placing = new Placing();
            yield return AimGhost(rig, SaplingPiece(pair.Key), pair.Value, placing);
            c.Check(placing.Status == Player.PlacementStatus.Valid, $"{pair.Key.DisplayName} ghost on ash ground can be placed (status {placing.Status})");
            yield return PressPlace(rig, pair.Key.SaplingHash, placing);
            if (c.Check(placing.Placed, $"{pair.Key.DisplayName} transplant planted in the Ashlands (status {placing.Status})"))
            {
                c.Check(!ShieldGenerator.IsInsideShield(placing.Go.transform.position), $"{pair.Key.DisplayName}: outside any shield");
                Age(placing.View, 20000.0);
                placedAt[pair.Key] = placing;
            }
        }
        var lava = Vector3.zero;
        var haveLava = false;
        for (var r = 3f; r <= 40f && !haveLava; r += 1.5f)
        {
            for (var a = 0; a < 24 && !haveLava; a++)
            {
                var p = here + Quaternion.Euler(0f, a * 15f, 0f) * Vector3.forward * r;
                p.y = Ground(p);
                if (Vegetation(p) > 0.15f && Heightmap.FindBiome(p) == Heightmap.Biome.AshLands && StandOk(rig.Origin, p) && Buildable(p))
                {
                    lava = p;
                    haveLava = true;
                }
            }
        }
        if (c.Check(haveLava, "lava ground within 40 m of the test place"))
        {
            var onLava = new Placing();
            yield return AimGhost(rig, SaplingPiece(fiddle), lava, onLava);
            c.Check(onLava.Status == Player.PlacementStatus.NeedDirt, $"fiddlehead ghost on lava is refused (status {onLava.Status}, lava mask {F(Vegetation(lava))})");
        }

        // Both grow there, outside any shield.
        foreach (var pair in placedAt)
        {
            var grown = new Grown();
            yield return WaitGrown(rig, pair.Key, pair.Value.View, pair.Value.GhostAt, 16f, grown);
            if (c.Check(grown.Ok, $"{pair.Key.DisplayName} transplant grew in the Ashlands ({F(grown.Waited)} s)"))
            {
                var pick = grown.Go.GetComponent<Pickable>();
                c.Check(Utils.GetPrefabName(grown.Go) == pair.Key.GrownPrefabs[0] && pick != null && pick.CanBePicked(), $"into a ripe {pair.Key.GrownPrefabs[0]}");
            }
        }
        if (hot != null)
        {
            while (Time.time - spawnedAt < 12.5f)
            {
                yield return new WaitForSeconds(0.25f);
            }
            c.Check(Alive(hot.GetComponent<ZNetView>()) && hot.GetComponent<Plant>().GetStatus() == Plant.Status.TooHot,
                "raspberry transplant too hot and 20000 s past planting: still a young plant");
        }
    }

    // ---------- replant.far.north (T18) ----------

    private static IEnumerator RunNorth()
    {
        return FarTest(NorthName, Heightmap.Biome.DeepNorth, Band(8600f, 9900f), 1.6f, NorthBody);
    }

    private static float Snow(Vector3 p)
    {
        var hm = Heightmap.FindHeightmap(p);
        return hm != null ? hm.GetCultivationMask(p) : -1f;
    }

    private static IEnumerator NorthBody(Rig rig, Checks c, Vector3 here)
    {
        var player = rig.Player;
        var taken = new List<Vector3>();
        var lingon = PlantCatalog.ByKey("LingonberryBush");
        var piece = SaplingPiece(lingon);
        var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 7);
        rig.Give(lingon.ItemName, 2);
        yield return Hold(rig, tool);
        bool North(Vector3 p) => Heightmap.FindBiome(p) == Heightmap.Biome.DeepNorth && Buildable(p);
        var spot = Vector3.zero;
        if (!c.Check(tool != null && piece != null && FarSpot(rig, here, taken, 1.4f, out spot, North), "free spot in the Deep North"))
        {
            yield break;
        }
        var until = Time.time + 3f;
        while (Time.time < until && player.m_currentBiome != Heightmap.Biome.DeepNorth)
        {
            yield return null;
        }
        c.Check(player.m_currentBiome == Heightmap.Biome.DeepNorth, $"the game sees the player in the {player.m_currentBiome}");
        var placing = new Placing();
        yield return AimGhost(rig, piece, spot, placing);
        c.Check(placing.Status == Player.PlacementStatus.Valid, $"lingonberry ghost in the Deep North can be placed (status {placing.Status}, snow {F(Snow(spot))})");
        yield return PressPlace(rig, lingon.SaplingHash, placing);
        if (c.Check(placing.Placed, $"lingonberry transplant planted (status {placing.Status})"))
        {
            c.Check(!ShieldGenerator.IsInsideShield(placing.Go.transform.position), "outside any shield");
            Age(placing.View, 20000.0);
        }

        // Deep snow: where the game refuses ordinary crops, the lingonberry ghost is still fine.
        var deep = Vector3.zero;
        var haveDeep = false;
        for (var r = 3f; r <= 40f && !haveDeep; r += 1.5f)
        {
            for (var a = 0; a < 24 && !haveDeep; a++)
            {
                var p = here + Quaternion.Euler(0f, a * 15f, 0f) * Vector3.forward * r;
                p.y = Ground(p);
                if (Snow(p) > player.m_deepSnowBuildHeight + 0.05f && North(p) && StandOk(rig.Origin, p)
                    && !Physics.CheckCapsule(p + Vector3.up * 0.2f, p + Vector3.up * 2f, 1.2f, SpaceMask, QueryTriggerInteraction.Ignore))
                {
                    deep = p;
                    haveDeep = true;
                }
            }
        }
        if (c.Check(haveDeep, "deep snow within 40 m of the test place"))
        {
            var carrot = TablePiece("sapling_carrot");
            if (carrot != null && rig.Give("CarrotSeeds", 1) != null)
            {
                var control = new Placing();
                yield return AimGhost(rig, carrot, deep, control);
                c.Note($"control: a carrot seed ghost on that snow has status {control.Status}");
            }
            var onSnow = new Placing();
            yield return AimGhost(rig, piece, deep, onSnow);
            c.Check(onSnow.Status == Player.PlacementStatus.Valid, $"lingonberry ghost on deep snow can be placed (status {onSnow.Status}, snow {F(Snow(deep))})");
        }
        if (placing.Placed)
        {
            var grown = new Grown();
            yield return WaitGrown(rig, lingon, placing.View, placing.GhostAt, 16f, grown);
            if (c.Check(grown.Ok, $"lingonberry transplant grew in the Deep North ({F(grown.Waited)} s)"))
            {
                var pick = grown.Go.GetComponent<Pickable>();
                c.Check(Utils.GetPrefabName(grown.Go) == "LingonberryBush" && pick != null && pick.CanBePicked(), "into a lingonberry bush with berries");
            }
        }
    }

    // ---------- replant.wrongbiome (T17, T18: Meadows part) ----------

    private static IEnumerator RunWrongBiome()
    {
        if (!WorldReady(WrongBiomeName))
        {
            yield break;
        }
        var c = new Checks(WrongBiomeName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, WrongBiomeName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 7);
            var here = Heightmap.FindBiome(rig.Origin);
            var spot = Vector3.zero;
            if (!c.Check(tool != null && here != Heightmap.Biome.AshLands && here != Heightmap.Biome.DeepNorth
                         && FindPlantSpot(rig, taken, 1.4f, out spot, p => Buildable(p)), $"level 7 cultivator, a free spot in the {here}"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            foreach (var key in new[] { "Fiddlehead", "SmokePuff", "LingonberryBush" })
            {
                var kind = PlantCatalog.ByKey(key);
                rig.Give(kind.ItemName, 1);
                var placing = new Placing();
                yield return AimGhost(rig, SaplingPiece(kind), spot, placing);
                c.Check(placing.Status == Player.PlacementStatus.WrongBiome, $"{kind.DisplayName} ghost in the {here}: refused, wrong biome (status {placing.Status})");
                ClearCenter();
                yield return PressPlace(rig, kind.SaplingHash, placing);
                c.Check(!placing.Placed && CountByName(rig.Inv, kind.ItemDisplayName) == 1 && CenterText() == L("$msg_wrongbiome"),
                    $"{kind.DisplayName}: nothing planted ('{CenterText()}')");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- scythe ----------

    private sealed class MowResult
    {
        internal bool Swung;
    }

    // Player with a scythe 1.6 m from target, facing it, one swing; waits for the swing to land.
    private static IEnumerator Mow(Rig rig, Checks c, GameObject target, MowResult result)
    {
        var player = rig.Player;
        result.Swung = false;
        var scythe = rig.Inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Scythe") ?? rig.Give("Scythe", 1);
        if (!c.Check(scythe != null && scythe.m_shared.m_attack != null && scythe.m_shared.m_attack.m_harvest, "scythe given (a harvesting weapon)"))
        {
            yield break;
        }
        yield return Hold(rig, scythe);
        var at = target.transform.position;
        var from = StandPoint(rig.Origin, at, 1.6f);
        MoveTo(rig, from + Vector3.up * 0.05f);
        var dir = at - from;
        dir.y = 0f;
        player.transform.rotation = Quaternion.LookRotation(dir.normalized);
        rig.LookDir(dir.normalized + Vector3.down * 0.2f);
        Physics.SyncTransforms();
        yield return new WaitForSeconds(0.5f);
        var view = target.GetComponent<ZNetView>();
        for (var attempt = 0; attempt < 2 && Alive(view); attempt++)
        {
            var until = Time.time + 3f;
            while (Time.time < until && (player.InAttack() || !player.StartAttack(null, false)))
            {
                yield return null;
            }
            var started = false;
            until = Time.time + 2.5f;
            while (Time.time < until && Alive(view))
            {
                started |= player.InAttack();
                yield return null;
            }
            result.Swung |= started || !Alive(view);
        }
        yield return new WaitForSeconds(0.3f);
    }

    // ---------- replant.scythe (T29: Yggdrasil part, near the spawn) ----------

    private static IEnumerator RunScythe()
    {
        if (!WorldReady(ScytheName))
        {
            yield break;
        }
        var c = new Checks(ScytheName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, ScytheName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            ClockOk(c);
            RootGate.ClearLedger();
            var ygg = PlantCatalog.ByKey("YggaShoot");
            var fiddle = PlantCatalog.ByKey("Fiddlehead");
            if (!SpawnRoot(rig, c, taken, out _, out var root, out var rootSpot))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            // Room to stand next to it is part of the search (the root mesh is 40 m wide: the first spot in reach
            // often has a branch where the player would stand).
            var spots = new List<Vector3>();
            var home = rig.Origin;
            if (!c.Check(SpotsNearRoot(rootSpot, home, root, 6f, ygg.GrowRadius + 0.3f, 1, spots, p => StandOk(home, p)), "spot in reach of the root with room to stand next to it"))
            {
                c.Report();
                yield break;
            }
            // Root too low: the Yggdrasil transplant is due, healthy, and waits for sap.
            root.m_nview.GetZDO().Set(ZDOVars.s_level, 5f);
            var waiting = rig.Spawn(ygg.SaplingName, spots[0], Quaternion.identity);
            // Control 1 m beside it: a transplant that cannot grow here (wrong biome).
            var beside = spots[0] + Vector3.Cross(Vector3.up, (rig.Origin - spots[0]).normalized) * 1.1f;
            beside.y = Ground(beside);
            var sick = rig.Spawn(fiddle.SaplingName, beside, Quaternion.identity);
            yield return Settle();
            var waitView = waiting.GetComponent<ZNetView>();
            var sickView = sick.GetComponent<ZNetView>();
            var waitPlant = waiting.GetComponent<Plant>();
            var sickPlant = sick.GetComponent<Plant>();
            var drawer = waiting.GetComponent<RootDrawer>();
            Age(waitView, 8000.0);
            Age(sickView, 20000.0);
            // Sick one first: until its own health check ran it counts as healthy, and a healthy plant 1 m beside
            // the Yggdrasil transplant takes its grow space (vanilla Plant.HaveGrowSpace).
            sickPlant.UpdateHealth(20000.0);
            waitPlant.UpdateHealth(8000.0);
            drawer.NextTry = 0f;
            var none = waitPlant.Grow();
            rig.Track(none);
            c.Check(none == null && waitPlant.GetStatus() == Plant.Status.Healthy && drawer.LastState == RootState.Waiting,
                $"Yggdrasil transplant waits for sap, healthy ({waitPlant.GetStatus()}, {drawer.LastState})");
            c.Check(sickPlant.GetStatus() == Plant.Status.WrongBiome, $"control: a fiddlehead transplant beside it cannot grow here ({sickPlant.GetStatus()})");

            var mown = new MowResult();
            yield return Mow(rig, c, sick, mown);
            c.Check(mown.Swung && !Alive(sickView), $"control: the scythe swing destroys the transplant that cannot grow (swing started {mown.Swung})");
            yield return new WaitForSeconds(0.5f);
            c.Check(Alive(waitView), "the Yggdrasil transplant that waits for sap stays");
            // Was it under the same swing? Harvest circle of the scythe (vanilla Attack: range ahead of the player,
            // radius by Farming skill) against where the Yggdrasil transplant stands.
            var scythe = player.GetRightItem();
            var attack = scythe != null ? scythe.m_shared.m_attack : null;
            if (c.Check(attack != null && attack.m_harvest, "scythe still in hand"))
            {
                var radius = Mathf.Lerp(attack.m_harvestRadius, attack.m_harvestRadiusMaxLevel, player.GetSkillFactor(Skills.SkillType.Farming));
                var centre = player.transform.position + player.transform.forward * attack.m_attackRange;
                var off = spots[0] - centre;
                off.y = 0f;
                c.Check(off.magnitude < radius, $"it stood inside the harvest circle of that swing ({F(off.magnitude)} m from its centre, radius {F(radius)} m)");
            }
            c.Report();
        }
        finally
        {
            RootGate.ClearLedger();
            rig.Restore();
        }
    }
}
#endif
