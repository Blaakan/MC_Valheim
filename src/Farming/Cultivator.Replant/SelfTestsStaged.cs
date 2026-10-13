#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Staged self tests (same class as SelfTests.cs): things that only show after a real log out or a
// real game restart with every MC mod Enabled = false. One small test per TESTING.md item, so one can be fixed or
// dropped alone:
//   replant.disabled.kept     T31: first game leave transplants in the bag and in a chest, a level 4 cultivator and a
//                             young transplant; second game (mod off from the start) find them all again, young one
//                             keep its name and grow, no transplant piece in the build menu
//   replant.disabled.owngrow  T39 restart part: young transplant loaded with the mod off from the start carry this
//                             player's own grow time (config file as it is); own setting halved in memory = it stay
//                             young at the old time and grow at the half time
//   replant.relog.world       M09 save part (single player world, same save code as a server): dropped transplant,
//                             young transplant and transplants in the bag are still there after log out and load
// Disabled-start tests registered from Plugin.BindConfig (RegisterAtLoad): OnActivated never run in the second game.
// Me never write a setting: second game read the config file as the script left it, own grow time forced only with
// ServerRules.TestOwn (memory).
internal static partial class SelfTests
{
    private const string DisabledKeptName = "replant.disabled.kept";
    private const string DisabledGrowName = "replant.disabled.owngrow";
    private const string RelogWorldName = "replant.relog.world";

    private const string ChestPrefab = "piece_chest_wood";

    private static readonly float[] LeaveRings = { 7f, 9f, 11f, 13f, 15f, 17f, 19f, 22f };

    // Plugin.BindConfig, Debug only: tests that must be known while the mod is off (second game of a run, server
    // without the mod). Never unregistered.
    internal static void RegisterAtLoad()
    {
        SelfTest.RegisterDisabledStart(DisabledKeptName, DisabledKeptLeave, DisabledKeptCheck);
        SelfTest.RegisterDisabledStart(DisabledGrowName, DisabledGrowLeave, DisabledGrowCheck);
        RegisterNoServerTest();
    }

    private static void RegisterStaged()
    {
        SelfTest.RegisterRelog(RelogWorldName, RelogWorldBefore, null, RelogWorldAfter);
    }

    private static void UnregisterStaged()
    {
        SelfTest.UnregisterRelog(RelogWorldName);
    }

    // ---------- small tools ----------

    private static string Vec(Vector3 v) =>
        v.x.ToString("R", CultureInfo.InvariantCulture) + ";" + v.y.ToString("R", CultureInfo.InvariantCulture) + ";" + v.z.ToString("R", CultureInfo.InvariantCulture);

