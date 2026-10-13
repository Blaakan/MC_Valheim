#if DEBUG
using System;
using System.Collections;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. In-world self tests that press real keys, mouse buttons, wheel and mouse movement (SelfTest.Key,
// MouseWheel, MouseMove: device state changed by the world probe, the game read it like from a person), most of them
// with another MC mod loaded in the same game. One small test per TESTING.md item part, so one can be fixed alone.
// Other mod found by GUID in the shared registry, its settings through BepInEx config, what it does through the game
// (item in the hand, player ZDO, water level, RenderSettings) or its own AppDomain slot: never through its types.
//   horizons.input.spyglass           C07: real MC_Spyglass in the right hand, real left click = up, real wheel = x8
//                                     (or the spyglass's own maximum): the Spyglass mod's own message reaches my
//                                     reader; land 2 to 5 km out where it looks is built finer within 8 s, object tiles
//                                     and buildings farther out, no long freeze; real right click = down: within 3 s
//                                     nothing wanted past the no-spyglass reach, objects back, no hole, nothing twice
//   horizons.input.spyglass-turn      C07: spyglass up, view turned 60 degrees with real mouse movement: the new
//                                     direction gets detail the no-spyglass rule never gives, more far tiles than
//                                     after lowering
//   horizons.input.spyglass-settings  C08: real spyglass up at full zoom: SpyglassDetail off = no detail changes;
//                                     SpyglassMaxBoost 1 = none; 4 = more; 8 = more than 4 (settings forced in memory)
//   horizons.input.swim-dive          C01: Swim Dive loaded; player dropped in deep sea, real Crouch key held = camera
//                                     under water: fog on screen is Swim Dive's own every frame (not my thin fog), far
//                                     ground whole; far-sea sheet faces up only; real Jump key = back up: far sea
//                                     painted, game's distant water plane out of reach, my clear-weather fog back
//   horizons.input.fly                T04: debug fly with real Forward + Run keys at the game's own speed (50 m/s),
//                                     out and back: far ground under every point at every check, nothing twice, no far
//                                     tile as real ground, no freeze; after: zones load, finest tile under camera
// Settings forced only in memory (Kit.Force), weather with EnvMan debug fields, all put back in finally.
internal static class InputTests
{
    private const string SpyglassName = "horizons.input.spyglass";
    private const string SpyglassTurnName = "horizons.input.spyglass-turn";
    private const string SpyglassSettingsName = "horizons.input.spyglass-settings";
    private const string DiveName = "horizons.input.swim-dive";
    private const string FlyName = "horizons.input.fly";

    private const string SpyglassGuid = "MC.Exploration.View.Spyglass";
    private const string SpyglassItem = "MC_Spyglass"; // SpyglassContent.ItemName of that mod
    private const string DiveGuid = "MC.Exploration.Swimming.Dive";

    internal static void Register()
    {
        SelfTest.Register(SpyglassName, RunSpyglass);
        SelfTest.Register(SpyglassTurnName, RunSpyglassTurn);
        SelfTest.Register(SpyglassSettingsName, RunSpyglassSettings);
        SelfTest.Register(DiveName, RunDive);
        SelfTest.Register(FlyName, RunFly);
    }

    internal static void Unregister()
    {
        SelfTest.Unregister(SpyglassName);
        SelfTest.Unregister(SpyglassTurnName);
        SelfTest.Unregister(SpyglassSettingsName);
        SelfTest.Unregister(DiveName);
        SelfTest.Unregister(FlyName);
    }

    // ---------- shared

    private static IEnumerator Ready(Kit.Checks c, Kit.Box ready)
    {
        ready.Ok = false;
        if (Player.m_localPlayer == null || Utils.GetMainCamera() == null || GameCamera.instance == null || EnvMan.instance == null || ZNet.instance == null)
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
        yield return Kit.WaitTerrain(up);
        ready.Ok = c.Check(up.Ok, up.Detail);
    }

    // Other MC mod loaded and active? state = what the registry say (for the fail text).
    internal static bool ModActive(string guid, out string state)
    {
        var mod = FeatureRegistry.Find(guid);
        state = mod.HasValue ? $"{mod.Value.State}, \"{mod.Value.Status}\"" : "not loaded";
        return mod.HasValue && mod.Value.IsActive;
    }

    // Setting of another plugin as it is now (BepInEx config, no type of theirs).
    internal static bool OtherSetting<T>(string guid, string section, string key, out T value)
    {
        value = default;
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info.Instance == null
            || !info.Instance.Config.TryGetEntry<T>(section, key, out var entry))
        {
            return false;
        }
        value = entry.Value;
        return true;
    }

    // Game button bound to this key in this profile (player can rebind)? Test can only press the key it names.
    private static bool Bound(string button, KeyCode key, out string path)
    {
        var def = ZInput.instance != null ? ZInput.instance.GetButtonDef(button) : null;
        path = def != null ? def.GetActionPath() : null;
        return path != null && string.Equals(path, ZInput.KeyCodeToPath(key), StringComparison.OrdinalIgnoreCase);
    }

    // ---------- the real spyglass

    // What the Spyglass mod wrote for me (slot "MC.ViewBoost.v1", layout in ViewBoost.cs), as it is in the AppDomain:
    // not my test stand-in.
    private struct Glass
    {
        internal bool Fresh;      // written in one of the last two frames
        internal float Zoom;
        internal float Weight;
        internal Vector2 Forward; // XZ, normalised (zero = none)
    }

    private static Glass ReadGlass()
    {
        var g = new Glass();
        var s = AppDomain.CurrentDomain.GetData(ViewBoost.SlotName) as double[];
        if (s == null || s.Length < 9 || (int)s[0] != 1)
        {
            return g;
        }
        g.Fresh = Time.frameCount - s[1] <= 2;
        g.Zoom = (float)s[2];
        g.Weight = (float)s[3];
        var f = new Vector2((float)s[4], (float)s[6]);
        g.Forward = f.sqrMagnitude > 0.0001f ? f.normalized : Vector2.zero;
        return g;
    }

    // Spyglass in the right hand; old hands, inventory and look direction put back.
    private sealed class SpyRig
    {
        private readonly Player _p;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly Vector3 _look;
        private ItemDrop.ItemData _item;

        internal SpyRig(Player p)
        {
            _p = p;
            _right = p.m_rightItem;
            _left = p.m_leftItem;
            _look = p.GetLookDir();
        }

        internal bool Give(out string why)
        {
            why = "";
            if (ObjectDB.instance == null || ObjectDB.instance.GetItemPrefab(SpyglassItem) == null)
            {
                why = $"the game knows no item '{SpyglassItem}' (the Spyglass mod's item)";
                return false;
            }
            _item = _p.GetInventory().AddItem(SpyglassItem, 1, 1, 0, 0L, "", false);
            if (_item == null)
            {
                why = "the spyglass could not be put in the inventory";
                return false;
            }
            if (!_p.EquipItem(_item, false) || !ReferenceEquals(_p.m_rightItem, _item))
            {
                why = "the spyglass could not be taken in the right hand";
                return false;
            }
            return true;
        }

        internal void Restore()
        {
            if (_p == null)
            {
                return;
            }
            var item = _item;
            _item = null;
            if (item != null)
            {
                Kit.Safe("spyglass out of the hand", () =>
                {
                    if (_p.IsItemEquiped(item))
                    {
                        _p.UnequipItem(item, false);
                    }
                });
                Kit.Safe("spyglass out of the inventory", () => _p.GetInventory().RemoveItem(item));
            }
            Kit.Safe("old hands", () =>
            {
                var inv = _p.GetInventory();
                if (_right != null && inv.ContainsItem(_right) && !_p.IsItemEquiped(_right))
                {
                    _p.EquipItem(_right, false);
                }
                if (_left != null && inv.ContainsItem(_left) && !_p.IsItemEquiped(_left))
                {
                    _p.EquipItem(_left, false);
                }
            });
            Kit.Safe("look direction", () => _p.SetLookDir(_look));
        }
    }

    private sealed class SpyRun
    {
        internal SpyRig Rig;
        internal Vector2 Dir;        // where the player looks (XZ)
        internal float Want;         // zoom the wheel must reach
        internal float ObjectsBeyond;
        internal float PiecesBeyond;
    }

    // Direction (of 16) with most land 2 to 5 km out: "toward land" of the test list. land = land points of 7.
    private static Vector3 LandDirection(Vector3 from, out int land)
    {
        var wg = WorldGenerator.instance;
        var water = ZoneSystem.instance.m_waterLevel;
        var radius = Plugin.Cfg.V(Plugin.Cfg.WorldRadius) - 200f;
        var best = Vector3.forward;
        land = -1;
        for (var i = 0; i < 16; i++)
        {
            var a = i * Mathf.PI / 8f;
            var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            var n = 0;
            for (var r = 2000f; r <= 5000f; r += 500f)
            {
                var p = from + d * r;
                if (p.x * p.x + p.z * p.z < radius * radius && wg.GetHeight(p.x, p.z) > water + 1f)
                {
                    n++;
                }
            }
            if (n > land)
            {
                land = n;
                best = d;
            }
        }
        return best;
    }

    // Both mods there, buttons on the mouse, default spyglass settings of mine (in memory), spyglass in the hand,
    // player looking toward land, everything settled.
    private static IEnumerator PrepareSpyglass(Kit.Checks c, SpyRun run, Kit.Box ok)
    {
        ok.Ok = false;
        var ready = new Kit.Box();
        yield return Ready(c, ready);
        if (!ready.Ok)
        {
            yield break;
        }
        if (!ModActive(SpyglassGuid, out var state))
        {
            // No pass without a check: a skipped test must not read as proof.
            c.Check(false, $"the Spyglass mod is not loaded and active ({state}): nothing was checked");
            yield break;
        }
        var attackOk = Bound("Attack", KeyCode.Mouse0, out var attackPath);
        var blockOk = Bound("Block", KeyCode.Mouse1, out var blockPath);
        if (!attackOk || !blockOk)
        {
            c.Check(false, $"Attack is bound to '{attackPath}' and Block to '{blockPath}': the test can only press the left and the right mouse button");
            yield break;
        }
        var player = Player.m_localPlayer;
        var cfg = Plugin.Cfg;
        Kit.Force(cfg.SpyglassDetail, (bool)cfg.SpyglassDetail.DefaultValue);
        Kit.Force(cfg.SpyglassMaxBoost, (float)cfg.SpyglassMaxBoost.DefaultValue);
        Kit.Force(cfg.ObjectsEnabled, true);
        Kit.Force(cfg.DrawPieces, true);
        ViewBoost.TestSlot = null;
        run.ObjectsBeyond = cfg.ObjectFarDistance.Value * 1.08f + 65f;
        run.PiecesBeyond = cfg.PieceDistance.Value * 1.08f + 1f;
        run.Want = OtherSetting<float>(SpyglassGuid, "Zoom", "MaxMagnification", out var max) ? Mathf.Min(8f, max) : 8f;

        run.Rig = new SpyRig(player);
        if (!run.Rig.Give(out var why))
        {
            c.Check(false, why);
            yield break;
        }
        var dir = LandDirection(player.transform.position, out var land);
        player.SetLookDir(dir);
        run.Dir = new Vector2(dir.x, dir.z).normalized;
        c.Note($"spyglass in the right hand, looking toward ({dir.x:0.00},{dir.z:0.00}): land at {land} of 7 points 2 to 5 km out; the spyglass zooms to x{run.Want:0.#}");
        var settled = new Kit.Box();
        yield return Kit.Settle(settled, 40f, objects: true);
        if (!c.Check(settled.Ok && LodTerrainManager.Instance != null && DistantObjectManager.Instance != null, settled.Detail))
        {
            yield break;
        }
        yield return Kit.Frames(3);
        ok.Ok = true;
    }

    // Real left mouse button down and KEPT down (raises the spyglass in click mode and in hold-to-look mode), until
    // the Spyglass mod's own message says "at the eye".
    private static IEnumerator RaiseReal(Kit.Box result)
    {
        result.Ok = false;
        if (!SelfTest.Key(KeyCode.Mouse0, true))
        {
            result.Detail = "no input driver: the left mouse button could not be pressed (not a probe run?)";
            yield break;
        }
        var start = Kit.Now;
        var g = ReadGlass();
        while (Kit.Now - start < 4f)
        {
            yield return null;
            g = ReadGlass();
            if (g.Fresh && g.Weight >= 0.99f && g.Zoom > 1.01f)
            {
                result.Ok = true;
                break;
            }
        }
        result.Detail = $"{Kit.Now - start:0.0} s after the click the Spyglass mod's message is {(g.Fresh ? "fresh" : "old or missing")}, zoom x{g.Zoom:0.##}, weight {g.Weight:0.##}";
    }

    // Real wheel notches up until the message carries the wanted zoom. Zoom is kept for the session: when it is there
    // already, two notches down first, so the wheel is really used. Ok = wheel moved the zoom up and it got there.
    private static IEnumerator ZoomTo(float want, Kit.Box result)
    {
        var g = ReadGlass();
        var downs = 0;
        while (downs < 2 && g.Fresh && g.Zoom >= want - 0.05f && SelfTest.MouseWheel(-1f))
        {
            downs++;
            yield return Kit.Frames(3);
            g = ReadGlass();
        }
        var low = g.Zoom;
        var ups = 0;
        while (ups < 16 && g.Fresh && g.Zoom < want - 0.05f && SelfTest.MouseWheel(1f))
        {
            ups++;
            yield return Kit.Frames(3);
            g = ReadGlass();
        }
        result.Ok = g.Fresh && ups > 0 && g.Zoom > low + 0.05f && g.Zoom >= want - 0.05f;
        result.Detail = $"zoom x{g.Zoom:0.##} after {downs} wheel notch(es) down (x{low:0.##}) and {ups} up (wanted x{want:0.#})";
    }

    // Left button up (lowers in hold-to-look mode), then a real right click (Block lowers it in both modes).
    private static IEnumerator LowerReal()
    {
        SelfTest.Key(KeyCode.Mouse0, false);
        yield return null;
        yield return SelfTest.KeyTap(KeyCode.Mouse1);
    }

    private sealed class Seen
    {
        internal int Leaves;
        internal int PastReach;
        internal int ObjectTilesBeyond;
        internal int BuildingTilesBeyond;
        internal int Frames;
        internal int Lost;      // frames without a fresh "at the eye" message
        internal float Seconds;
        internal float WorstFrame;
    }

    // Real spyglass kept at the eye for some seconds; Seen keeps the most of each count. forward = direction counted.
    private static IEnumerator Look(float seconds, Vector2 forward, SpyRun run, Seen seen)
    {
        var mgr = LodTerrainManager.Instance;
        var om = DistantObjectManager.Instance;
        var camera = Utils.GetMainCamera();
        var start = Kit.Now;
        var frames = 0;
        while (Kit.Now - start < seconds)
        {
            var g = ReadGlass();
            if (!g.Fresh || g.Weight < 0.99f)
            {
                seen.Lost++;
            }
            var cam = camera.transform.position;
            if (mgr != null)
            {
                seen.Leaves = Mathf.Max(seen.Leaves, mgr.CountLeavesInView(cam, forward, 8f, 600f));
                mgr.TestBoostLeft(out var past, out _, out _);
                seen.PastReach = Mathf.Max(seen.PastReach, past);
            }
            if (om != null)
            {
                om.TestTilesBeyond(cam, run.ObjectsBeyond, out var tiles, out _);
                om.TestTilesBeyond(cam, run.PiecesBeyond, out _, out var pieces);
                seen.ObjectTilesBeyond = Mathf.Max(seen.ObjectTilesBeyond, tiles);
                seen.BuildingTilesBeyond = Mathf.Max(seen.BuildingTilesBeyond, pieces);
            }
            if (++frames > 2)
            {
                seen.WorstFrame = Mathf.Max(seen.WorstFrame, Time.unscaledDeltaTime);
            }
            seen.Frames++;
            yield return null;
        }
        seen.Seconds += Kit.Now - start;
    }

    // Spyglass down again? (message says weight 0, or it is old)
    private static bool GlassDown()
    {
        var g = ReadGlass();
        return !g.Fresh || g.Weight <= 0.01f;
    }

    private static void EndSpyglass(SpyRun run)
    {
        Kit.Safe("input", () => SelfTest.ReleaseInput());
        if (run.Rig != null)
        {
            run.Rig.Restore();
        }
        Kit.PutBack();
    }

    // ---------- horizons.input.spyglass (C07)

    private static IEnumerator RunSpyglass()
    {
        const string N = SpyglassName;
        var c = new Kit.Checks(N);
        var run = new SpyRun();
        try
        {
            var ok = new Kit.Box();
            yield return PrepareSpyglass(c, run, ok);
            if (!ok.Ok)
            {
                c.Report("");
                yield break;
            }
            var cfg = Plugin.Cfg;
            var mgr = LodTerrainManager.Instance;
            var om = DistantObjectManager.Instance;
            var camera = Utils.GetMainCamera();
            var errors = ErrorWatch.Errors;
            var forward = run.Dir;

            // Spyglass in the hand, not raised.
            var cam = camera.transform.position;
            var leaves = mgr.CountLeavesInView(cam, forward, 8f, 600f);
            var finest = ObjectTests.FinestInCone(mgr, cam, forward, 8f, 2000f, 5000f);
            om.TestTilesBeyond(cam, run.ObjectsBeyond, out var objects0, out _);
            om.TestTilesBeyond(cam, run.PiecesBeyond, out _, out var buildings0);
            mgr.TestBoostLeft(out var past0, out _, out var pastFirst0);
            var calmStart = Kit.Now;
            var calmFrames = 0;
            while (Kit.Now - calmStart < 1f)
            {
                calmFrames++;
                yield return null;
            }
            var calmRate = calmFrames / Mathf.Max(0.1f, Kit.Now - calmStart);
            c.Note($"spyglass in the hand, down: {leaves} far leaf tiles within 8 degrees of the view, finest built tile 2 to 5 km out: level {finest}; "
                   + $"object tiles beyond {run.ObjectsBeyond:0} m: {objects0}, with buildings beyond {run.PiecesBeyond:0} m: {buildings0}; {calmRate:0} frames per second");
            c.Check(GlassDown(), "spyglass in the hand, down: the Spyglass mod sends no 'at the eye' message");
            c.Check(objects0 == 0 && buildings0 == 0, "spyglass down: no object tile beyond ObjectFarDistance and no buildings beyond PieceDistance");
            c.Check(past0 == 0, $"spyglass down: no wanted far tile is split farther out than SplitFactor x (1 + SplitHysteresis) tile widths ({past0} nodes, first {pastFirst0})");

            // Real click and real wheel.
            var raised = new Kit.Box();
            yield return RaiseReal(raised);
            if (!c.Check(raised.Ok, "a real left click with the spyglass in the hand brings it to the eye and the Spyglass mod's message arrives: " + raised.Detail))
            {
                c.Report("");
                yield break;
            }
            var zoomed = new Kit.Box();
            yield return ZoomTo(run.Want, zoomed);
            c.Check(zoomed.Ok, $"the real mouse wheel zooms the spyglass to x{run.Want:0.#}: {zoomed.Detail}");
            var glass = ReadGlass();
            var off = glass.Forward == Vector2.zero ? 180f : Vector2.Angle(glass.Forward, forward);
            c.Check(glass.Fresh && off <= 6f, $"the spyglass looks where the player looked ({off:0.0} degrees off)");
            var cap = cfg.V(cfg.SpyglassMaxBoost);
            var read = ViewBoost.Read(cfg.V(cfg.SpyglassDetail), cap);
            c.Check(read.Active && Kit.Near(read.Boost, Mathf.Min(glass.Zoom, cap), 0.02f),
                $"Distant Horizons reads the real spyglass: boost x{read.Boost:0.##} (zoom x{glass.Zoom:0.##}, SpyglassMaxBoost {cap:0.#})");

            var up = new Seen();
            yield return Look(8f, forward, run, up);
            cam = camera.transform.position;
            var finestUp = ObjectTests.FinestInCone(mgr, cam, forward, 8f, 2000f, 5000f);
            var upRate = up.Frames / Mathf.Max(0.1f, up.Seconds);
            c.Note($"real spyglass up for 8 s: {up.Leaves} leaf tiles in view, finest built tile 2 to 5 km out: level {finestUp}, {up.ObjectTilesBeyond} object tiles beyond the normal distance, "
                   + $"{up.BuildingTilesBeyond} with buildings beyond PieceDistance, {upRate:0} frames per second ({calmRate:0} before), longest frame {up.WorstFrame * 1000f:0} ms");
            c.Check(up.Lost == 0, $"the spyglass stayed at the eye for the whole 8 s ({up.Lost} of {up.Frames} frames without its message: lowered by a hit or a menu?)");
            c.Check(up.Leaves >= leaves + 2, $"more far tiles where the real spyglass looks ({leaves} -> {up.Leaves})");
            c.Check(up.PastReach > 0, $"wanted far tiles are split farther out than without a spyglass ({up.PastReach} nodes past the normal reach)");
            c.Check(finest < 99 && finestUp < finest, $"the land 2 to 5 km out is built finer within 8 s (level {finest} -> {finestUp})");
            c.Check(up.ObjectTilesBeyond > 0, $"far objects are drawn farther out that way ({up.ObjectTilesBeyond} object tiles beyond {run.ObjectsBeyond:0} m)");
            c.Check(up.BuildingTilesBeyond > 0, $"buildings are drawn farther out that way ({up.BuildingTilesBeyond} tiles beyond {run.PiecesBeyond:0} m)");
            c.Check(up.WorstFrame < 3f, $"no long freeze with the spyglass up (longest frame {up.WorstFrame:0.00} s)");

            // Real right click: down.
            var lowerAt = Kit.Now;
            yield return LowerReal();
            var down = new Kit.Box();
            yield return Kit.WaitUntil(GlassDown, 2f, down);
            c.Check(down.Ok, $"a real right click lowers the spyglass: its message says down within 2 s ({down.Detail})");
            cam = camera.transform.position;
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
            var backSeconds = Kit.Now - lowerAt;
            c.Check(back.Ok && backSeconds <= 3f,
                $"spyglass lowered: {backSeconds:0.0} s after the right click (limit 3 s, and 2 s after its message said down) no wanted far tile is split farther out than without a spyglass ({past} nodes left, first {pastFirst}; {up.PastReach} with it up)");
            var lowered = mgr.CountLeavesInView(cam, forward, 8f, 600f);
            c.Check(lowered < up.Leaves, $"spyglass lowered: fewer far tiles wanted in view than with it up ({up.Leaves} -> {lowered}; {leaves} before)");
            var objectsBack = new Kit.Box();
            var objectsLeft = 0;
            var buildingsLeft = 0;
            yield return Kit.WaitUntil(() =>
            {
                var now = camera.transform.position;
                om.TestTilesBeyond(now, run.ObjectsBeyond, out objectsLeft, out _);
                om.TestTilesBeyond(now, run.PiecesBeyond, out _, out buildingsLeft);
                return objectsLeft == 0 && buildingsLeft == 0;
            }, 2f, objectsBack);
            c.Check(objectsBack.Ok, $"spyglass lowered: within 2 s no far object tile is wanted beyond ObjectFarDistance and no buildings beyond PieceDistance ({objectsLeft} tiles, {buildingsLeft} with buildings)");
            var drawn = new Kit.Box();
            var finer = 0;
            yield return Kit.WaitUntil(() =>
            {
                mgr.TestBoostLeft(out _, out finer, out _);
                return finer == 0;
            }, 3f, drawn);
            c.Check(drawn.Ok, $"spyglass lowered: 3 s later at most, no far tile finer than wanted is still drawn ({finer} such tiles, waited {drawn.Detail})");
            yield return new WaitForSecondsRealtime(1f);
            cam = camera.transform.position;
            Kit.Coverage(mgr, cam.x, cam.z, 6000f, 128f, out points, out holes, out _, out firstHole);
            mgr.TestSurfaceCut(out var overlaps, out var notShown);
            c.Check(holes == 0 && overlaps == 0 && notShown == 0, $"…and a second later still no hole and nothing drawn twice ({holes} holes, {overlaps} overlaps, {notShown} not shown)");
            var finestLowered = ObjectTests.FinestInCone(mgr, cam, forward, 8f, 2000f, 5000f);
            var finestNormal = ObjectTests.FinestNormal(cfg, 2000f);
            c.Check(finestLowered >= finestNormal,
                $"…and the ground drawn 2 to 5 km out is no finer than the no-spyglass rule allows (level {finestLowered}, the rule allows level {finestNormal} or coarser; level {finestUp} with the spyglass up)");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report($"real spyglass, real click and wheel: {leaves} -> {up.Leaves} far tiles in view at x{glass.Zoom:0.#}, {lowered} when lowered");
        }
        finally
        {
            EndSpyglass(run);
        }
    }

    // ---------- horizons.input.spyglass-turn (C07)

    private static IEnumerator RunSpyglassTurn()
    {
        const string N = SpyglassTurnName;
        var c = new Kit.Checks(N);
        var run = new SpyRun();
        try
        {
            var ok = new Kit.Box();
            yield return PrepareSpyglass(c, run, ok);
            if (!ok.Ok)
            {
                c.Report("");
                yield break;
            }
            var mgr = LodTerrainManager.Instance;
            var camera = Utils.GetMainCamera();
            var errors = ErrorWatch.Errors;
            var raised = new Kit.Box();
            yield return RaiseReal(raised);
            if (!c.Check(raised.Ok, "a real left click brings the spyglass to the eye: " + raised.Detail))
            {
                c.Report("");
                yield break;
            }
            var zoomed = new Kit.Box();
            yield return ZoomTo(run.Want, zoomed);
            c.Note(zoomed.Detail);
            var first = new Seen();
            yield return Look(3f, run.Dir, run, first);
            var from = ReadGlass().Forward;
            if (!c.Check(from != Vector2.zero && first.Lost == 0, $"the spyglass stayed at the eye before the turn ({first.Lost} of {first.Frames} frames without its message)"))
            {
                c.Report("");
                yield break;
            }

            // Real mouse movement to the right until the view turned 60 degrees. Raised spyglass turn the view zoom
            // times slower (its look scale) and player's own mouse sensitivity count too: 25 pixels a frame = 0.16
            // degrees at x8, so 60 degrees need 6.4 s at 60 frames a second and never fit the 8 s on a slower game.
            // So me move more each frame until one frame turn about one degree (answer of a move show one frame late).
            var turnStart = Kit.Now;
            var turned = 0f;
            var sent = true;
            var pixels = 25f;
            while (Kit.Now - turnStart < 8f && turned < 60f && sent)
            {
                sent = SelfTest.MouseMove(new Vector2(pixels, 0f));
                yield return null;
                var g = ReadGlass();
                if (g.Fresh && g.Forward != Vector2.zero)
                {
                    var now = Vector2.Angle(from, g.Forward);
                    if (now - turned < 1f && pixels < 300f)
                    {
                        pixels *= 1.5f;
                    }
                    turned = now;
                }
            }
            yield return Kit.Frames(3);
            var after = ReadGlass();
            if (!c.Check(sent && turned >= 60f && after.Fresh && after.Weight >= 0.99f,
                    $"real mouse movement turns the view of the raised spyglass ({turned:0} degrees in {Kit.Now - turnStart:0.0} s, wanted 60; spyglass still up: {after.Fresh && after.Weight >= 0.99f})"))
            {
                c.Report("");
                yield break;
            }
            var to = after.Forward;
            var second = new Seen();
            yield return Look(4f, to, run, second);
            c.Check(second.Lost == 0, $"the spyglass stayed at the eye after the turn ({second.Lost} of {second.Frames} frames without its message)");
            c.Check(second.PastReach > 0, $"turned {turned:0} degrees: wanted far tiles are split farther out than without a spyglass ({second.PastReach} nodes past the normal reach)");

            yield return LowerReal();
            var back = new Kit.Box();
            var past = 0;
            yield return Kit.WaitUntil(() =>
            {
                mgr.TestBoostLeft(out past, out _, out _);
                return past == 0 && GlassDown();
            }, 3f, back);
            c.Check(back.Ok, $"lowered with a real right click: within 3 s nothing of the boost is wanted ({past} nodes left)");
            var cam = camera.transform.position;
            var lowered = mgr.CountLeavesInView(cam, to, 8f, 600f);
            c.Check(second.Leaves >= lowered + 2,
                $"turned {turned:0} degrees: the new direction got more far tiles while the spyglass looked there than it keeps once lowered ({second.Leaves} with it up, {lowered} lowered)");
            Kit.Coverage(mgr, cam.x, cam.z, 6000f, 128f, out var points, out var holes, out _, out var firstHole);
            c.Check(holes == 0, $"no hole in the far ground after turning and lowering ({holes} of {points} points, first {firstHole})");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report($"real spyglass turned {turned:0} degrees with the mouse: {second.Leaves} far tiles in the new view, {lowered} once lowered");
        }
        finally
        {
            EndSpyglass(run);
        }
    }

    // ---------- horizons.input.spyglass-settings (C08)

    private static IEnumerator RunSpyglassSettings()
    {
        const string N = SpyglassSettingsName;
        var c = new Kit.Checks(N);
        var run = new SpyRun();
        try
        {
            var ok = new Kit.Box();
            yield return PrepareSpyglass(c, run, ok);
            if (!ok.Ok)
            {
                c.Report("");
                yield break;
            }
            var cfg = Plugin.Cfg;
            var mgr = LodTerrainManager.Instance;
            var camera = Utils.GetMainCamera();
            var errors = ErrorWatch.Errors;
            var forward = run.Dir;
            var leaves = mgr.CountLeavesInView(camera.transform.position, forward, 8f, 600f);

            // SpyglassDetail off before the spyglass comes up.
            cfg.SetForTest(cfg.SpyglassDetail, false);
            var raised = new Kit.Box();
            yield return RaiseReal(raised);
            if (!c.Check(raised.Ok, "a real left click brings the spyglass to the eye: " + raised.Detail))
            {
                c.Report("");
                yield break;
            }
            var zoomed = new Kit.Box();
            yield return ZoomTo(run.Want, zoomed);
            var zoom = ReadGlass().Zoom;
            c.Check(zoomed.Ok, $"the real mouse wheel zooms the spyglass to x{run.Want:0.#}: {zoomed.Detail}");

            var off = new Seen();
            yield return Look(2.5f, forward, run, off);
            c.Check(off.Lost == 0, $"the spyglass was at the eye all that time ({off.Lost} of {off.Frames} frames without its message)");
            c.Check(!ViewBoost.Read(cfg.V(cfg.SpyglassDetail), cfg.V(cfg.SpyglassMaxBoost)).Active, "SpyglassDetail = false: the real spyglass's message gives no boost");
            c.Check(off.PastReach == 0 && off.ObjectTilesBeyond == 0 && off.BuildingTilesBeyond == 0,
                $"SpyglassDetail = false: a raised x{zoom:0.#} spyglass changes no detail ({off.PastReach} wanted nodes split past the normal reach, {off.ObjectTilesBeyond} object tiles and {off.BuildingTilesBeyond} with buildings farther out; tiles in view {leaves} -> {off.Leaves})");

            cfg.SetForTest(cfg.SpyglassDetail, true);
            cfg.SetForTest(cfg.SpyglassMaxBoost, 1f);
            var one = new Seen();
            yield return Look(2.5f, forward, run, one);
            c.Check(!ViewBoost.Read(cfg.V(cfg.SpyglassDetail), cfg.V(cfg.SpyglassMaxBoost)).Active, "SpyglassMaxBoost = 1: the real spyglass's message gives no boost");
            c.Check(one.Lost == 0 && one.PastReach == 0 && one.ObjectTilesBeyond == 0 && one.BuildingTilesBeyond == 0,
                $"SpyglassMaxBoost = 1: a raised x{zoom:0.#} spyglass changes no detail ({one.PastReach} wanted nodes split past the normal reach, {one.ObjectTilesBeyond} object tiles and {one.BuildingTilesBeyond} with buildings farther out; tiles in view {leaves} -> {one.Leaves}; {one.Lost} frames without the message)");

            cfg.SetForTest(cfg.SpyglassMaxBoost, 4f);
            var four = new Seen();
            yield return Look(6f, forward, run, four);
            c.Check(four.Lost == 0 && four.Leaves >= leaves + 2 && four.PastReach > 0,
                $"SpyglassMaxBoost = 4: more detail through the real spyglass ({leaves} -> {four.Leaves} far tiles in view, {four.PastReach} nodes past the normal reach; {four.Lost} frames without the message)");

            // 8 against 4 only says something when the spyglass really zooms past 4.
            if (c.Check(zoom > 4.5f, $"the spyglass zooms past x4 (x{zoom:0.##}): without that SpyglassMaxBoost = 8 cannot show more than 4"))
            {
                cfg.SetForTest(cfg.SpyglassMaxBoost, 8f);
                var eight = new Seen();
                yield return Look(4f, forward, run, eight);
                c.Check(eight.Lost == 0 && eight.Leaves > four.Leaves,
                    $"SpyglassMaxBoost = 8: more detail through a x{zoom:0.#} spyglass than with 4 ({four.Leaves} -> {eight.Leaves} far tiles in view; {eight.Lost} frames without the message)");
            }

            cfg.SetForTest(cfg.SpyglassMaxBoost, (float)cfg.SpyglassMaxBoost.DefaultValue);
            yield return LowerReal();
            var back = new Kit.Box();
            var past = 0;
            yield return Kit.WaitUntil(() =>
            {
                mgr.TestBoostLeft(out past, out _, out _);
                return past == 0 && GlassDown();
            }, 3f, back);
            c.Check(back.Ok, $"lowered with a real right click, settings back: within 3 s nothing of the boost is wanted ({past} nodes left)");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report($"real spyglass at x{zoom:0.#}: no change with SpyglassDetail off or SpyglassMaxBoost 1, {leaves} -> {four.Leaves} far tiles in view with 4");
        }
        finally
        {
            EndSpyglass(run);
        }
    }

    // ---------- horizons.input.swim-dive (C01)

    // Sea deep enough to dive in, nearest first. at = on the water surface.
    private static bool FindSea(Vector3 from, float minDepth, out Vector3 at, out float depth)
    {
        var wg = WorldGenerator.instance;
        var water = ZoneSystem.instance.m_waterLevel;
        for (var r = 150f; r <= 3000f; r += 50f)
        {
            var steps = Mathf.Max(16, Mathf.RoundToInt(2f * Mathf.PI * r / 60f));
            for (var i = 0; i < steps; i++)
            {
                var a = i * 2f * Mathf.PI / steps;
                var p = from + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                var h = wg.GetHeight(p.x, p.z);
                if (h > water - minDepth)
                {
                    continue;
                }
                var deepAround = true;
                for (var k = 0; k < 4 && deepAround; k++)
                {
                    var b = k * Mathf.PI / 2f;
                    deepAround = wg.GetHeight(p.x + Mathf.Cos(b) * 24f, p.z + Mathf.Sin(b) * 24f) < water - minDepth * 0.75f;
                }
                if (!deepAround)
                {
                    continue;
                }
                at = new Vector3(p.x, water, p.z);
                depth = water - h;
                return true;
            }
        }
        at = from;
        depth = 0f;
        return false;
    }

    // How far the camera is under the water surface at its own spot (the rule Swim Dive's view uses); below 0 = above
    // the water or no water there.
    private static float CameraDepth(Camera cam)
    {
        var p = cam.transform.position;
        var surface = Floating.GetLiquidLevel(p, 1f, LiquidType.Water);
        return surface < -9000f ? -1f : surface - p.y;
    }

    // Triangles of the far-sea sheet that face up. Every one looked at (about 12 000, once): a step of 48 indices
    // only ever met the first triangle of a quad, and a wrong second triangle is the classic mistake.
    private static void SheetFacing(Mesh mesh, out int up, out int total)
    {
        up = 0;
        total = 0;
        var v = mesh.vertices;
        for (var s = 0; s < mesh.subMeshCount; s++)
        {
            var t = mesh.GetTriangles(s);
            for (var i = 0; i + 2 < t.Length; i += 3)
            {
                total++;
                if (Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).y > 0f)
                {
                    up++;
                }
            }
        }
    }

    private static IEnumerator RunDive()
    {
        const string N = DiveName;
        var c = new Kit.Checks(N);
        var weather = new Kit.Weather();
        Kit.Flight flight = null;
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
            if (!ModActive(DiveGuid, out var state))
            {
                c.Check(false, $"Swim Dive is not loaded and active ({state}): nothing was checked");
                c.Report("");
                yield break;
            }
            var crouchOk = Bound("Crouch", KeyCode.LeftControl, out var crouchPath);
            var jumpOk = Bound("Jump", KeyCode.Space, out var jumpPath);
            if (!crouchOk || !jumpOk)
            {
                c.Check(false, $"Crouch is bound to '{crouchPath}' and Jump to '{jumpPath}': the test can only press Left Ctrl and Space");
                c.Report("");
                yield break;
            }
            // Swim Dive's own view settings (each player's choice): without them there is no under-water view to check.
            var haveCamera = OtherSetting<bool>(DiveGuid, "Visuals", "UnderwaterCamera", out var underCamera);
            var haveFog = OtherSetting<bool>(DiveGuid, "Visuals", "UnderwaterFog", out var underFog);
            var haveVisibility = OtherSetting<float>(DiveGuid, "Visuals", "UnderwaterVisibility", out var visibility);
            if (!c.Check(haveCamera && haveFog && haveVisibility && underCamera && underFog,
                    $"Swim Dive's UnderwaterCamera and UnderwaterFog are on in its settings (camera {(haveCamera ? underCamera.ToString() : "not found")}, fog {(haveFog ? underFog.ToString() : "not found")}, visibility {(haveVisibility ? visibility.ToString("0.#") : "not found")})"))
            {
                c.Report("");
                yield break;
            }
            var player = Player.m_localPlayer;
            var camera = Utils.GetMainCamera();
            var cfg = Plugin.Cfg;
            if (!c.Check(FindSea(player.transform.position, 8f, out var sea, out var seaDepth), "no sea 8 m deep within 3 km of the start place: nothing was checked"))
            {
                c.Report("");
                yield break;
            }
            c.Note($"sea {seaDepth:0} m deep at ({sea.x:0},{sea.z:0}), {Utils.DistanceXZ(sea, player.transform.position):0} m from the start place");
            Kit.ForceFogDefaults();
            Kit.Force(cfg.FarWater, true);
            weather.Force("Clear", 0.5f);

            // Over the sea, wait for the water there, then let go: the player falls in and swims.
            flight = new Kit.Flight(player);
            flight.Put(sea + Vector3.up * 3f);
            var box = new Kit.Box();
            yield return Kit.WaitUntil(() => ZoneSystem.instance.IsActiveAreaLoaded() && ZNetScene.instance.IsAreaReady(sea), 25f, box);
            if (!c.Check(box.Ok, $"the sea around ({sea.x:0},{sea.z:0}) loaded within 25 s ({box.Detail})"))
            {
                c.Report("");
                yield break;
            }
            yield return Kit.WaitWeather("Clear", box);
            c.Check(box.Ok, "clear weather at noon: " + (box.Ok ? "shown" : box.Detail));
            var clear = EnvMan.instance.GetEnv("Clear");
            var raw = clear.m_fogDensityDay;
            // What my fog rule leaves of it on screen (README: clear weather x FogDensityMultiplier).
            var thin = Kit.ExpectedFog(clear, raw);
            var mgr = LodTerrainManager.Instance;
            yield return Kit.WaitUntil(() => mgr != null && mgr.SeaPainting && mgr.TestSeaDraws == mgr.Water.BandCount, 10f, box);
            if (!c.Check(box.Ok, "before the dive: the far sea is painted"))
            {
                c.Report("");
                yield break;
            }
            player.m_debugFly = false;
            yield return Kit.WaitUntil(() => player.IsSwimming(), 6f, box);
            if (!c.Check(box.Ok, $"the player swims in the sea ({box.Detail})"))
            {
                c.Report("");
                yield break;
            }
            yield return Kit.WaitFixed(5);
            var eof = new WaitForEndOfFrame();
            yield return eof;
            c.Check(CameraDepth(camera) <= 0f && Kit.Near(RenderSettings.fogDensity, thin) && thin < raw * 0.9f,
                $"swimming at the surface: the camera is above the water and the clear-weather fog is thinned ({Kit.F(RenderSettings.fogDensity)}, weather {Kit.F(raw)})");
            var errors = ErrorWatch.Errors;

            // Real Crouch key held: Swim Dive takes the player down and lets the camera follow.
            if (!c.Check(SelfTest.Key(KeyCode.LeftControl, true), "no input driver: Left Ctrl could not be pressed (not a probe run?)"))
            {
                c.Report("");
                yield break;
            }
            yield return Kit.WaitUntil(() => CameraDepth(camera) > 0.3f, 8f, box);
            if (!c.Check(box.Ok, $"holding the real Crouch key while swimming takes the camera under water within 8 s (camera {CameraDepth(camera):0.00} m under, feet {player.GetLiquidLevel() - player.transform.position.y:0.0} m under)"))
            {
                c.Report("");
                yield break;
            }
            yield return new WaitForSecondsRealtime(1f);

            // Under water: what is on screen at the end of each frame.
            var expected = (RenderSettings.fogMode == FogMode.ExponentialSquared ? 1.978f : 3.912f) / Mathf.Clamp(visibility, 5f, 100f);
            var frames = 0;
            var wrongFog = 0;
            var above = 0;
            var worstFog = expected;
            var deepest = 0f;
            for (var i = 0; i < 30; i++)
            {
                yield return eof;
                frames++;
                var depth = CameraDepth(camera);
                deepest = Mathf.Max(deepest, depth);
                if (depth <= 0.02f)
                {
                    above++;
                    continue;
                }
                var d = RenderSettings.fogDensity;
                if (!Kit.Near(d, expected, 0.02f))
                {
                    wrongFog++;
                    worstFog = d;
                }
            }
            c.Check(above == 0, $"the camera stayed under water for {frames} frames ({above} above; deepest {deepest:0.0} m)");
            c.Check(wrongFog == 0,
                $"under water the fog on screen is Swim Dive's own in every frame: density {Kit.F(expected)} for its {Mathf.Clamp(visibility, 5f, 100f):0.#} m visibility, not the thinned clear-weather fog {Kit.F(thin)} ({wrongFog} of {frames - above} frames differ, e.g. {Kit.F(worstFog)})");
            var cam = camera.transform.position;
            Kit.Coverage(mgr, cam.x, cam.z, 2560f, 128f, out var points, out var holes, out _, out var firstHole);
            mgr.TestSurfaceCut(out var overlaps, out var notShown);
            c.Check(holes == 0 && overlaps == 0 && notShown == 0, $"under water: the far ground is whole and single ({holes} of {points} points without, {overlaps} overlaps, {notShown} not shown, first {firstHole})");
            var sheet = mgr.Water;
            c.Note($"under water: far sea painting {mgr.SeaPainting}, {mgr.TestSeaDraws} bands drawn last frame, sheet from {sheet.Inner:0} m out at {sheet.Center.y:0.##} m");
            if (c.Check(sheet.Mesh != null, "the far-sea sheet exists"))
            {
                SheetFacing(sheet.Mesh, out var facingUp, out var triangles);
                c.Check(triangles > 0 && facingUp == triangles, $"every triangle of the far-sea sheet faces up, none faces a camera below it ({facingUp} of {triangles})");
            }
            yield return Kit.Shot(N, "under-water");

            // Real Jump key: back up.
            SelfTest.Key(KeyCode.LeftControl, false);
            SelfTest.Key(KeyCode.Space, true);
            // Swim Dive keeps the camera under the surface as long as the diver's eye is under it: camera above = diver up.
            yield return Kit.WaitUntil(() => CameraDepth(camera) <= 0f, 10f, box);
            SelfTest.Key(KeyCode.Space, false);
            if (!c.Check(box.Ok, $"holding the real Jump key brings player and camera back to the surface within 10 s (camera {CameraDepth(camera):0.00} m under)"))
            {
                c.Report("");
                yield break;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Kit.WaitFixed(3);
            yield return eof;
            c.Check(CameraDepth(camera) <= 0f && Kit.Near(RenderSettings.fogDensity, thin) && thin < raw * 0.9f,
                $"back at the surface: the fog on screen is the thinned clear-weather fog again ({Kit.F(RenderSettings.fogDensity)}, weather {Kit.F(raw)}, under water it was {Kit.F(expected)})");
            yield return Kit.WaitUntil(() => mgr.SeaPainting && mgr.TestSeaDraws == mgr.Water.BandCount, 5f, box);
            Kit.LodPlanes(out var planes, out var hidden, out _, out _);
            c.Check(box.Ok && Kit.Buffers(camera, Kit.SeaBuffer) == 1, $"back at the surface: the far sea is painted, all {mgr.Water.BandCount} bands, one buffer on the camera ({mgr.TestSeaDraws} bands, {Kit.Buffers(camera, Kit.SeaBuffer)} buffer(s))");
            c.Check(planes >= 1 && hidden == planes, $"…and the game's own distant water plane is out of reach ({hidden} of {planes})");
            c.Check(Mathf.Abs(mgr.Water.Center.y - ZoneSystem.instance.m_waterLevel) < 0.001f, $"…the sheet lies at the water level ({mgr.Water.Center.y:0.##} m)");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons during the dive ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            yield return Kit.Shot(N, "back-at-the-surface");

            // Home.
            var home = flight.Origin;
            flight.Put(home + Vector3.up * 2f);
            yield return Kit.WaitUntil(() => ZoneSystem.instance.IsActiveAreaLoaded() && ZNetScene.instance.IsAreaReady(home), 25f, box);
            if (c.Check(box.Ok, $"the start place loaded again within 25 s ({box.Detail})"))
            {
                flight.Land();
            }
            c.Report($"real dive with Swim Dive {deepest:0.0} m under: its fog on screen under water, far sea and thin fog back at the surface");
        }
        finally
        {
            Kit.Safe("input", () => SelfTest.ReleaseInput());
            if (flight != null)
            {
                flight.End();
            }
            weather.Restore();
            Kit.PutBack();
        }
    }

    // ---------- horizons.input.fly (T04)

    private static IEnumerator RunFly()
    {
        const string N = FlyName;
        const float LegSeconds = 20f;
        var c = new Kit.Checks(N);
        Kit.Flight flight = null;
        var player = Player.m_localPlayer;
        var look = player != null ? player.GetLookDir() : Vector3.forward;
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var forwardOk = Bound("Forward", KeyCode.W, out var forwardPath);
            var runOk = Bound("Run", KeyCode.LeftShift, out var runPath);
            if (!forwardOk || !runOk)
            {
                c.Check(false, $"Forward is bound to '{forwardPath}' and Run to '{runPath}': the test can only press W and Left Shift");
                c.Report("");
                yield break;
            }
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 20f);
            c.Note("before: " + settled.Detail);
            var mgr = LodTerrainManager.Instance;
            var cam = Utils.GetMainCamera();
            var radius = Plugin.Cfg.V(Plugin.Cfg.WorldRadius);
            flight = new Kit.Flight(player);
            var origin = flight.Origin;
            var dir = (origin + Vector3.right * 1500f).magnitude < radius - 600f ? Vector3.right : Vector3.left;
            var errorsBefore = ErrorWatch.Errors;

            // Up like the "fly" console command plus Space: 250 m over the start place (clear of every hill near it).
            flight.Put(origin + Vector3.up * 250f);
            player.SetLookDir(dir);
            yield return Kit.Frames(3);

            int checks = 0, holeChecks = 0, worstHoles = 0, notShownChecks = 0, overlapChecks = 0, groundListed = 0, frames = 0, coarsestUnder = 0;
            var firstHole = "";
            var worstFrame = 0f;
            var flown = 0f;
            var farthest = 0f;
            var flightStart = Kit.Now;
            var coarseSince = -1f;
            var longestCoarse = 0f;
            var keysOk = true;
            for (var leg = 0; leg < 2 && keysOk; leg++)
            {
                player.SetLookDir(leg == 0 ? dir : -dir);
                yield return Kit.Frames(2);
                keysOk = SelfTest.Key(KeyCode.W, true) && SelfTest.Key(KeyCode.LeftShift, true);
                var t0 = Kit.Now;
                var from = player.transform.position;
                var nextCheck = 0f;
                var nextGround = 0f;
                while (keysOk && Kit.Now - t0 < LegSeconds)
                {
                    yield return null;
                    var elapsed = Kit.Now - t0;
                    frames++;
                    if (frames > 2)
                    {
                        worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
                    }
                    // Second leg ends over the start place.
                    if (leg == 1 && Vector3.Dot(player.transform.position - origin, dir) <= 0f)
                    {
                        break;
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
                        var level = mgr.TrySampleSurface(cp.x, cp.z, out _, out var under) ? under.Level : 99;
                        if (level < 99 && level > coarsestUnder)
                        {
                            coarsestUnder = level;
                        }
                        // How long the ground right under the camera stays coarser than the second finest level.
                        if (level > 1)
                        {
                            if (coarseSince < 0f)
                            {
                                coarseSince = Kit.Now;
                            }
                            longestCoarse = Mathf.Max(longestCoarse, Kit.Now - coarseSince);
                        }
                        else
                        {
                            coarseSince = -1f;
                        }
                    }
                    if (elapsed >= nextGround)
                    {
                        nextGround = elapsed + 2f;
                        Kit.FarTilesAsGround(out _, out var listed, out var notDistant);
                        groundListed += listed + notDistant;
                    }
                }
                SelfTest.Key(KeyCode.W, false);
                SelfTest.Key(KeyCode.LeftShift, false);
                flown += Utils.DistanceXZ(from, player.transform.position);
                farthest = Mathf.Max(farthest, Utils.DistanceXZ(origin, player.transform.position));
                yield return Kit.Frames(3);
            }
            var flightSeconds = Kit.Now - flightStart;
            var speed = flown / Mathf.Max(0.1f, flightSeconds);
            c.Note($"flew {flown:0} m in {flightSeconds:0.0} s with the real Forward and Run keys ({speed:0} m/s, {frames} frames, {frames / Mathf.Max(0.1f, flightSeconds):0} per second, longest frame {worstFrame * 1000f:0} ms); "
                   + $"{checks} checks, coarsest far tile under the camera on the way: level {coarsestUnder}, longest time with ground coarser than level 1 under the camera: {longestCoarse:0.0} s");
            c.Check(keysOk, "the input driver took the key presses (W and Left Shift)");
            c.Check(flown >= 1200f && speed >= 30f,
                $"the real keys flew the player at the game's own running fly speed ({flown:0} m, {speed:0} m/s; the game flies {Character.m_debugFlySpeed * 2.5f:0} m/s with Run held, {Character.m_debugFlySpeed} without)");
            c.Check(checks >= 60, $"enough checks on the way ({checks})");
            c.Check(holeChecks == 0, $"far ground under every point within 2.5 km of the camera at every check ({holeChecks} of {checks} checks found holes, at most {worstHoles} points, first {firstHole})");
            c.Check(notShownChecks == 0, $"every surface tile was built and switched on at every check ({notShownChecks} checks found one that was not)");
            c.Check(overlapChecks == 0, $"no far ground drawn twice at any check ({overlapChecks} checks)");
            c.Check(groundListed == 0, $"no far tile was ever listed with the game's real ground heightmaps ({groundListed})");
            c.Check(worstFrame < 3f, $"no long freeze on the way (longest frame {worstFrame:0.00} s)");

            // Over the start place: zones load, then the finest tiles come.
            flight.Put(origin + Vector3.up * 2f);
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
            c.Report($"{flown:0} m flown with real keys at {speed:0} m/s, {farthest:0} m from the start at most, far ground whole at {checks} checks");
        }
        finally
        {
            Kit.Safe("input", () => SelfTest.ReleaseInput());
            if (flight != null)
            {
                flight.End();
            }
            Kit.Safe("look direction", () =>
            {
                if (player != null)
                {
                    player.SetLookDir(look);
                }
            });
            Kit.PutBack();
        }
    }
}
#endif
