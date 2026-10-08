using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using UnityEngine.Rendering;
#endif

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only (calls vanish in Release). In-world self tests, world probe run them (tools/Test-InWorld.ps1,
// -Mod View.DistantHorizons):
//   horizons.logic    other-mod classification, every setting carry a ConfigurationManager Order, no own Enabled
//   horizons.terrain  far tiles built and on screen, vanilla 3x3 gone, paint buffer on main camera, far clip up;
//                     screenshot; NOTE tile stats
//   horizons.objects  far object tiles built (NOTE when world have none near), NOTE object stats
//   horizons.boost    spyglass boost (ViewBoost): factor maths, slot reading (stale, weight, cap, setting off), recompute
//                     trigger; live: fake spyglass slot = more far tiles in the looked-at direction, back after; NOTE counts
//   horizons.detach   turn-off path (TerrainLink.Detach, same code OnDeactivated run): managers and their objects
//                     gone, vanilla 3x3 back, no buffer left on camera, far clip back, game's distant water plane
//                     back; then TerrainLink.AttachIfInWorld (turn-on-in-a-world path) and far tiles back
// More tests in own files, registered here: WorldTests (SelfTestsWorld.cs: what get drawn), LifeTests
// (SelfTestsLife.cs: off-on, settings, console), ObjectTests (SelfTestsObjects.cs: far objects, simulation distance,
// spyglass), EdgeTests (SelfTestsEdge.cs: one small test per edge case believed wrong today), MpTests (SelfTestsMp.cs:
// client on a dedicated server), NearGroundTests (horizons.near). Shared tools: SelfTestKit.cs. horizons.cleanlog go
// last: it look back at the whole session.
// Me never write config (framework rule): detach test call same code the toggle call, without the toggle; tests that
// need the framework's own off-on use Plugin.TestBlocker, settings are forced in memory (DHConfig.SetForTest).
internal static class SelfTests
{
    private const string LogicName = "horizons.logic";
    private const string TerrainName = "horizons.terrain";
    private const string ObjectsName = "horizons.objects";
    private const string DetachName = "horizons.detach";
    private const string BoostName = "horizons.boost";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(LogicName, RunLogic);
        SelfTest.Register(TerrainName, RunTerrain);
        SelfTest.Register(ObjectsName, RunObjects);
        SelfTest.Register(BoostName, RunBoost);
        SelfTest.Register(DetachName, RunDetach);
        NearGroundTests.Register();
        ErrorWatch.Install();
        WorldTests.Register();
        LifeTests.Register();
        ObjectTests.Register();
        EdgeTests.Register();
        MpTests.Register();
        SelfTest.Register(LifeTests.CleanLogName, LifeTests.RunCleanLog);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(LogicName);
        SelfTest.Unregister(TerrainName);
        SelfTest.Unregister(ObjectsName);
        SelfTest.Unregister(BoostName);
        ViewBoost.TestSlot = null;
        SelfTest.Unregister(DetachName);
        NearGroundTests.Unregister();
        // Forced settings and Plugin.TestBlocker stay: test that turn me off through the framework
        // still run and put them back itself (Kit.PutBack).
        WorldTests.Unregister();
        LifeTests.Unregister();
        ObjectTests.Unregister();
        EdgeTests.Unregister();
        MpTests.Unregister();
        SelfTest.Unregister(LifeTests.CleanLogName);
#endif
    }

#if DEBUG
    private const float BuildTimeout = 80f;
    private const int MinTiles = 4;
    private const float LodPlaneHidden = 1000000f;
    private const float VanillaMeshTimeout = 20f;

    private static readonly CameraEvent[] PaintEvents =
    {
        CameraEvent.AfterGBuffer, CameraEvent.BeforeGBuffer, CameraEvent.AfterForwardOpaque,
        CameraEvent.BeforeForwardOpaque, CameraEvent.BeforeForwardAlpha,
    };

    private static IEnumerator RunLogic()
    {
        var ok = true;
        ok &= Check(LogicName, ForeignMods.Classify("com.distanthorizons.valheim") == ForeignKind.Blocks,
            "the standalone Distant Horizons blocks");
        ok &= Check(LogicName, ForeignMods.Classify("MARC.DONEGALHORIZONLIFT") == ForeignKind.Blocks,
            "New Horizons: Treelines blocks (GUID case ignored)");
        ok &= Check(LogicName, ForeignMods.Classify("com.Skarif.ValheimPerformanceOverhaul_WATER") == ForeignKind.Composes,
            "Valheim Performance Overhaul composes");
        ok &= Check(LogicName, ForeignMods.Classify("vapok.mods.nofogbruh") == ForeignKind.None, "NoFogBruh is fine");
        ok &= Check(LogicName, ForeignMods.Classify(null) == ForeignKind.None, "no GUID = none");

        var count = 0;
        var noOrder = new List<string>();
        var ownEnabled = false;
        foreach (var kv in Plugin.Cfg.File)
        {
            if (kv.Key.Section == "General")
            {
                continue;
            }
            count++;
            if (string.Equals(kv.Key.Key, "Enabled", StringComparison.OrdinalIgnoreCase))
            {
                ownEnabled = true;
            }
            var tags = kv.Value.Description.Tags;
            var cma = tags != null && tags.Length > 0 ? tags[0] as ConfigurationManagerAttributes : null;
            if (cma == null || cma.Order == null)
            {
                noOrder.Add(kv.Key.Section + "/" + kv.Key.Key);
            }
            if (kv.Key.Section.Length > 0 && char.IsDigit(kv.Key.Section[0]))
            {
                ok &= Check(LogicName, false, $"section '{kv.Key.Section}' starts with a number");
            }
        }
        ok &= Check(LogicName, count >= 60, $"{count} settings of this mod outside General (expected 60 or more)");
        ok &= Check(LogicName, noOrder.Count == 0, "settings without a first ConfigurationManager tag with Order: "
                                                   + (noOrder.Count == 0 ? "none" : string.Join(", ", noOrder)));
        ok &= Check(LogicName, !ownEnabled, "no Enabled of its own next to the framework's General/Enabled");
        if (ok)
        {
            SelfTest.Pass(LogicName, $"classification and {count} settings checked");
        }
        yield break;
    }

    private static IEnumerator RunTerrain()
    {
        var wait = new Box();
        yield return WaitForTiles(wait);
        var mgr = LodTerrainManager.Instance;
        if (!Check(TerrainName, wait.Ok, wait.Detail))
        {
            yield break;
        }
        var ok = Check(TerrainName, !TerrainLink.VanillaActive, "vanilla 3x3 distant terrain removed");
        var cam = Utils.GetMainCamera();
        if (Plugin.Cfg.FarTerrainDraw.Value == "shrink")
        {
            ok &= Check(TerrainName, CountOurBuffers(cam, "DistantHorizons far terrain") == 1,
                "one far-terrain paint buffer on the main camera");
        }
        if (Plugin.Cfg.RaiseCameraFarClip.Value)
        {
            ok &= Check(TerrainName, cam.farClipPlane >= Plugin.Cfg.CameraFarClip.Value - 1f,
                $"far clip {cam.farClipPlane:0} m >= CameraFarClip {Plugin.Cfg.CameraFarClip.Value:0} m");
        }
        SelfTest.Note(TerrainName, mgr.GetStats());
        SelfTest.Screenshot(TerrainName, "far");
        yield return null;
        yield return null;
        if (ok)
        {
            SelfTest.Pass(TerrainName, $"{mgr.TotalBuilds} tiles built, vanilla grid gone, camera set up");
        }
    }

    private static IEnumerator RunObjects()
    {
        if (!Plugin.Cfg.ObjectsEnabled.Value)
        {
            SelfTest.Note(ObjectsName, "ObjectsEnabled is off in this config: nothing to check");
            SelfTest.Pass(ObjectsName, "skipped (far objects turned off)");
            yield break;
        }
        var start = Time.realtimeSinceStartup;
        DistantObjectManager om = null;
        while (Time.realtimeSinceStartup - start < BuildTimeout)
        {
            om = DistantObjectManager.Instance;
            if (om != null && om.TotalBuilds > 0)
            {
                break;
            }
            yield return new WaitForSeconds(0.5f);
        }
        if (!Check(ObjectsName, om != null, "the far-object manager is running"))
        {
            yield break;
        }
        SelfTest.Note(ObjectsName, om.GetStats());
        if (Check(ObjectsName, om.TotalBuilds > 0, $"far object tiles built within {BuildTimeout:0} s"))
        {
            SelfTest.Pass(ObjectsName, $"{om.TotalBuilds} object tile builds");
        }
    }

    private static double[] FakeSlot(float zoom, float weight, Vector3 forward, float halfAngle, int frame) =>
        new double[] { 1, frame, zoom, weight, forward.x, forward.y, forward.z, halfAngle, halfAngle * 0.6f };

    private static IEnumerator RunBoost()
    {
        var ok = true;
        // Pure: factor. View to +Z, 10 degrees wide, boost 4.
        var s = new ViewBoost.State { Active = true, Boost = 4f, Forward = new Vector2(0f, 1f), HalfAngle = 10f };
        var cam = Vector3.zero;
        ok &= Check(BoostName, Mathf.Approximately(s.Factor(cam, -64f, 1936f, 64f, 2064f), 4f), "tile straight ahead: full boost");
        ok &= Check(BoostName, Mathf.Approximately(s.Factor(cam, -64f, -2064f, 64f, -1936f), 1f), "tile behind: none");
        var side = s.Factor(cam, 664f, 1936f, 792f, 2064f); // about 19 degrees off: inside the fade
        ok &= Check(BoostName, side > 1f && side < 4f, $"tile at the edge of the view: partial ({side:0.##})");
        ok &= Check(BoostName, Mathf.Approximately(s.Factor(cam, -10f, -10f, 10f, 10f), 4f), "tile around the camera: full");
        ok &= Check(BoostName, Mathf.Approximately(default(ViewBoost.State).Factor(cam, 0f, 100f, 10f, 110f), 1f), "no spyglass: 1");

        // Pure: reading the slot (TestSlot replace the shared slot).
        var f = Time.frameCount;
        ViewBoost.TestSlot = FakeSlot(6f, 1f, Vector3.forward, 10f, f);
        var r = ViewBoost.Read(true, 4f);
        ok &= Check(BoostName, r.Active && Mathf.Approximately(r.Boost, 4f), $"zoom 6 capped to SpyglassMaxBoost 4 ({r.Boost:0.##})");
        yield return null;
        ViewBoost.TestSlot = FakeSlot(6f, 1f, Vector3.forward, 10f, Time.frameCount);
        ok &= Check(BoostName, !ViewBoost.Read(false, 4f).Active, "SpyglassDetail off: no boost");
        yield return null;
        ViewBoost.TestSlot = FakeSlot(3f, 0.5f, Vector3.forward, 10f, Time.frameCount);
        r = ViewBoost.Read(true, 4f);
        ok &= Check(BoostName, r.Active && Mathf.Approximately(r.Boost, 2f), $"half raised: half the zoom ({r.Boost:0.##})");
        yield return null;
        ViewBoost.TestSlot = FakeSlot(3f, 1f, Vector3.forward, 10f, Time.frameCount - 20);
        ok &= Check(BoostName, !ViewBoost.Read(true, 4f).Active, "old slot (20 frames): no boost");
        yield return null;
        ViewBoost.TestSlot = FakeSlot(3f, 1f, Vector3.up, 10f, Time.frameCount);
        ok &= Check(BoostName, !ViewBoost.Read(true, 4f).Active, "looking straight up: no boost");
        ViewBoost.TestSlot = null;

        // Pure: recompute trigger.
        var last = default(ViewBoost.Applied);
        ok &= Check(BoostName, ViewBoost.Poll(ref last, s, 10f), "boost on: recompute");
        ok &= Check(BoostName, !ViewBoost.Poll(ref last, s, 10.5f), "same view: no recompute");
        var turned = s;
        turned.Forward = new Vector2(Mathf.Sin(10f * Mathf.Deg2Rad), Mathf.Cos(10f * Mathf.Deg2Rad));
        ok &= Check(BoostName, !ViewBoost.Poll(ref last, turned, 10.1f), "turned, but too soon after the last");
        ok &= Check(BoostName, ViewBoost.Poll(ref last, turned, 10.5f), "turned 10 degrees: recompute");
        ok &= Check(BoostName, ViewBoost.Poll(ref last, default, 11.01f), "boost off: recompute at once");

        // Live: a fake spyglass looking along the camera = more far tiles that way.
        var wait = new Box();
        yield return WaitForTiles(wait);
        var mgr = LodTerrainManager.Instance;
        var camera = Utils.GetMainCamera();
        if (!wait.Ok || mgr == null || camera == null)
        {
            SelfTest.Note(BoostName, "no far terrain to test live: " + wait.Detail);
        }
        else
        {
            try
            {
                var fwd3 = camera.transform.forward;
                var fwd = new Vector2(fwd3.x, fwd3.z);
                if (fwd.sqrMagnitude < 0.01f)
                {
                    fwd = new Vector2(0f, 1f);
                }
                fwd.Normalize();
                var pos = camera.transform.position;
                var before = mgr.CountLeavesInView(pos, fwd, 8f, 600f);
                var until = Time.realtimeSinceStartup + 4f;
                var during = before;
                while (Time.realtimeSinceStartup < until)
                {
                    ViewBoost.TestSlot = FakeSlot(4f, 1f, new Vector3(fwd.x, 0f, fwd.y), 8f, Time.frameCount);
                    during = Mathf.Max(during, mgr.CountLeavesInView(pos, fwd, 8f, 600f));
                    yield return null;
                }
                ViewBoost.TestSlot = null;
                yield return new WaitForSeconds(1.5f);
                var after = mgr.CountLeavesInView(pos, fwd, 8f, 600f);
                SelfTest.Note(BoostName, $"far leaf tiles within 8 deg of the view: {before} before, {during} with boost x4, {after} after");
                ok &= Check(BoostName, during >= before + 2, $"boost gives more far tiles that way ({before} -> {during})");
                ok &= Check(BoostName, after < during, $"back to fewer after the spyglass is lowered ({during} -> {after})");
            }
            finally
            {
                ViewBoost.TestSlot = null;
            }
        }
        if (ok)
        {
            SelfTest.Pass(BoostName, "boost maths, slot reading and recompute trigger checked");
        }
    }

    private static IEnumerator RunDetach()
    {
        var wait = new Box();
        yield return WaitForTiles(wait);
        if (!Check(DetachName, wait.Ok, wait.Detail))
        {
            yield break;
        }
        var cam = Utils.GetMainCamera();
        var tl = TerrainLink.Current;
        var ok = true;
        try
        {
            TerrainLink.Detach();
        }
        catch (Exception e)
        {
            ok = Check(DetachName, false, "TerrainLink.Detach threw " + e);
        }
        try
        {
            yield return DetachChecks(cam, tl, ok);
        }
        finally
        {
            // Test stop early (timeout, error): never leave world without far terrain.
            if (LodTerrainManager.Instance == null && Plugin.Instance != null && Plugin.Instance.IsActive)
            {
                TerrainLink.AttachIfInWorld();
            }
        }
    }

    private static IEnumerator DetachChecks(Camera cam, TerrainLod tl, bool ok)
    {
        yield return null;
        yield return null;

        ok &= Check(DetachName, LodTerrainManager.Instance == null && DistantObjectManager.Instance == null,
            "both managers gone");
        ok &= Check(DetachName, tl != null && tl.GetComponent<LodTerrainManager>() == null
                                && tl.GetComponent<DistantObjectManager>() == null, "no manager left on TerrainLod");
        ok &= Check(DetachName, GameObject.Find("DistantHorizons_LOD") == null, "far tile root destroyed");
        ok &= Check(DetachName, GameObject.Find("DistantHorizons_Objects") == null, "far object root destroyed");
        ok &= Check(DetachName, GameObject.Find("DH_ImpostorBakeRig") == null, "impostor bake rig destroyed");
        ok &= Check(DetachName, TerrainLink.VanillaActive, "vanilla 3x3 distant terrain back");
        ok &= Check(DetachName, CountOurBuffers(cam, null) == 0, "no Distant Horizons buffer left on the main camera");
        if (Plugin.Cfg.RaiseCameraFarClip.Value)
        {
            ok &= Check(DetachName, cam.farClipPlane < Plugin.Cfg.CameraFarClip.Value,
                $"far clip back to the game's value ({cam.farClipPlane:0} m)");
        }
        var hidden = 0;
        var planes = 0;
        var block = new MaterialPropertyBlock();
        foreach (var w in Water.Instances)
        {
            var r = w != null ? w.GetComponent<MeshRenderer>() : null;
            var m = r != null ? r.sharedMaterial : null;
            if (m == null || !m.HasProperty("_IsLod") || m.GetFloat("_IsLod") < 0.5f)
            {
                continue;
            }
            planes++;
            r.GetPropertyBlock(block);
            if (block.GetFloat("_VisibleMaxDistance") >= LodPlaneHidden)
            {
                hidden++;
            }
        }
        ok &= Check(DetachName, hidden == 0, $"game's distant water plane visible again ({planes} plane(s), {hidden} still hidden)");

        // Vanilla grid must really get its meshes now, without the camera moving (its Update rebuild all nine once
        // the builder has them).
        var meshStart = Time.realtimeSinceStartup;
        var meshed = 0;
        while (Time.realtimeSinceStartup - meshStart < VanillaMeshTimeout)
        {
            meshed = CountVanillaMeshes(tl);
            if (tl != null && meshed == tl.m_heightmaps.Count && meshed > 0)
            {
                break;
            }
            yield return new WaitForSeconds(0.25f);
        }
        ok &= Check(DetachName, tl != null && meshed == tl.m_heightmaps.Count && meshed > 0,
            $"vanilla distant terrain has its meshes within {VanillaMeshTimeout:0} s ({meshed} of "
            + $"{(tl != null ? tl.m_heightmaps.Count : 0)} built)");
        SelfTest.Screenshot(DetachName, "vanilla");
        yield return null;
        yield return null;

        TerrainLink.AttachIfInWorld();
        var back = new Box();
        yield return WaitForTiles(back);
        ok &= Check(DetachName, back.Ok, "after attaching again: " + back.Detail);
        ok &= Check(DetachName, !TerrainLink.VanillaActive, "vanilla 3x3 removed again");
        if (ok)
        {
            SelfTest.Pass(DetachName, "turn-off path left the game vanilla, turn-on path in a world brought the far terrain back");
        }
    }

    // Finally-safe: test that stop early never leave world without my managers.
    private static IEnumerator WaitForTiles(Box result)
    {
        var start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < BuildTimeout)
        {
            var mgr = LodTerrainManager.Instance;
            if (mgr != null && mgr.TotalBuilds >= MinTiles && !TerrainLink.VanillaActive)
            {
                result.Ok = true;
                result.Detail = $"{mgr.TotalBuilds} far tiles built after {Time.realtimeSinceStartup - start:0.0} s";
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
        }
        var m = LodTerrainManager.Instance;
        result.Ok = false;
        result.Detail = m == null
            ? $"no terrain manager after {BuildTimeout:0} s (TerrainLod found: {TerrainLink.Current != null})"
            : $"only {m.TotalBuilds} far tiles after {BuildTimeout:0} s: {m.GetStats()}";
    }

    // Vanilla 3x3 tiles with a built mesh (no Regenerate yet = no mesh).
    private static int CountVanillaMeshes(TerrainLod tl)
    {
        if (tl == null)
        {
            return 0;
        }
        var n = 0;
        foreach (var h in tl.m_heightmaps)
        {
            var mf = h != null && h.m_heightmap != null ? h.m_heightmap.GetComponent<MeshFilter>() : null;
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0)
            {
                n++;
            }
        }
        return n;
    }

    private static int CountOurBuffers(Camera cam, string name)
    {
        if (cam == null)
        {
            return 0;
        }
        var n = 0;
        foreach (var evt in PaintEvents)
        {
            foreach (var cb in cam.GetCommandBuffers(evt))
            {
                if (cb != null && cb.name != null && cb.name.StartsWith("DistantHorizons", StringComparison.Ordinal)
                    && (name == null || cb.name == name))
                {
                    n++;
                }
            }
        }
        return n;
    }

    private static bool Check(string test, bool condition, string what)
    {
        if (!condition)
        {
            SelfTest.Fail(test, what);
        }
        return condition;
    }

    private sealed class Box
    {
        public bool Ok;
        public string Detail;
    }
#endif
}
