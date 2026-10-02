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
//   horizons.detach   turn-off path (TerrainLink.Detach, same code OnDeactivated run): managers and their objects
//                     gone, vanilla 3x3 back, no buffer left on camera, far clip back, game's distant water plane
//                     back; then TerrainLink.AttachIfInWorld (turn-on-in-a-world path) and far tiles back
// Me never write config (framework rule): detach test call same code the toggle call, without the toggle.
internal static class SelfTests
{
    private const string LogicName = "horizons.logic";
    private const string TerrainName = "horizons.terrain";
    private const string ObjectsName = "horizons.objects";
    private const string DetachName = "horizons.detach";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(LogicName, RunLogic);
        SelfTest.Register(TerrainName, RunTerrain);
        SelfTest.Register(ObjectsName, RunObjects);
        SelfTest.Register(DetachName, RunDetach);
        NearGroundTests.Register();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(LogicName);
        SelfTest.Unregister(TerrainName);
        SelfTest.Unregister(ObjectsName);
        SelfTest.Unregister(DetachName);
        NearGroundTests.Unregister();
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
