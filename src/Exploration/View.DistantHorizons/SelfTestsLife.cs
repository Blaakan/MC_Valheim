#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. In-world self tests about the mod's life and its settings (world probe, tools/Test-InWorld.ps1):
//   horizons.settings       section set, every section's settings in README order (ConfigurationManager Order),
//                           the four choice settings are value lists, Status is read-only plain text under Enabled
//   horizons.toggle         game paused, mod off through the framework (same path as the MC Mods panel): at once
//                           normal fog, no manager, no buffer, far plane back, water plane back, real zones plain,
//                           patches off, "dh" unknown; a frame later nothing of mine in the scene; vanilla 3x3 with
//                           meshes while still paused; TerrainLod off-on while off = vanilla (what a world load
//                           does); resume = fog stays normal; on = far terrain back, patches and "dh" back; two more
//                           quick off-on = one of everything, no error
//   horizons.relog          TerrainLod off then on with the mod on (the two hooks a logout and a world load fire):
//                           managers stop and leave nothing, then attach again, far terrain back, one of everything
//   horizons.blocked        the standalone Distant Horizons counted as loaded (ForeignMods.TestPlugins): exact Status
//                           sentence, state Conflict, one warning, nothing of mine drawn, vanilla 3x3; gone = active
//   horizons.console        dh, dh stats, dh envs, dh objects, dh get, dh get Enabled (unknown), dh set with the
//                           value already there (file not written), usage lines, dh objects rebuild, dh rebuild
//   horizons.live-terrain   the config file really tells me about a change; ViewDistance 5000 = no split beyond
//                           6000 m and no hole; BaseVertexSpacing changed three
//                           times = old terrain kept while changing, one rebuild half a second after the last, tiles
//                           with the new spacing; back
//   horizons.live-objects   DrawTrees off = trees stay 0.3 s then go without starting over; ObjectsEnabled off = all
//                           gone at once, on = back; ImpostorBakeBrightness dragged = cards re-baked once, half a
//                           second after the last change
//   horizons.world-radius   WorldRadius raised: sea edge follows at once, a tile beyond the old radius is wanted and
//                           built, no hole in the bigger disc; back: that tile is dropped
//   horizons.local-only     nothing of mine is a network object, no network hook, and a full off-on plus both
//                           rebuild commands register nothing and create no world object
//   horizons.cleanlog       no error line from or about this mod since game start, no guarded failure
// Mod off-on: Plugin.TestBlocker (in memory) + Kit.Refresh (the framework's own re-check, with the config file told
// not to save meanwhile), never the Enabled setting: no config file is written.
internal static class LifeTests
{
    private const string SettingsName = "horizons.settings";
    private const string ToggleName = "horizons.toggle";
    private const string RelogName = "horizons.relog";
    private const string BlockedName = "horizons.blocked";
    private const string ConsoleName = "horizons.console";
    private const string LiveTerrainName = "horizons.live-terrain";
    private const string LiveObjectsName = "horizons.live-objects";
    private const string WorldRadiusName = "horizons.world-radius";
    private const string LocalOnlyName = "horizons.local-only";
    internal const string CleanLogName = "horizons.cleanlog";

    private const string StandaloneGuid = "com.distanthorizons.valheim";
    private const string StandaloneStatus =
        "Inactive: The standalone Distant Horizons (BepInEx/plugins/DistantHorizons) also draws the distant terrain. Remove one of them.";
    private const string StandaloneWarning =
        "The standalone Distant Horizons (BepInEx/plugins/DistantHorizons) also draws the distant terrain, so Distant Horizons stays off. Remove one of them.";

    internal static void Register()
    {
        SelfTest.Register(SettingsName, RunSettings);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(RelogName, RunRelog);
        SelfTest.Register(BlockedName, RunBlocked);
        SelfTest.Register(ConsoleName, RunConsole);
        SelfTest.Register(LiveTerrainName, RunLiveTerrain);
        SelfTest.Register(LiveObjectsName, RunLiveObjects);
        SelfTest.Register(WorldRadiusName, RunWorldRadius);
        SelfTest.Register(LocalOnlyName, RunLocalOnly);
    }

    internal static void Unregister()
    {
        SelfTest.Unregister(SettingsName);
        SelfTest.Unregister(ToggleName);
        SelfTest.Unregister(RelogName);
        SelfTest.Unregister(BlockedName);
        SelfTest.Unregister(ConsoleName);
        SelfTest.Unregister(LiveTerrainName);
        SelfTest.Unregister(LiveObjectsName);
        SelfTest.Unregister(WorldRadiusName);
        SelfTest.Unregister(LocalOnlyName);
    }