    private static bool TryVec(string text, out Vector3 v)
    {
        v = Vector3.zero;
        var parts = (text ?? "").Split(';');
        return parts.Length == 3
               && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v.x)
               && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v.y)
               && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v.z);
    }

    private static bool GrowTimesAre(Plant plant, PlantKind kind, float multiplier)
    {
        TransplantContent.GrowTimes(kind, multiplier, out var low, out var high);
        return plant != null && Near(plant.m_growTime, low, 0.5f) && Near(plant.m_growTimeMax, high, 0.5f);
    }

    private static string GrowTimesText(Plant plant) => plant != null ? $"{F(plant.m_growTime)}-{F(plant.m_growTimeMax)} s" : "no plant";

    private static string GrowTimesOf(PlantKind kind, float multiplier)
    {
        TransplantContent.GrowTimes(kind, multiplier, out var low, out var high);
        return $"x{F(multiplier)} = {F(low)}-{F(high)} s";
    }

    // Free spot some metres from the player (no Rig here: what a leave part puts down must stay).
    private static bool LeaveSpot(Player p, List<Vector3> taken, float clear, out Vector3 spot) =>
        FindSpot(p.transform.position, p.transform.forward, LeaveRings, clear, taken, out spot);

    private static void DestroyOwned(GameObject go)
    {
        if (go == null || ZNetScene.instance == null)
        {
            return;
        }
        var view = go.GetComponent<ZNetView>();
        if (view != null && view.IsValid())
        {
            view.ClaimOwnership();
        }
        ZNetScene.instance.Destroy(go);
    }

    // Takes up to count items of this shared name out of the bag again.
    private static void TakeBack(Inventory inv, string sharedName, int count)
    {
        var have = CountByName(inv, sharedName);
        var take = Mathf.Min(count, have);
        if (take > 0)
        {
            inv.RemoveItem(sharedName, take);
        }
    }

    private static bool StagedReady(string name)
    {
        if (Player.m_localPlayer == null || ZNetScene.instance == null || ObjectDB.instance == null || ZNet.instance == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(name, "no player, network list, item database or world");
            return false;
        }
        return true;
    }

    // Wait until the slow update loop looked at this plant once more (it tries growth every 10 s).
    private static IEnumerator NextPlantPass(Plant plant, float seconds)
    {
        var seen = plant != null ? plant.m_updateTime : 0f;
        var until = Time.time + seconds;
        while (plant != null && Time.time < until && Mathf.Approximately(plant.m_updateTime, seen))
        {
            yield return null;
        }
        yield return null;
    }

    // ---------- replant.disabled.kept (T31) ----------

    private static IEnumerator DisabledKeptLeave()
    {
        if (!StagedReady(DisabledKeptName))
        {
            yield break;
        }
        var c = new Checks(DisabledKeptName);
        var p = Player.m_localPlayer;
        var inv = p.GetInventory();
        var rasp = PlantCatalog.ByKey("RaspberryBush");
        var name = rasp.ItemDisplayName;
        var taken = new List<Vector3>();
        if (!c.Check(Plugin.FeatureActive && SelfTest.Stage == "" && TransplantContent.SaplingPrefab(rasp) != null && ZNetScene.instance.GetPrefab(ChestPrefab) != null,
                $"first game with the mod on (stage '{SelfTest.Stage}'), young raspberry transplant and wooden chest known to the game"))
        {
            c.Report();
            yield break;
        }
        var bagBefore = CountByName(inv, name);
        inv.AddItem(rasp.ItemName, 3, 1, 0, 0L, "", false);
        var tool = inv.AddItem(PlantCatalog.CultivatorPrefab, 1, 4, 0, 0L, "", false);
        var bag = CountByName(inv, name);
        c.Check(bag == bagBefore + 3 && tool != null && tool.m_quality == 4, $"three transplants ({bagBefore} -> {bag}) and a level 4 cultivator put in the inventory");
        var chestSpot = Vector3.zero;
        var plantSpot = Vector3.zero;
        if (!c.Check(LeaveSpot(p, taken, 1.6f, out chestSpot) && LeaveSpot(p, taken, 1.4f, out plantSpot), "two free spots near the player"))
        {
            c.Report();
            yield break;
        }
        var chestGo = Object.Instantiate(ZNetScene.instance.GetPrefab(ChestPrefab), chestSpot, Quaternion.identity);
        var sapling = Object.Instantiate(TransplantContent.SaplingPrefab(rasp), plantSpot, Quaternion.identity);
        yield return Settle();
        var chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
        var stored = chest != null ? chest.GetInventory().AddItem(rasp.ItemName, 2, 1, 0, 0L, "", false) : null;
        var plant = sapling != null ? sapling.GetComponent<Plant>() : null;
        c.Check(stored != null && CountByName(chest.GetInventory(), name) == 2, "a wooden chest with two transplants stands near the player");
        c.Check(plant != null && Alive(sapling.GetComponent<ZNetView>()), "a young raspberry transplant is planted near the player");
        // Chest writes its content into its saved data on the next change tick: one second is plenty.
        yield return new WaitForSeconds(1.5f);
        var carried = SelfTest.Carry(DisabledKeptName + ".chest", Vec(chestSpot))
                      && SelfTest.Carry(DisabledKeptName + ".plant", Vec(plantSpot))
                      && SelfTest.Carry(DisabledKeptName + ".bag", bag.ToString(CultureInfo.InvariantCulture));
        c.Check(carried, "places and counts kept for the second game (run folder of the probe)");
        c.Report(": left for the game that starts with the mod off");
    }

    private static IEnumerator DisabledKeptCheck()
    {
        if (!StagedReady(DisabledKeptName))
        {
            yield break;
        }
        var c = new Checks(DisabledKeptName);
        var p = Player.m_localPlayer;
        var inv = p.GetInventory();
        var rasp = PlantCatalog.ByKey("RaspberryBush");
        var name = rasp.ItemDisplayName;
        var rig = new Rig(p, DisabledKeptName);
        ZNetView chestView = null;
        ZNetView sapView = null;
        var given = 0;
        ItemDrop.ItemData tool = null;
        try
        {
            var me = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(SelfTest.Stage == SelfTest.DisabledStage && me != null && me.Value.IsDisabledByUser && !Plugin.FeatureActive && !FeaturePatched(),
                $"second game, started with Enabled = false: stage '{SelfTest.Stage}', state {(me != null ? me.Value.State : "?")}, status '{(me != null ? me.Value.Status : "?")}', "
                + $"feature patches applied {FeaturePatched()}");
            var havePlaces = TryVec(SelfTest.Carried(DisabledKeptName + ".chest"), out var chestSpot) & TryVec(SelfTest.Carried(DisabledKeptName + ".plant"), out var plantSpot);
            var haveBag = int.TryParse(SelfTest.Carried(DisabledKeptName + ".bag"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bag);
            if (!c.Check(havePlaces && haveBag, "the first game left its places and counts (its leave part ran and passed)"))
            {
                c.Report();
                yield break;
            }
            given = 3;
            c.Note($"player {F(Vector3.Distance(p.transform.position, chestSpot))} m from the chest, {F(Vector3.Distance(p.transform.position, plantSpot))} m from the young transplant");

            // Inventory, loaded from the character file with the mod off.
            c.Check(ObjectDB.instance.GetItemPrefab(rasp.ItemName) != null && ZNetScene.instance.GetPrefab(rasp.SaplingHash) != null,
                "mod off from the start: transplant item and young plant are still known to the game");
            c.Check(CountByName(inv, name) == bag, $"the transplants are still in the inventory ({CountByName(inv, name)}, {bag} when the first game quit)");
            tool = inv.GetAllItems().FirstOrDefault(i => CultivatorTiers.IsCultivator(i) && i.m_quality == 4);
            c.Check(tool != null, "the level 4 cultivator is still level 4");

            // Chest, loaded from the world file.
            var chestHash = ChestPrefab.GetStableHashCode();
            yield return WaitFor(() => FindNear(new[] { chestHash }, chestSpot, 1.5f) != null, 15f);
            chestView = FindNear(new[] { chestHash }, chestSpot, 1.5f);
            var chest = chestView != null ? chestView.GetComponent<Container>() : null;
            if (c.Check(chest != null, "the chest is still there"))
            {
                yield return WaitFor(() => CountByName(chest.GetInventory(), name) > 0, 4f);
                c.Check(CountByName(chest.GetInventory(), name) == 2, $"the chest still holds its two transplants ({CountByName(chest.GetInventory(), name)})");
            }

            // Young transplant, loaded from the world file: name on hover, then growth on the clock.
            sapView = FindNear(new[] { rasp.SaplingHash }, plantSpot, 1.5f);
            var plant = sapView != null ? sapView.GetComponent<Plant>() : null;
            if (c.Check(plant != null, "the young transplant is still there"))
            {
                plant.UpdateHealth(100.0);
                c.Check(plant.GetHoverText().Contains(name) && plant.GetHoverName().Contains(name),
                    $"its hover text shows its name ('{OneLine(plant.GetHoverText())}')");
            }

            // Build menu of the cultivator: no transplant piece.
            if (tool != null)
            {
                yield return Hold(rig, tool);
                // Game's own lists made new with the transplants in the bag: a transplant piece left on would be
                // learned and listed now (control: the character has had the transplant).
                p.UpdateKnownRecipesList();
                p.UpdateAvailablePiecesList();
                var pieces = p.m_buildPieces != null ? p.m_buildPieces.m_availablePieces.Count : -1;
                c.Check(p.InPlaceMode() && pieces > 0 && p.m_knownMaterial.Contains(name) && CountByName(inv, name) > 0,
                    $"cultivator in hand: build menu with {pieces} piece(s), transplants carried and known to the character (control)");
                c.Check(SaplingPiecesOffered(p) == 0, $"no transplant in the build menu ({SaplingPiecesOffered(p)})");
                p.UnequipItem(tool, false);
                yield return Frames(2);
            }

            // It grows (vanilla sapling rules, this player's own grow time).
            if (plant != null)
            {
                MakeClockOld();
                yield return GrowReady(rig, c);
                var own = ServerRules.OwnRules.GrowTimeMultiplier;
                TransplantContent.GrowTimes(rasp, own, out _, out var high);
                Age(sapView, high + 200.0);
                var grown = new Grown();
                yield return WaitGrown(rig, rasp, sapView, plantSpot, 26f, grown);
                c.Check(grown.Ok, $"the young transplant grows into a raspberry bush once {F(high + 200f)} s are on its clock ({F(grown.Waited)} s waited)");
            }
            c.Report();
        }
        finally
        {
            // Throwaway world, still: what the first game left goes away again.
            rig.Restore();
            TakeBack(inv, name, given);
            if (tool != null && inv.ContainsItem(tool))
            {
                if (p.IsItemEquiped(tool))
                {
                    p.UnequipItem(tool, false);
                }
                inv.RemoveItem(tool);
            }
            if (chestView != null)
            {
                DestroyOwned(chestView.gameObject);
            }
            if (Alive(sapView))
            {
                DestroyOwned(sapView.gameObject);
            }
        }
    }

    // ---------- replant.disabled.owngrow (T39, restart part) ----------

    private static IEnumerator DisabledGrowLeave()
    {
        if (!StagedReady(DisabledGrowName))
        {
            yield break;
        }
        var c = new Checks(DisabledGrowName);
        var p = Player.m_localPlayer;
        var rasp = PlantCatalog.ByKey("RaspberryBush");
        var taken = new List<Vector3>();
        var spot = Vector3.zero;
        if (!c.Check(Plugin.FeatureActive && SelfTest.Stage == "" && TransplantContent.SaplingPrefab(rasp) != null && LeaveSpot(p, taken, 1.4f, out spot),
                "first game with the mod on, a free spot near the player"))
        {
            c.Report();
            yield break;
        }
        var sapling = Object.Instantiate(TransplantContent.SaplingPrefab(rasp), spot, Quaternion.identity);
        yield return Settle();
        c.Check(sapling != null && Alive(sapling.GetComponent<ZNetView>()), "a young raspberry transplant is planted near the player");
        c.Check(SelfTest.Carry(DisabledGrowName + ".plant", Vec(spot)), "its place kept for the second game (run folder of the probe)");
        c.Report(": left for the game that starts with the mod off");
    }

    private static IEnumerator DisabledGrowCheck()
    {
        if (!StagedReady(DisabledGrowName))
        {
            yield break;
        }
        var c = new Checks(DisabledGrowName);
        var p = Player.m_localPlayer;
        var rasp = PlantCatalog.ByKey("RaspberryBush");
        var rig = new Rig(p, DisabledGrowName);
        ZNetView sapView = null;
        try
        {
            var me = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(SelfTest.Stage == SelfTest.DisabledStage && me != null && me.Value.IsDisabledByUser && !Plugin.FeatureActive && !FeaturePatched(),
                $"second game, started with Enabled = false: stage '{SelfTest.Stage}', state {(me != null ? me.Value.State : "?")}");
            if (!c.Check(TryVec(SelfTest.Carried(DisabledGrowName + ".plant"), out var spot), "the first game left the place of its young transplant (its leave part ran and passed)"))
            {
                c.Report();
                yield break;
            }
            yield return WaitFor(() => FindNear(new[] { rasp.SaplingHash }, spot, 1.5f) != null, 15f);
            sapView = FindNear(new[] { rasp.SaplingHash }, spot, 1.5f);
            var plant = sapView != null ? sapView.GetComponent<Plant>() : null;
            if (!c.Check(plant != null, "the young transplant is still there, loaded with the mod off"))
            {
                c.Report();
                yield break;
            }
            MakeClockOld();
            yield return GrowReady(rig, c);

            // Own setting as the config file has it (never written here): that is the time the transplant carries.
            var own = CultivatorRules.Own().GrowTimeMultiplier;
            var prefab = TransplantContent.SaplingPrefab(rasp);
            var prefabPlant = prefab != null ? prefab.GetComponent<Plant>() : null;
            c.Check(ServerRules.TestOwn == null && GrowTimesAre(plant, rasp, own) && GrowTimesAre(prefabPlant, rasp, own),
                $"mod off from the start: the loaded transplant ({GrowTimesText(plant)}) and the young-plant prefab ({GrowTimesText(prefabPlant)}) carry this player's own "
                + $"GrowTimeMultiplier from the config file ({GrowTimesOf(rasp, own)})");

            // Half of it (0.5 for the default 1: 2.5 h instead of 5 h), in memory. Clock set between the two times:
            // young on the own time, grown on the half time.
            var half = own * 0.5f;
            TransplantContent.GrowTimes(rasp, own, out var low, out _);
            TransplantContent.GrowTimes(rasp, half, out _, out var halfHigh);
            var age = halfHigh + 100f;
            if (!c.Check(half >= CultivatorRules.GrowTimeMultiplierMin && age < low,
                    $"own GrowTimeMultiplier {F(own)} can be halved for the check (half time ends at {F(halfHigh)} s, own time starts at {F(low)} s)"))
            {
                c.Report();
                yield break;
            }
            yield return WaitFor(() => Time.time - plant.m_spawnTime > 10.5f, 12f);
            Age(sapView, age);
            yield return NextPlantPass(plant, 13f);
            c.Check(Alive(sapView), $"with {F(age)} s on its clock it stays a young plant on the own setting x{F(own)} (needs {F(low)} s or more)");
            rig.Undo(() => ServerRules.TestOwn = null);
            ServerRules.TestOwn = new CultivatorRules { GrowTimeMultiplier = half };
            c.Check(!TransplantContent.RebuildPending && GrowTimesAre(plant, rasp, half) && GrowTimesAre(prefabPlant, rasp, half),
                $"own GrowTimeMultiplier x{F(half)} (in memory) with the mod off from the start: at once on the loaded transplant ({GrowTimesText(plant)}) "
                + $"and on the prefab ({GrowTimesText(prefabPlant)})");
            var grown = new Grown();
            yield return WaitGrown(rig, rasp, sapView, spot, 26f, grown);
            c.Check(grown.Ok, $"and with the same {F(age)} s on its clock it now grows ({F(grown.Waited)} s waited): x{F(half)} needs {F(halfHigh)} s at most, not {F(low)} s");
            c.Report();
        }
        finally
        {
            rig.Restore();
            if (Alive(sapView))
            {
                DestroyOwned(sapView.gameObject);
            }
        }
    }

    // ---------- replant.relog.world (M09: what a save and a load keep) ----------

    private static Vector3 _relogPlantSpot;
    private static Vector3 _relogDropSpot;
    private static int _relogBag;
    private static bool _relogLeft;

    private static IEnumerator RelogWorldBefore()
    {
        _relogLeft = false;
        if (!StagedReady(RelogWorldName))
        {
            yield break;
        }
        var c = new Checks(RelogWorldName);
        var p = Player.m_localPlayer;
        var inv = p.GetInventory();
        var rasp = PlantCatalog.ByKey("RaspberryBush");
        var name = rasp.ItemDisplayName;
        var taken = new List<Vector3>();
        var itemPrefab = TransplantContent.ItemPrefab(rasp);
        if (!c.Check(Plugin.FeatureActive && itemPrefab != null && TransplantContent.SaplingPrefab(rasp) != null
                     && LeaveSpot(p, taken, 1.4f, out _relogPlantSpot) && LeaveSpot(p, taken, 1.2f, out _relogDropSpot),
                "mod on, two free spots some metres from the player (out of auto pickup reach)"))
        {
            c.Report();
            yield break;
        }
        _relogLeft = true;
        inv.AddItem(rasp.ItemName, 2, 1, 0, 0L, "", false);
        _relogBag = CountByName(inv, name);
        var sapling = Object.Instantiate(TransplantContent.SaplingPrefab(rasp), _relogPlantSpot, Quaternion.identity);
        // Vanilla drop call (what a drop from the inventory ends in), at a place the player does not stand on.
        var data = itemPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
        data.m_dropPrefab = itemPrefab;
        var dropped = ItemDrop.DropItem(data, 1, _relogDropSpot + Vector3.up * 0.6f, Quaternion.identity);
        yield return Settle();
        yield return new WaitForSeconds(1f);
        c.Check(sapling != null && Alive(sapling.GetComponent<ZNetView>()), "before the log out: a young raspberry transplant is planted");
        c.Check(dropped != null && DropsNear(_relogDropSpot, name, 3f).Count == 1, $"before the log out: one transplant lies on the ground ({DropsNear(_relogDropSpot, name, 3f).Count})");
        c.Check(_relogBag >= 2, $"before the log out: {_relogBag} transplant(s) in the inventory");
        c.Report(": now the log out");
    }

    private static IEnumerator RelogWorldAfter()
    {
        if (!StagedReady(RelogWorldName))
        {
            yield break;
        }
        var c = new Checks(RelogWorldName);
        var p = Player.m_localPlayer;
        var inv = p.GetInventory();
        var rasp = PlantCatalog.ByKey("RaspberryBush");
        var name = rasp.ItemDisplayName;
        ZNetView sapView = null;
        var drops = new List<ItemDrop>();
        try
        {
            if (!c.Check(_relogLeft, "the part before the log out put its things down"))
            {
                c.Report();
                yield break;
            }
            c.Check(Plugin.FeatureActive, "mod on after logging back in");
            yield return WaitFor(() => FindNear(new[] { rasp.SaplingHash }, _relogPlantSpot, 1.5f) != null && DropsNear(_relogDropSpot, name, 3f).Count > 0, 15f);
            sapView = FindNear(new[] { rasp.SaplingHash }, _relogPlantSpot, 1.5f);
            var plant = sapView != null ? sapView.GetComponent<Plant>() : null;
            if (c.Check(plant != null, "after the save and the load: the young transplant is still there"))
            {
                plant.UpdateHealth(100.0);
                c.Check(plant.GetHoverText().Contains(name), $"with its name on hover ('{OneLine(plant.GetHoverText())}')");
                c.Check(GrowTimesAre(plant, rasp, ServerRules.Current.GrowTimeMultiplier),
                    $"and the grow time of the rules in force ({GrowTimesText(plant)}, want {GrowTimesOf(rasp, ServerRules.Current.GrowTimeMultiplier)})");
            }
            drops = DropsNear(_relogDropSpot, name, 3f);
            c.Check(drops.Count == 1 && drops[0].m_itemData.m_stack == 1 && TransplantContent.KindOfItem(drops[0].m_itemData) == rasp,
                $"after the save and the load: the dropped transplant still lies there ({drops.Count} drop(s)) and is a raspberry transplant");
            c.Check(CountByName(inv, name) == _relogBag, $"the inventory still has its {_relogBag} transplant(s) ({CountByName(inv, name)})");
            c.Report();
        }
        finally
        {
            if (_relogLeft)
            {
                TakeBack(inv, name, 2);
                foreach (var drop in DropsNear(_relogDropSpot, name, 3f))
                {
                    DestroyOwned(drop.gameObject);
                }
                var left = sapView != null ? sapView : FindNear(new[] { rasp.SaplingHash }, _relogPlantSpot, 1.5f);
                if (Alive(left))
                {
                    DestroyOwned(left.gameObject);
                }
            }
            _relogLeft = false;
        }
    }
}
#endif