    private static IEnumerator Ready(Kit.Checks c, Kit.Box ready)
    {
        ready.Ok = false;
        if (Player.m_localPlayer == null || Utils.GetMainCamera() == null || EnvMan.instance == null)
        {
            c.Check(false, "no local player, camera or weather manager");
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

    // ---------- horizons.settings

    // README configuration table, top to bottom.
    private static readonly string[][] ReadmeOrder =
    {
        new[] { "General", "Enabled", "Status" },
        new[]
        {
            DHConfig.LodSection, "BaseTileSize", "BaseVertexSpacing", "LodLevels", "SplitFactor", "SplitHysteresis", "ViewDistance",
            "WorldRadius", "FillCracks", "CrackFillDepth", "ExactMaxSpacing", "NearTerrainOffset", "TerrainMaterial", "FarTerrainDraw",
            "LodHideDistance",
        },
        new[] { DHConfig.StreamingSection, "MaxBuildsInFlight", "MaxMeshBuildsPerFrame", "UpdateInterval", "UpdateStepDistance", "WaitForZones" },
        new[]
        {
            DHConfig.RenderingSection, "RaiseCameraFarClip", "CameraFarClip", "FogDensityMultiplier", "FogClearDensity", "FogStormDensity",
            "KeepWetWeatherFog", "RealTerrainFadeFix", "FarWater", "FarWaterInnerRadius", "FarWaterGloss", "FarWaterFog", "FarWaterFoam",
        },
        new[]
        {
            DHConfig.ObjectsSection, "ObjectsEnabled", "ObjectMeshDistance", "ObjectFullDistance", "ObjectThinDistance", "ObjectFarDistance",
            "ObjectThinFactor", "ObjectThinScale", "ObjectFarFactor", "ObjectFarScale", "PieceDistance", "RockDistance", "BigRockSize",
            "BigTreeHeight", "SmallTreeCards", "DrawTrees", "DrawRocks", "DrawBushes", "DrawPieces", "DrawLogs", "ImpostorResolution",
            "ImpostorBakeBrightness", "ImpostorShader", "ObjectBuildBudgetMs", "ObjectUpdateInterval", "ObjectCacheSeconds", "MaxVertsPerMesh",
            "MaxVertsPerObject", "SnapObjectsToTerrain", "FarObjectWind",
        },
        new[] { DHConfig.SpyglassSection, "SpyglassDetail", "SpyglassMaxBoost" },
        new[] { DHConfig.LoggingSection, "DebugLogging" },
    };

    private static ConfigurationManagerAttributes Attributes(ConfigEntryBase entry)
    {
        var tags = entry.Description != null ? entry.Description.Tags : null;
        return tags != null && tags.Length > 0 ? tags[0] as ConfigurationManagerAttributes : null;
    }

    private static IEnumerator RunSettings()
    {
        const string N = SettingsName;
        var c = new Kit.Checks(N);
        var bySection = new Dictionary<string, List<ConfigEntryBase>>();
        var byKey = new Dictionary<string, ConfigEntryBase>();
        foreach (var kv in Plugin.Cfg.File)
        {
            if (!bySection.TryGetValue(kv.Key.Section, out var list))
            {
                list = new List<ConfigEntryBase>();
                bySection[kv.Key.Section] = list;
            }
            list.Add(kv.Value);
            byKey[kv.Key.Section + "/" + kv.Key.Key] = kv.Value;
        }
        var wantSections = new List<string>();
        foreach (var section in ReadmeOrder)
        {
            wantSections.Add(section[0]);
        }
        var haveSections = new List<string>(bySection.Keys);
        haveSections.Sort(StringComparer.Ordinal);
        var sortedWant = new List<string>(wantSections);
        sortedWant.Sort(StringComparer.Ordinal);
        c.Check(string.Join("|", haveSections.ToArray()) == string.Join("|", sortedWant.ToArray()),
            $"sections are {string.Join(", ", wantSections.ToArray())} (found {string.Join(", ", haveSections.ToArray())})");

        foreach (var section in ReadmeOrder)
        {
            if (!bySection.TryGetValue(section[0], out var entries))
            {
                continue;
            }
            // ConfigurationManager lists a section's settings by Order, highest first.
            entries.Sort((a, b) =>
            {
                var oa = Attributes(a) != null && Attributes(a).Order.HasValue ? Attributes(a).Order.Value : int.MinValue;
                var ob = Attributes(b) != null && Attributes(b).Order.HasValue ? Attributes(b).Order.Value : int.MinValue;
                return ob.CompareTo(oa);
            });
            var have = new List<string>();
            var orders = new HashSet<int>();
            var unique = true;
            foreach (var e in entries)
            {
                have.Add(e.Definition.Key);
                var a = Attributes(e);
                if (a == null || !a.Order.HasValue || !orders.Add(a.Order.Value))
                {
                    unique = false;
                }
            }
            var want = new List<string>(section);
            want.RemoveAt(0);
            c.Check(unique, $"section {section[0]}: every setting has its own ConfigurationManager order");
            c.Check(string.Join(",", have.ToArray()) == string.Join(",", want.ToArray()),
                $"section {section[0]} lists its settings in the README's order (listed: {string.Join(", ", have.ToArray())})");
        }

        foreach (var key in new[] { "TerrainMaterial", "FarTerrainDraw" })
        {
            c.Check(byKey.TryGetValue(DHConfig.LodSection + "/" + key, out var e) && e.Description.AcceptableValues is AcceptableValueList<string>,
                $"{key} is a choice from a list (drop-down)");
        }
        c.Check(byKey.TryGetValue(DHConfig.ObjectsSection + "/ImpostorShader", out var shader) && shader.Description.AcceptableValues is AcceptableValueList<string>,
            "ImpostorShader is a choice from a list (drop-down)");
        c.Check(byKey.TryGetValue(DHConfig.ObjectsSection + "/ImpostorResolution", out var res) && res.Description.AcceptableValues is AcceptableValueList<int>,
            "ImpostorResolution is a choice from a list (drop-down)");

        if (c.Check(byKey.TryGetValue("General/Status", out var status) && byKey.ContainsKey("General/Enabled"), "General has Enabled and Status"))
        {
            var sa = Attributes(status);
            c.Check(status.SettingType == typeof(string) && sa != null && sa.ReadOnly == true && sa.HideDefaultButton == true && sa.CustomDrawer != null,
                "Status is plain read-only text (read-only, no reset button, own text drawer)");
        }

        c.Report("sections, order, choice lists and Status checked");
        yield break;
    }

    // ---------- horizons.toggle

    private static IEnumerator RunToggle()
    {
        const string N = ToggleName;
        var c = new Kit.Checks(N);
        var weather = new Kit.Weather();
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (ready.Ok && !c.Check(Kit.Shrink, "FarTerrainDraw is not 'shrink' in this config: the buffer checks need it"))
            {
                ready.Ok = false;
            }
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            var cfg = Plugin.Cfg;
            var cam = Utils.GetMainCamera();
            var tl = TerrainLink.Current;
            var mgr = LodTerrainManager.Instance;
            Kit.ForceFogDefaults();
            Kit.Force(cfg.FarWater, true);
            Kit.Force(cfg.ObjectsEnabled, true);
            Kit.Force(cfg.RealTerrainFadeFix, true);
            var box = new Kit.Box();
            weather.Force("Clear", 0.5f);
            yield return Kit.WaitWeather("Clear", box);
            if (!c.Check(box.Ok, box.Detail))
            {
                c.Report("");
                yield break;
            }
            var raw = EnvMan.instance.GetEnv("Clear").m_fogDensityDay;
            yield return Kit.WaitUntil(() => mgr.SeaPainting && mgr.TestSeaDraws > 0, 10f, box);
            c.Check(box.Ok, "before: the far sea is painted");
            yield return Kit.WaitUntil(() => GameObject.Find("DistantHorizons_Objects") != null, 25f, box);
            c.Check(box.Ok, "before: far objects exist");
            yield return Kit.Frames(3);

            var raise = cfg.RaiseCameraFarClip.Value;
            var originalFar = mgr.TestOriginalFarClip;
            var handlers = cfg.TestHandlerCount;
            var rpcs = ZRoutedRpc.instance.m_functions.Count;
            var errors = ErrorWatch.Errors;
            LodTerrainManager.TestRealZoneLeftovers(out _, out var sunkBefore, out _, out _);
            c.Check(Kit.Near(RenderSettings.fogDensity, raw * 0.25f), $"before: clear-weather fog is thinned ({Kit.F(RenderSettings.fogDensity)}, weather {Kit.F(raw)})");
            c.Check(Kit.LivePatches() == 5 && Kit.BuilderPatch(), $"before: the mod's patches are on ({Kit.LivePatches()} of 5, builder patch {Kit.BuilderPatch()})");
            c.Check(Terminal.commands.ContainsKey("dh"), "before: the dh console command exists");
            c.Check(!raise || (originalFar > 0f && cam.farClipPlane > originalFar), $"before: the far plane is raised ({cam.farClipPlane:0} m, the game's {originalFar:0} m)");
            c.Check(sunkBefore > 0 && Kit.Buffers(cam, Kit.TerrainBuffer) == 1, "before: real zones painted and one paint buffer on the camera");
            c.Check(handlers == 2, $"before: the two managers listen to settings ({handlers})");

            // Pause like the Esc menu does in single player.
            Game.Pause();
            yield return Kit.WaitUntil(() => Time.timeScale == 0f, 3f, box);
            if (!c.Check(box.Ok, "the game pauses (single player)"))
            {
                c.Report("");
                yield break;
            }

            Kit.SwitchOff();
            // At once, same frame, still paused.
            c.Check(!Plugin.Instance.IsActive && Plugin.Instance.StatusText == Kit.OffText, $"off: the mod is inactive and Status says why ('{Plugin.Instance.StatusText}')");
            c.Check(Kit.Near(RenderSettings.fogDensity, raw), $"off, at once while paused: normal fog ({Kit.F(RenderSettings.fogDensity)}, weather {Kit.F(raw)})");
            c.Check(LodTerrainManager.Instance == null && DistantObjectManager.Instance == null, "off, at once: both managers gone");
            c.Check(Kit.Buffers(cam, null) == 0, $"off, at once: no far terrain or far sea buffer on the camera ({Kit.Buffers(cam, null)})");
            c.Check(!raise || Mathf.Abs(cam.farClipPlane - originalFar) < 0.5f, $"off, at once: normal sky distance (far plane {cam.farClipPlane:0} m, the game's {originalFar:0} m)");
            Kit.LodPlanes(out var planes, out var hidden, out var visible, out _);
            c.Check(hidden == 0 && (planes == 0 || Kit.Near(visible, Kit.VanillaPlaneDistance())),
                $"off, at once: the game's distant water plane is back ({Kit.F(visible)}, the game writes {Kit.F(Kit.VanillaPlaneDistance())}; {hidden} of {planes} out of reach)");
            LodTerrainManager.TestRealZoneLeftovers(out var zones, out var sunk, out var forcedOff, out var tessOff);
            c.Check(zones > 0 && sunk == 0 && forcedOff == 0 && tessOff == 0,
                $"off, at once: real zones are drawn by the game alone ({sunk} of {zones} still depth-only, {forcedOff} forced off, {tessOff} changed tessellation)");
            c.Check(Kit.LivePatches() == 0, $"off: the mod's live patches are removed ({Kit.LivePatches()} of 5 left)");
            c.Check(Kit.BuilderPatch(), "off: the always-on terrain builder patch stays");
            c.Check(!Terminal.commands.ContainsKey("dh") && Kit.AnyContains(Kit.Console("dh"), "is not a recognized command"), "off: dh is an unknown console command");
            c.Check(cfg.TestHandlerCount == 0, $"off: nothing listens to the settings any more ({cfg.TestHandlerCount})");
            yield return Kit.Frames(2);
            c.Check(Time.timeScale == 0f, "still paused");
            c.Check(Kit.Roots("DistantHorizons_LOD") == 0 && Kit.Roots("DistantHorizons_Objects") == 0 && Kit.Roots("DH_ImpostorBakeRig") == 0,
                "off, one frame later, still paused: no far tile, far object or bake object in the scene");

            // Vanilla distant terrain comes back by itself, paused, camera not moving.
            var camAt = cam.transform.position;
            var meshes = new Kit.Box();
            yield return Kit.WaitUntil(() => tl.m_heightmaps.Count > 0 && Kit.VanillaMeshes(tl) == tl.m_heightmaps.Count, 20f, meshes);
            c.Check(meshes.Ok, $"off: the vanilla distant terrain has its meshes within 20 s ({Kit.VanillaMeshes(tl)} of {tl.m_heightmaps.Count}, after {meshes.Detail})");
            c.Check(Time.timeScale == 0f && Vector3.Distance(cam.transform.position, camAt) < 1f, "…while still paused and without the camera moving");
            yield return Kit.Shot(N, "off-paused");

            // What a world load does while the mod is off: TerrainLod comes on with no patch of mine.
            tl.enabled = false;
            tl.enabled = true;
            yield return Kit.Frames(2);
            c.Check(LodTerrainManager.Instance == null && tl.GetComponent<LodTerrainManager>() == null && tl.GetComponent<DistantObjectManager>() == null && tl.m_heightmaps.Count > 0,
                $"off: TerrainLod switched off and on (as a world load does) makes the vanilla 3x3 and no manager ({tl.m_heightmaps.Count} vanilla tiles)");
            yield return Kit.WaitUntil(() => tl.m_heightmaps.Count > 0 && Kit.VanillaMeshes(tl) == tl.m_heightmaps.Count, 20f, meshes);
            c.Check(meshes.Ok, "off: that vanilla 3x3 gets its meshes too");

            Game.Unpause();
            yield return Kit.WaitUntil(() => Time.timeScale > 0f, 3f, box);
            yield return Kit.WaitFixed(3);
            c.Check(box.Ok && Kit.Near(RenderSettings.fogDensity, raw), $"resumed with the mod off: the fog stays the game's own ({Kit.F(RenderSettings.fogDensity)})");
            c.Check(ErrorWatch.Errors == errors, $"resumed: no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");

            // Tick it again.
            Kit.SwitchOn();
            var onAt = Kit.Now;
            c.Check(Plugin.Instance.IsActive && Plugin.Instance.StatusText == "Active.", $"on: the mod is active ('{Plugin.Instance.StatusText}')");
            var up = new Kit.Box();
            yield return Kit.WaitTerrain(up, 30f);
            c.Check(up.Ok && Kit.Now - onAt <= 10f, $"on: the far terrain streams back within a few seconds ({Kit.Now - onAt:0.0} s, limit 10 s): " + up.Detail);
            yield return Kit.WaitFixed(3);
            c.Check(Kit.LivePatches() == 5 && Terminal.commands.ContainsKey("dh"), $"on: patches ({Kit.LivePatches()} of 5) and the dh command are back");
            c.Check(Kit.Near(RenderSettings.fogDensity, raw * 0.25f), $"on: clear-weather fog is thinned again ({Kit.F(RenderSettings.fogDensity)})");

            // Twice more, quickly.
            for (var i = 0; i < 2; i++)
            {
                Kit.SwitchOff();
                yield return null;
                Kit.SwitchOn();
                yield return null;
            }
            yield return Kit.WaitTerrain(up, 30f);
            yield return Kit.WaitUntil(() => LodTerrainManager.Instance != null && LodTerrainManager.Instance.SeaPainting, 10f, box);
            yield return Kit.Frames(3);
            c.Check(up.Ok && Plugin.Instance.IsActive, "after two more quick off-on: active, far terrain there: " + up.Detail);
            c.Check(tl.GetComponents<LodTerrainManager>().Length == 1 && tl.GetComponents<DistantObjectManager>().Length == 1, "…one terrain manager and one object manager");
            c.Check(Kit.Roots("DistantHorizons_LOD") == 1 && Kit.Roots("DistantHorizons_Objects") <= 1 && Kit.Roots("DH_ImpostorBakeRig") <= 1,
                $"…no doubled terrain: one far tile root ({Kit.Roots("DistantHorizons_LOD")}), at most one object root ({Kit.Roots("DistantHorizons_Objects")})");
            c.Check(Kit.Buffers(cam, Kit.TerrainBuffer) == 1 && Kit.Buffers(cam, Kit.SeaBuffer) <= 1,
                $"…one paint buffer on the camera ({Kit.Buffers(cam, Kit.TerrainBuffer)} terrain, {Kit.Buffers(cam, Kit.SeaBuffer)} sea)");
            c.Check(!TerrainLink.VanillaActive, "…the vanilla 3x3 is gone again");
            c.Check(cfg.TestHandlerCount == handlers, $"…the same number of settings listeners as before ({cfg.TestHandlerCount}, before {handlers})");
            c.Check(ZRoutedRpc.instance.m_functions.Count == rpcs, $"…no network call registered by all this ({ZRoutedRpc.instance.m_functions.Count}, before {rpcs})");
            c.Check(ErrorWatch.Errors == errors, $"…no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report("off while paused = vanilla at once, on = far terrain back, quick toggles leave one of everything");
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
            weather.Restore();
            Kit.PutBack();
        }
    }

    // ---------- horizons.relog

    private static IEnumerator RunRelog()
    {
        const string N = RelogName;
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
            var cam = Utils.GetMainCamera();
            var tl = TerrainLink.Current;
            var mgr = LodTerrainManager.Instance;
            var raise = cfg.RaiseCameraFarClip.Value;
            var originalFar = mgr.TestOriginalFarClip;
            var errors = ErrorWatch.Errors;
            yield return Kit.Frames(2);

            // Logout half: the game switches TerrainLod off (my OnDisable postfix runs).
            tl.enabled = false;
            c.Check(LodTerrainManager.Instance == null && DistantObjectManager.Instance == null && TerrainLink.Current == null,
                "TerrainLod switched off (what a logout does): both managers stopped, the link is dropped");
            c.Check(Kit.Buffers(cam, null) == 0, $"…no Distant Horizons buffer left on the camera ({Kit.Buffers(cam, null)})");
            c.Check(!raise || originalFar <= 0f || Mathf.Abs(cam.farClipPlane - originalFar) < 0.5f, $"…the far plane is the game's again ({cam.farClipPlane:0} m)");
            LodTerrainManager.TestRealZoneLeftovers(out var zones, out var sunk, out var forcedOff, out _);
            c.Check(sunk == 0 && forcedOff == 0, $"…no real zone left depth-only or forced off ({sunk}, {forcedOff} of {zones})");
            Kit.LodPlanes(out var planes, out var hidden, out _, out _);
            c.Check(hidden == 0, $"…the game's distant water plane is not left out of reach ({hidden} of {planes})");
            c.Check(cfg.TestHandlerCount == 0, $"…nothing listens to the settings ({cfg.TestHandlerCount})");
            yield return Kit.Frames(2);
            c.Check(Kit.Roots("DistantHorizons_LOD") == 0 && Kit.Roots("DistantHorizons_Objects") == 0, "…no far tile or far object left in the scene");

            // World load half: TerrainLod comes on (my OnEnable prefix runs, vanilla CreateMeshes is skipped).
            tl.enabled = true;
            c.Check(TerrainLink.Current == tl && LodTerrainManager.Instance != null && DistantObjectManager.Instance != null,
                "TerrainLod switched on (what a world load does): both managers attached");
            c.Check(tl.m_heightmaps.Count == 0, $"…the vanilla 3x3 was not created ({tl.m_heightmaps.Count} tiles)");
            var up = new Kit.Box();
            yield return Kit.WaitTerrain(up, 60f);
            yield return Kit.Frames(3);
            c.Check(up.Ok, "…the far terrain works again: " + up.Detail);
            c.Check(tl.GetComponents<LodTerrainManager>().Length == 1 && tl.GetComponents<DistantObjectManager>().Length == 1, "…one manager of each kind");
            c.Check(Kit.Roots("DistantHorizons_LOD") == 1, $"…one far tile root ({Kit.Roots("DistantHorizons_LOD")})");
            c.Check(!Kit.Shrink || Kit.Buffers(cam, Kit.TerrainBuffer) == 1, $"…one paint buffer on the camera ({Kit.Buffers(cam, Kit.TerrainBuffer)})");
            c.Check(cfg.TestHandlerCount == 2, $"…the two managers listen to settings again ({cfg.TestHandlerCount})");
            c.Check(ErrorWatch.Errors == errors, $"…no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report("TerrainLod off and on with the mod on: clean stop, clean start");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    // ---------- horizons.blocked

    private static IEnumerator RunBlocked()
    {
        const string N = BlockedName;
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
            var cam = Utils.GetMainCamera();
            var tl = TerrainLink.Current;
            var warnings = ErrorWatch.Warnings("also draws the distant terrain");
            var exact = ErrorWatch.Warnings(StandaloneWarning);

            // The standalone counts as loaded from now.
            ForeignMods.ForgetLoggedForTest(StandaloneGuid);
            ForeignMods.TestPlugins.Add(new KeyValuePair<string, string>(StandaloneGuid, "Distant Horizons"));
            Kit.Refresh();
            c.Check(!Plugin.Instance.IsActive && Plugin.Instance.State == ModState.Conflict, $"with the standalone loaded the mod is inactive (state {Plugin.Instance.State})");
            c.Check(Plugin.Instance.StatusText == StandaloneStatus, $"Status is the exact sentence of the test list (got '{Plugin.Instance.StatusText}')");
            Kit.Refresh();
            Kit.Refresh();
            c.Check(ErrorWatch.Warnings("also draws the distant terrain") == warnings + 1,
                $"one warning in the log, also after more refreshes ({ErrorWatch.Warnings("also draws the distant terrain") - warnings})");
            c.Check(ErrorWatch.Warnings(StandaloneWarning) == exact + 1, "the warning names the standalone and says to remove one of them");
            c.Check(LodTerrainManager.Instance == null && DistantObjectManager.Instance == null && Kit.LivePatches() == 0 && Kit.Buffers(cam, null) == 0,
                $"this mod draws nothing: no manager, no live patch ({Kit.LivePatches()}), no buffer ({Kit.Buffers(cam, null)})");
            yield return Kit.Frames(2);
            c.Check(Kit.Roots("DistantHorizons_LOD") == 0 && Kit.Roots("DistantHorizons_Objects") == 0, "no far tile or far object of this mod in the scene");
            var meshes = new Kit.Box();
            yield return Kit.WaitUntil(() => tl.m_heightmaps.Count > 0 && Kit.VanillaMeshes(tl) == tl.m_heightmaps.Count, 20f, meshes);
            c.Check(meshes.Ok, $"the game's own distant terrain is left for the other mod ({Kit.VanillaMeshes(tl)} of {tl.m_heightmaps.Count} vanilla tiles with a mesh)");

            // Removed again.
            ForeignMods.TestPlugins.Clear();
            Kit.Refresh();
            c.Check(Plugin.Instance.IsActive && Plugin.Instance.StatusText == "Active.", $"without it the mod is active again ('{Plugin.Instance.StatusText}')");
            var up = new Kit.Box();
            yield return Kit.WaitTerrain(up, 60f);
            c.Check(up.Ok, "…and the far terrain is back: " + up.Detail);
            c.Report("standalone loaded = exact Status, one warning, nothing drawn; removed = active");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    // ---------- horizons.console

    private static IEnumerator RunConsole()
    {
        const string N = ConsoleName;
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
            var mgr = LodTerrainManager.Instance;
            Kit.Force(cfg.ObjectsEnabled, true);
            var box = new Kit.Box();
            yield return Kit.WaitUntil(() => DistantObjectManager.Instance != null && DistantObjectManager.Instance.TotalBuilds > 0, 30f, box);
            var om = DistantObjectManager.Instance;
            var errors = ErrorWatch.Errors;

            var lines = Kit.Console("dh");
            if (!c.Check(lines != null, "the game has a console object"))
            {
                c.Report("");
                yield break;
            }
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: tiles="), $"dh prints the tile counts ({Kit.First(lines)})");
            lines = Kit.Console("dh stats");
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: tiles="), $"dh stats prints the tile counts ({Kit.First(lines)})");
            lines = Kit.Console("dh envs");
            c.Check(Kit.AnyStartsWith(lines, "Environments (") && lines.Count > 5, $"dh envs lists the weathers ({lines.Count} lines, first: {Kit.First(lines)})");
            lines = Kit.Console("dh objects");
            c.Check(Kit.AnyStartsWith(lines, "DistantObjects: tiles="), $"dh objects prints the object counts ({Kit.First(lines)})");

            var view = cfg.ViewDistance.GetSerializedValue();
            lines = Kit.Console("dh get ViewDistance");
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: ViewDistance = " + view), $"dh get ViewDistance prints the setting ({Kit.First(lines)})");
            lines = Kit.Console("dh get Enabled");
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: unknown setting Enabled"), $"dh get Enabled: unknown setting ({Kit.First(lines)})");
            lines = Kit.Console("dh get Status");
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: unknown setting Status"), $"dh get Status: unknown setting ({Kit.First(lines)})");
            lines = Kit.Console("dh get");
            c.Check(Kit.AnyStartsWith(lines, "usage: dh get <Setting>"), $"dh get without a name prints the usage ({Kit.First(lines)})");

            // dh set with the value the setting already has: the whole set path runs, nothing changes, nothing is saved.
            var path = cfg.File.ConfigFilePath;
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            lines = Kit.Console("dh set ViewDistance " + view);
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: ViewDistance = " + view), $"dh set ViewDistance {view} answers with the value ({Kit.First(lines)})");
            c.Check((File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue) == stamp, "…and the config file was not written (same value)");
            lines = Kit.Console("dh set NoSuchSetting 1");
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: unknown setting NoSuchSetting"), $"dh set on an unknown name: unknown setting ({Kit.First(lines)})");
            lines = Kit.Console("dh set ViewDistance");
            c.Check(Kit.AnyStartsWith(lines, "usage: dh set <Setting> <value>"), $"dh set without a value prints the usage ({Kit.First(lines)})");

            if (c.Check(box.Ok && om != null, "far objects are running"))
            {
                var clears = om.TestClears;
                lines = Kit.Console("dh objects rebuild");
                c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: rebuilding all object tiles"), $"dh objects rebuild says so ({Kit.First(lines)})");
                c.Check(om.TestClears == clears + 1, "…and every object tile was thrown away");
                yield return Kit.WaitUntil(() =>
                {
                    om.TestTotals(out _, out var built, out _, out _, out _, out _);
                    return built > 0;
                }, 30f, box);
                c.Check(box.Ok, $"…and object tiles are built again within 30 s ({box.Detail})");
            }

            var builds = mgr.TotalBuilds;
            lines = Kit.Console("dh rebuild");
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: rebuilding all LOD tiles"), $"dh rebuild says so ({Kit.First(lines)})");
            c.Check(mgr.TestTileCount == 0, $"…and every far tile was thrown away ({mgr.TestTileCount} left)");
            yield return Kit.WaitUntil(() => mgr.TotalBuilds >= builds + 4 && mgr.TestSurfaceCount > 0, 60f, box);
            c.Check(box.Ok && !TerrainLink.VanillaActive, $"…and the far terrain streams again ({mgr.TotalBuilds - builds} tiles built after {box.Detail})");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report("dh, stats, envs, objects, get, set, both rebuilds answered as documented");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    // ---------- horizons.live-terrain

    private static IEnumerator RunLiveTerrain()
    {
        const string N = LiveTerrainName;
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
            var mgr = LodTerrainManager.Instance;
            var cam = Utils.GetMainCamera();
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 40f);
            c.Check(settled.Ok, settled.Detail);
            var errors = ErrorWatch.Errors;

            // A real change (ConfigurationManager, the file) reaches the mod through the config file's change event: the
            // mod must be listening to it. The changes below are then made in memory and sent through that same handler.
            var wired = cfg.TestListensToFile();
            c.Check(wired == true, wired == null
                ? "cannot see who listens to the config file's change event in this BepInEx build"
                : "the mod listens to its config file's change event (settings apply live)");

            // ViewDistance = 5000: far detail stops refining (a tile already split may stay split up to 20 % farther).
            Kit.Force(cfg.ViewDistance, 22000f);
            yield return Kit.Frames(3);
            var limit = 5000f * (1f + cfg.SplitHysteresis.Value) + 5f;
            var before = mgr.TestInternalBeyond(cam.transform.position, limit);
            cfg.SetForTest(cfg.ViewDistance, 5000f);
            yield return Kit.Frames(3);
            var after = mgr.TestInternalBeyond(cam.transform.position, limit);
            c.Check(before > 0, $"with ViewDistance 22000 tiles farther than {limit:0} m are split ({before})");
            c.Check(after == 0, $"ViewDistance = 5000: no tile farther than {limit:0} m is split any more ({after}, before {before})");
            Kit.Coverage(mgr, 0f, 0f, cfg.V(cfg.WorldRadius), 128f, out var points, out var holes, out _, out var firstHole);
            c.Check(holes == 0, $"…and that leaves no hole ({holes} of {points} points without far ground, first {firstHole})");
            cfg.SetForTest(cfg.ViewDistance, 22000f);
            yield return Kit.Frames(3);
            c.Check(mgr.TestInternalBeyond(cam.transform.position, limit) > 0, "back to 22000: far tiles split again");

            // BaseVertexSpacing, changed three times like a dragged slider.
            var width = cfg.TileWidth;
            var target = Mathf.Approximately(cfg.V(cfg.BaseVertexSpacing), 4f) ? 5f : 4f;
            var tiles = mgr.TestTileCount;
            cfg.SetForTest(cfg.BaseVertexSpacing, target - 1f);
            yield return new WaitForSecondsRealtime(0.15f);
            cfg.SetForTest(cfg.BaseVertexSpacing, target - 0.5f);
            yield return new WaitForSecondsRealtime(0.15f);
            cfg.SetForTest(cfg.BaseVertexSpacing, target);
            var last = Kit.Now;
            var newWidth = cfg.TileWidth;
            yield return new WaitForSecondsRealtime(0.3f);
            c.Check(mgr.TestRebuildPending && mgr.TestTileCount == tiles && mgr.TestBuiltWithWidth(width) > 0,
                $"0.3 s after the last BaseVertexSpacing change the old far terrain is still there, the rebuild waits ({mgr.TestTileCount} tiles, before {tiles})");
            var fired = new Kit.Box();
            yield return Kit.WaitUntil(() => !mgr.TestRebuildPending, 3f, fired);
            var waited = Kit.Now - last;
            c.Check(fired.Ok && waited >= 0.45f && waited <= 2f, $"the far terrain rebuilds about half a second after the last change ({waited:0.00} s)");
            c.Check(newWidth != width && mgr.TestBuiltWithWidth(width) == 0, $"…every tile of the old spacing is gone ({mgr.TestBuiltWithWidth(width)} left with {width} quads per edge)");
            var built = new Kit.Box();
            yield return Kit.WaitUntil(() => mgr.TestBuiltWithWidth(newWidth) >= 4, 60f, built);
            c.Check(built.Ok, $"BaseVertexSpacing = {target}: tiles with {newWidth} quads per edge are built ({mgr.TestBuiltWithWidth(newWidth)} after {built.Detail})");

            // Put it back and wait for the terrain of the real setting.
            cfg.ClearForTest(cfg.BaseVertexSpacing);
            yield return Kit.WaitUntil(() => !mgr.TestRebuildPending && mgr.TestBuiltWithWidth(cfg.TileWidth) >= 4, 60f, built);
            c.Check(built.Ok && cfg.TileWidth == width, $"value put back: tiles with {width} quads per edge again");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report("ViewDistance and BaseVertexSpacing apply live, the rebuild waits for the last change");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    // ---------- horizons.live-objects

    private static int Trees(DistantObjectManager om)
    {
        om.TestTotals(out _, out _, out _, out var trees, out _, out _);
        return trees;
    }

    private static IEnumerator RunLiveObjects()
    {
        const string N = LiveObjectsName;
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
            // A tree of my own in the world data, so far trees exist whatever this world holds around here.
            var planted = ObjectTests.PlantNearTree(made);
            if (planted != null)
            {
                Kit.Console("dh objects rebuild");
                yield return Kit.Frames(2);
            }
            c.Note(planted != null ? $"a {planted} was put in the world data about 1.1 km away for this test" : "no tree prefab to put in the world: the test uses the far trees it finds");
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 40f, objects: true);
            var om = DistantObjectManager.Instance;
            if (!c.Check(settled.Ok && om != null, settled.Detail))
            {
                c.Report("");
                yield break;
            }
            var errors = ErrorWatch.Errors;
            var trees = Trees(om);
            c.Note(om.GetStats());
            if (!c.Check(trees > 0, $"far trees are drawn ({trees})"))
            {
                c.Report("");
                yield break;
            }

            // DrawTrees = false.
            var clears = om.TestClears;
            var t0 = Kit.Now;
            cfg.SetForTest(cfg.DrawTrees, false);
            yield return new WaitForSecondsRealtime(0.3f);
            c.Check(Trees(om) == trees, $"0.3 s after DrawTrees = false the far trees are still there ({Trees(om)} of {trees}): the change waits half a second");
            var first = new Kit.Box();
            yield return Kit.WaitUntil(() => Trees(om) < trees, 5f, first);
            var firstAfter = Kit.Now - t0;
            c.Check(first.Ok && firstAfter <= 2.5f, $"DrawTrees = false: the far trees start to go about half a second after the change ({firstAfter:0.00} s, limit 2.5 s)");
            var gone = new Kit.Box();
            yield return Kit.WaitUntil(() => Trees(om) == 0, 30f, gone);
            c.Check(gone.Ok, $"DrawTrees = false: no far tree left ({Trees(om)}) {Kit.Now - t0:0.0} s after the change");
            c.Check(om.TestClears == clears, "…without throwing every object tile away (other objects stay on screen)");
            cfg.SetForTest(cfg.DrawTrees, true);

            // ObjectsEnabled = false, then true.
            cfg.SetForTest(cfg.ObjectsEnabled, false);
            yield return Kit.Frames(3);
            om.TestTotals(out var tiles, out _, out _, out _, out _, out _);
            c.Check(tiles == 0 && Kit.Roots("DistantHorizons_Objects") == 0, $"ObjectsEnabled = false: all far objects are gone at once ({tiles} tiles left)");
            cfg.SetForTest(cfg.ObjectsEnabled, true);
            var back = new Kit.Box();
            yield return Kit.WaitUntil(() => Trees(om) > 0 && !om.TestBusy, 45f, back);
            c.Check(back.Ok, $"ObjectsEnabled = true (and DrawTrees = true): far objects with trees are back ({Trees(om)} trees after {back.Detail})");

            // ImpostorBakeBrightness dragged.
            clears = om.TestClears;
            var atlas = om.TestAtlas;
            var during = 0;
            for (var i = 0; i < 8; i++)
            {
                cfg.SetForTest(cfg.ImpostorBakeBrightness, 0.95f - 0.05f * i);
                yield return new WaitForSecondsRealtime(0.1f);
                if (om.TestClears != clears)
                {
                    during++;
                }
            }
            var last = Kit.Now - 0.1f;
            yield return new WaitForSecondsRealtime(0.25f);
            c.Check(during == 0 && om.TestClears == clears && ReferenceEquals(om.TestAtlas, atlas),
                "while ImpostorBakeBrightness is dragged (8 changes in 0.8 s) and just after, the tree cards are not re-baked");
            var rebaked = new Kit.Box();
            yield return Kit.WaitUntil(() => om.TestClears > clears, 3f, rebaked);
            var waited = Kit.Now - last;
            c.Check(rebaked.Ok && waited >= 0.45f && waited <= 2f, $"they are re-baked about half a second after the last change ({waited:0.00} s)");
            yield return new WaitForSecondsRealtime(2f);
            c.Check(om.TestClears == clears + 1, $"…once ({om.TestClears - clears} times)");
            yield return Kit.WaitUntil(() => om.TestAtlas != null && om.TestAtlas.Baked > 0 && Trees(om) > 0, 40f, back);
            c.Check(back.Ok && !ReferenceEquals(om.TestAtlas, atlas), $"…into a new card atlas, and far trees are drawn again ({(om.TestAtlas != null ? om.TestAtlas.Baked : 0)} cards after {back.Detail})");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report("DrawTrees, ObjectsEnabled and ImpostorBakeBrightness apply live");
        }
        finally
        {
            Kit.Safe("test tree", () => ObjectTests.Unmake(made));
            Kit.PutBack();
        }
    }

    // ---------- horizons.world-radius

    // Tiles (wanted, or only built ones) that lie inside the box of key: key itself, or finer tiles when it is split.
    private static int InBox(LodTerrainManager mgr, TileKey key, float size, bool builtOnly)
    {
        float minX = key.X * size, minZ = key.Y * size, maxX = minX + size, maxZ = minZ + size;
        var n = 0;
        foreach (var t in mgr.TestTiles)
        {
            if (t.Key.Level > key.Level || (builtOnly && t.State != TileState.Built))
            {
                continue;
            }
            if (t.MinX >= minX - 0.5f && t.MaxX <= maxX + 0.5f && t.MinZ >= minZ - 0.5f && t.MaxZ <= maxZ + 0.5f)
            {
                n++;
            }
        }
        return n;
    }

    private static IEnumerator RunWorldRadius()
    {
        const string N = WorldRadiusName;
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
            var mgr = LodTerrainManager.Instance;
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 40f);
            c.Check(settled.Ok, settled.Detail);
            var r0 = cfg.V(cfg.WorldRadius);
            var target = r0 + 1500f;
            // A tile one level under the root whose whole box lies beyond the old radius and inside the new one: every
            // root is split, so such a tile is wanted exactly when the radius reaches it.
            var level = mgr.RootLevel - 1;
            var size = cfg.BaseTileSize.Value * (1 << Mathf.Max(0, level));
            var found = false;
            var key = default(TileKey);
            var keyDistance = 0f;
            for (var y = -4; y <= 3 && !found && level >= 0; y++)
            {
                for (var x = -4; x <= 3 && !found; x++)
                {
                    var dx = Mathf.Max(x * size, 0f, -(x + 1) * size);
                    var dz = Mathf.Max(y * size, 0f, -(y + 1) * size);
                    var d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > r0 + 1f && d < target - 1f)
                    {
                        found = true;
                        key = new TileKey(level, x, y);
                        keyDistance = d;
                    }
                }
            }
            if (!c.Check(found && target <= 20000f, $"a tile box between the world radius {r0:0} m and {target:0} m exists in this layout"))
            {
                c.Report("");
                yield break;
            }
            c.Check(InBox(mgr, key, size, false) == 0, $"with WorldRadius {r0:0} the tile {key} ({keyDistance:0} m from the world's centre) is not drawn");

            cfg.SetForTest(cfg.WorldRadius, target);
            c.Check(Kit.Near(FarWater.WorldEdge, target), $"WorldRadius = {target:0}: the far sea's edge follows at once ({FarWater.WorldEdge:0} m)");
            var fired = new Kit.Box();
            yield return Kit.WaitUntil(() => !mgr.TestRebuildPending, 3f, fired);
            yield return Kit.Frames(3);
            c.Check(fired.Ok && InBox(mgr, key, size, false) > 0, $"…after the rebuild (half a second later) the tile {key} beyond the old radius is wanted");
            var builtBox = new Kit.Box();
            yield return Kit.WaitUntil(() => InBox(mgr, key, size, true) > 0, 60f, builtBox);
            c.Check(builtBox.Ok, $"…and built: the far land reaches the new edge ({builtBox.Detail})");
            Kit.Coverage(mgr, 0f, 0f, target, 256f, out var points, out var holes, out _, out var firstHole);
            c.Check(holes == 0, $"…far ground under every point of the {target:0} m disc ({holes} of {points} without, first {firstHole})");

            cfg.ClearForTest(cfg.WorldRadius);
            yield return Kit.WaitUntil(() => !mgr.TestRebuildPending, 3f, fired);
            yield return Kit.Frames(3);
            c.Check(fired.Ok && InBox(mgr, key, size, false) == 0 && Kit.Near(FarWater.WorldEdge, r0), $"value put back: the tile {key} is dropped again, the sea edge is {FarWater.WorldEdge:0} m");
            var up = new Kit.Box();
            yield return Kit.WaitTerrain(up, 40f);
            c.Check(up.Ok, "…and the far terrain is back: " + up.Detail);
            c.Report($"WorldRadius {r0:0} -> {target:0} -> {r0:0}: far land and sea edge follow");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    // ---------- horizons.local-only

    // Objects the world holds beyond the area the game simulates around the player (nothing there changes by itself).
    private static int FarWorldObjects()
    {
        var man = ZDOMan.instance;
        var sectors = man.m_objectsBySector;
        var refZone = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
        var keep = ZNet.instance.GetSyncedSimulationDistance().TotalSimulationDistance + 3;
        var n = 0;
        for (var i = 0; i < sectors.Length; i++)
        {
            var list = sectors[i];
            if (list == null || list.Count == 0)
            {
                continue;
            }
            var zx = i % 512 - 256;
            var zy = i / 512 - 256;
            if (Mathf.Abs(zx - refZone.x) <= keep && Mathf.Abs(zy - refZone.y) <= keep)
            {
                continue;
            }
            n += list.Count;
        }
        return n;
    }

    private static IEnumerator RunLocalOnly()
    {
        const string N = LocalOnlyName;
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
            Kit.Force(Plugin.Cfg.ObjectsEnabled, true);
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 40f, objects: true);
            c.Check(settled.Ok, settled.Detail);

            Kit.FarTilesAsGround(out var tiles, out _, out _);
            c.Check(tiles > 0 && Kit.NetworkedUnderRoots() == 0, $"none of the {tiles} far tiles, the far objects or the bake object is a network object ({Kit.NetworkedUnderRoots()} ZNetView)");
            c.Check(!Kit.Patched(typeof(ZNet), nameof(ZNet.OnNewConnection), ModInfo.Guid + ".framework")
                    && !Kit.Patched(typeof(ZNet), nameof(ZNet.RPC_PeerInfo), ModInfo.Guid + ".framework"),
                "the mod hooks no connection handshake (client-only: nothing is said to a server or to other players)");

            var rpcs = ZRoutedRpc.instance.m_functions.Count;
            var far = FarWorldObjects();
            TerrainLink.Detach();
            yield return Kit.Frames(2);
            TerrainLink.AttachIfInWorld();
            var up = new Kit.Box();
            yield return Kit.WaitTerrain(up, 60f);
            c.Check(up.Ok, up.Detail);
            Kit.Console("dh rebuild");
            Kit.Console("dh objects rebuild");
            yield return Kit.Settle(settled, 50f, objects: true);
            c.Check(settled.Ok, "after off-on and both rebuild commands: " + settled.Detail);
            c.Check(ZRoutedRpc.instance.m_functions.Count == rpcs, $"off-on and both rebuilds registered no network call ({ZRoutedRpc.instance.m_functions.Count}, before {rpcs})");
            c.Check(FarWorldObjects() == far, $"…and created or removed no object in the world ({FarWorldObjects()} far objects, before {far})");
            c.Check(Kit.NetworkedUnderRoots() == 0, "…and still nothing of the mod is a network object");
            c.Report("the mod only draws: no network object, no network call, no world object");
        }
        finally
        {
            Kit.PutBack();
        }
    }

    // ---------- horizons.cleanlog (registered last)

    internal static IEnumerator RunCleanLog()
    {
        const string N = CleanLogName;
        var c = new Kit.Checks(N);
        c.Check(ErrorWatch.Errors == 0, $"no error line from or about Distant Horizons since the game started ({ErrorWatch.Errors}, first: {ErrorWatch.FirstError})");
        var field = typeof(PatchGuard).GetField("Reported", BindingFlags.Static | BindingFlags.NonPublic);
        var reported = field != null ? field.GetValue(null) as HashSet<string> : null;
        c.Check(reported != null && reported.Count == 0,
            reported == null ? "cannot read the list of guarded failures" : $"no patch or update of the mod failed ({string.Join(", ", new List<string>(reported).ToArray())})");
        var mgr = LodTerrainManager.Instance;
        if (mgr != null)
        {
            c.Note($"exact tiles built from approximate heights this session: {mgr.TestExactFromPlain}; {mgr.GetStats()}");
        }
        c.Report("log clean so far");
        yield break;
    }
}
#endif
