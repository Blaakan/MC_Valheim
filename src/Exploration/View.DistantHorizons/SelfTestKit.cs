#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. Me = error and warning lines of this mod since game start (BepInEx log listener, put on once and
// kept: mod off-on in a test must not lose the count). Self test lines ("[selftest] FAIL ...") never count.
internal sealed class ErrorWatch : ILogListener
{
    private static ErrorWatch _instance;
    private static readonly object Gate = new object();
    private static int _errors;
    private static string _firstError = "";
    private static int _anyErrors;
    private static string _lastAnyError = "";
    private static readonly List<string> WarningLines = new List<string>();

    // Error lines of ANY plugin, the game or Unity since game start (self test lines never count), and the last one.
    internal static int AnyErrors
    {
        get
        {
            lock (Gate)
            {
                return _anyErrors;
            }
        }
    }

    internal static string LastAnyError
    {
        get
        {
            lock (Gate)
            {
                return _lastAnyError;
            }
        }
    }

    internal static int Errors
    {
        get
        {
            lock (Gate)
            {
                return _errors;
            }
        }
    }

    internal static string FirstError
    {
        get
        {
            lock (Gate)
            {
                return _firstError;
            }
        }
    }

    internal static void Install()
    {
        if (_instance != null)
        {
            return;
        }
        _instance = new ErrorWatch();
        BepInEx.Logging.Logger.Listeners.Add(_instance);
    }

    // Warning lines of this mod that contain text.
    internal static int Warnings(string text)
    {
        lock (Gate)
        {
            var n = 0;
            foreach (var line in WarningLines)
            {
                if (line.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    n++;
                }
            }
            return n;
        }
    }

    // Any thread (terrain builder thread log too).
    public void LogEvent(object sender, LogEventArgs e)
    {
        if (e == null || (e.Level & (LogLevel.Error | LogLevel.Fatal | LogLevel.Warning)) == 0)
        {
            return;
        }
        var text = e.Data != null ? e.Data.ToString() : "";
        if (text.IndexOf(SelfTest.Prefix, StringComparison.Ordinal) >= 0)
        {
            return;
        }
        var mine = e.Source != null && e.Source.SourceName == ModInfo.Name;
        lock (Gate)
        {
            if ((e.Level & LogLevel.Warning) != 0)
            {
                if (mine && WarningLines.Count < 500)
                {
                    WarningLines.Add(text);
                }
                return;
            }
            // Any error line at all: first line of it, who logged it, and whether its stack name another plugin me know.
            _anyErrors++;
            var lineEnd = text.IndexOf('\n');
            var head = (lineEnd > 0 ? text.Substring(0, lineEnd) : text).Trim();
            _lastAnyError = (e.Source != null ? e.Source.SourceName + ": " : "") + (head.Length > 160 ? head.Substring(0, 160) : head)
                            + (text.IndexOf("ValheimCommunityPatch", StringComparison.Ordinal) >= 0 ? " (stack names ValheimCommunityPatch)" : "");
            // Error of mine, or error of the game or Unity that name my code.
            if (mine || text.IndexOf(ModInfo.Guid, StringComparison.Ordinal) >= 0
                     || text.IndexOf("ViewDistantHorizonsMod", StringComparison.Ordinal) >= 0)
            {
                _errors++;
                if (_firstError.Length == 0)
                {
                    _firstError = text.Length > 300 ? text.Substring(0, 300) : text;
                }
            }
        }
    }

    public void Dispose()
    {
    }
}

// Debug build only. Tools the horizons.* self tests share: check list, waits on real time (they work while game is
// paused), weather and camera holds with put-back, console, mod off-on through the framework, settings forced in
// memory (DHConfig.SetForTest: never config), player lift (debug fly), state readers.
internal static class Kit
{
    internal const float PlaneHidden = 1000000f;
    internal const string OffText = "Inactive: a self test switched Distant Horizons off (this run only).";

    internal static float Now => Time.realtimeSinceStartup;

    internal static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    internal static bool Near(float a, float b, float relative = 0.01f) =>
        Mathf.Abs(a - b) <= Mathf.Max(0.0000005f, relative * Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)));

    // Checks of one test: every one counted, failures listed in one FAIL line, else one PASS line.
    internal sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        internal Checks(string name) => _name = name;

        internal bool AllOk => _failures.Count == 0;

        internal bool Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
            return ok;
        }

        internal void Note(string detail) => SelfTest.Note(_name, detail);

        internal void Report(string summary)
        {
            if (_failures.Count == 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK: {summary}");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    internal sealed class Box
    {
        internal bool Ok;
        internal string Detail = "";
    }

    // ---------- waits (real time)

    internal static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return null;
        }
    }

    internal static IEnumerator WaitUntil(Func<bool> done, float timeout, Box result = null)
    {
        var start = Now;
        var ok = done();
        while (!ok && Now - start < timeout)
        {
            yield return null;
            ok = done();
        }
        if (result != null)
        {
            result.Ok = ok;
            result.Detail = $"{Now - start:0.0} s";
        }
    }

    // Physics steps (the game write weather and fog in one). Never end while paused: timeout then.
    internal static IEnumerator WaitFixed(int steps, float timeout = 3f)
    {
        var start = Now;
        var last = Time.fixedTime;
        var seen = 0;
        while (seen < steps && Now - start < timeout)
        {
            yield return null;
            if (Time.fixedTime != last)
            {
                last = Time.fixedTime;
                seen++;
            }
        }
    }

    // Far terrain there: manager, some tiles, vanilla grid gone.
    internal static IEnumerator WaitTerrain(Box result, float timeout = 80f)
    {
        var start = Now;
        while (Now - start < timeout)
        {
            var mgr = LodTerrainManager.Instance;
            if (mgr != null && mgr.TotalBuilds >= 4 && mgr.TestSurfaceCount > 0 && !TerrainLink.VanillaActive)
            {
                result.Ok = true;
                result.Detail = $"{mgr.TotalBuilds} far tiles built after {Now - start:0.0} s";
                yield break;
            }
            yield return null;
        }
        var m = LodTerrainManager.Instance;
        result.Ok = false;
        result.Detail = m == null
            ? $"no terrain manager after {timeout:0} s (TerrainLod found: {TerrainLink.Current != null})"
            : $"far terrain not up after {timeout:0} s: {m.GetStats()}";
    }

    // Area loaded, far terrain done building (nothing waiting, no new tile for 2 s); objects = far objects done too.
    internal static IEnumerator Settle(Box result, float timeout = 45f, bool objects = false)
    {
        var start = Now;
        var lastBuilds = -1;
        var stableSince = start;
        var idleSince = -1f;
        result.Ok = false;
        while (Now - start < timeout)
        {
            var mgr = LodTerrainManager.Instance;
            var builds = mgr != null ? mgr.TotalBuilds : -1;
            if (mgr == null || builds != lastBuilds || mgr.TestPendingBuilds > 0 || mgr.TestRebuildPending || mgr.TestSurfaceCount == 0)
            {
                lastBuilds = builds;
                stableSince = Now;
            }
            var om = DistantObjectManager.Instance;
            if (objects && (om == null || om.TestBusy))
            {
                idleSince = -1f;
            }
            else if (idleSince < 0f)
            {
                idleSince = Now;
            }
            var loaded = ZoneSystem.instance != null && ZoneSystem.instance.IsActiveAreaLoaded();
            if (mgr != null && loaded && Now - stableSince >= 2f && idleSince >= 0f && Now - idleSince >= 1f)
            {
                result.Ok = true;
                result.Detail = $"settled after {Now - start:0.0} s";
                yield break;
            }
            yield return null;
        }
        var m = LodTerrainManager.Instance;
        var o = DistantObjectManager.Instance;
        result.Detail = $"not settled after {timeout:0} s: {(m != null ? m.GetStats() : "no terrain manager")}"
                        + (objects ? " | " + (o != null ? o.GetStats() : "no object manager") : "");
    }

    // ---------- weather

    // Me hold weather and hour like console "env" and "tod" do (EnvMan debug fields), and put back what was there.
    internal sealed class Weather
    {
        private readonly string _env;
        private readonly bool _todOn;
        private readonly float _tod;

        internal Weather()
        {
            var e = EnvMan.instance;
            _env = e.m_debugEnv;
            _todOn = e.m_debugTimeOfDay;
            _tod = e.m_debugTime;
        }

        internal void Force(string env, float time)
        {
            var e = EnvMan.instance;
            e.m_debugTimeOfDay = true;
            e.m_debugTime = time;
            e.m_debugEnv = env;
            e.ForceInstantEnvironmentSwitch();
        }

        internal void Restore()
        {
            var e = EnvMan.instance;
            if (e == null)
            {
                return;
            }
            e.m_debugEnv = _env;
            e.m_debugTimeOfDay = _todOn;
            e.m_debugTime = _tod;
            e.ForceInstantEnvironmentSwitch();
        }
    }

    // Game show this weather now (its own setup object, transition over) and wrote fog for it. Not while paused.
    internal static IEnumerator WaitWeather(string env, Box result, float timeout = 6f)
    {
        var man = EnvMan.instance;
        var want = man.GetEnv(env);
        result.Ok = false;
        if (want == null)
        {
            result.Detail = $"the game has no weather named '{env}'";
            yield break;
        }
        var start = Now;
        while (Now - start < timeout && !ReferenceEquals(man.GetCurrentEnvironment(), want))
        {
            man.ForceInstantEnvironmentSwitch();
            yield return null;
        }
        if (!ReferenceEquals(man.GetCurrentEnvironment(), want))
        {
            var cur = man.GetCurrentEnvironment();
            result.Detail = $"weather still '{(cur != null ? cur.m_name : "none")}' {timeout:0} s after asking for '{env}'";
            yield break;
        }
        yield return WaitFixed(2);
        result.Ok = true;
    }

    // Fog density of a weather with these day-part weights, like the game add it up.
    internal static float RawFog(EnvSetup env, float day, float night) => env.m_fogDensityDay * day + env.m_fogDensityNight * night;

    // What the mod's rule leave on screen for a weather whose own density is raw (README: clear weather x multiplier,
    // nothing at or above the storm density, linear between, wet weather kept).
    internal static float ExpectedFog(EnvSetup env, float raw)
    {
        var c = Plugin.Cfg;
        var mult = c.V(c.FogDensityMultiplier);
        if (mult == 1f || (c.V(c.KeepWetWeatherFog) && env.m_isWet))
        {
            return raw;
        }
        var clear = c.V(c.FogClearDensity);
        var storm = Mathf.Max(c.V(c.FogStormDensity), clear + 0.00001f);
        return raw * Mathf.Lerp(mult, 1f, Mathf.Clamp01((raw - clear) / (storm - clear)));
    }

    // ---------- settings forced in memory

    // Only when value differ (a forced equal value would still tell every handler "changed": needless rebuilds).
    internal static void Force<T>(BepInEx.Configuration.ConfigEntry<T> entry, T value)
    {
        var c = Plugin.Cfg;
        if (!EqualityComparer<T>.Default.Equals(c.V(entry), value))
        {
            c.SetForTest(entry, value);
        }
    }

    internal static void ForceFogDefaults()
    {
        var c = Plugin.Cfg;
        Force(c.FogDensityMultiplier, 0.25f);
        Force(c.FogClearDensity, 0.006f);
        Force(c.FogStormDensity, 0.02f);
        Force(c.KeepWetWeatherFog, true);
    }

    internal static bool Shrink => string.Equals(Plugin.Cfg.FarTerrainDraw.Value, "shrink", StringComparison.OrdinalIgnoreCase);

    // ---------- mod off and on through the framework (same path as the MC Mods panel after its Enabled click)

    internal static void SwitchOff()
    {
        Plugin.TestBlocker = OffText;
        Refresh();
    }

    internal static void SwitchOn()
    {
        Plugin.TestBlocker = null;
        Refresh();
    }

    // Framework look at every MC mod again (what a settings change make it do). It then write my Status line: for
    // that moment my config file is told not to save, so the line change in memory only and the file on disk is
    // never touched (when the test end the mod is on again and memory say what the file say).
    internal static void Refresh()
    {
        var file = Plugin.Cfg.File;
        var save = file.SaveOnConfigSet;
        file.SaveOnConfigSet = false;
        try
        {
            FeatureRegistry.RefreshAll();
        }
        finally
        {
            file.SaveOnConfigSet = save;
        }
    }

    // Finally of every test that turn things off: mod on, managers there, nothing forced.
    internal static void PutBack()
    {
        Safe("fake plugins", () => ForeignMods.TestPlugins.Clear());
        Safe("mod on", () =>
        {
            if (Plugin.TestBlocker != null)
            {
                Plugin.TestBlocker = null;
            }
            Refresh();
        });
        Safe("managers", () =>
        {
            var tl = TerrainLink.Current != null ? TerrainLink.Current : UnityEngine.Object.FindAnyObjectByType<TerrainLod>();
            if (tl != null && !tl.enabled)
            {
                tl.enabled = true;
            }
            if (LodTerrainManager.Instance == null && Plugin.Instance != null && Plugin.Instance.IsActive)
            {
                TerrainLink.AttachIfInWorld();
            }
        });
        Safe("settings", () => Plugin.Cfg.ClearAllForTest());
        Safe("spyglass", () => ViewBoost.TestSlot = null);
    }

    internal static void Safe(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log.Error($"self test clean-up ({what}) failed: {e}");
        }
    }

    // ---------- camera

    // Me hold the main camera still at a place (game camera script off), and give it back.
    internal sealed class CameraHold
    {
        private bool _held;

        internal void At(Vector3 pos, Vector3 lookAt)
        {
            var cam = Utils.GetMainCamera();
            if (cam == null || GameCamera.instance == null)
            {
                return;
            }
            GameCamera.instance.enabled = false;
            _held = true;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
        }

        internal void Release()
        {
            if (_held && GameCamera.instance != null)
            {
                GameCamera.instance.enabled = true;
            }
            _held = false;
        }
    }

    // ---------- player lift and travel (debug fly: no gravity, no fall damage)

    internal sealed class Flight
    {
        private readonly Player _player;
        private readonly Quaternion _rotation;
        private readonly bool _wasFlying;
        internal readonly Vector3 Origin;
        private bool _flying;

        internal Flight(Player player)
        {
            _player = player;
            Origin = player.transform.position;
            _rotation = player.transform.rotation;
            _wasFlying = player.m_debugFly;
        }

        internal void Put(Vector3 pos)
        {
            _flying = true;
            _player.m_debugFly = true;
            _player.transform.position = pos;
            _player.m_body.position = pos;
            _player.m_body.linearVelocity = Vector3.zero;
            _player.m_maxAirAltitude = pos.y;
        }

        internal bool Far => _player != null && Utils.DistanceXZ(_player.transform.position, Origin) > 20f;

        // Player over its start place and ground loaded there: stand it back.
        internal void Land()
        {
            if (!_flying || _player == null)
            {
                return;
            }
            var pos = Origin + Vector3.up * 0.2f;
            _player.transform.position = pos;
            _player.m_body.position = pos;
            _player.m_body.linearVelocity = Vector3.zero;
            _player.m_maxAirAltitude = pos.y;
            _player.transform.rotation = _rotation;
            _player.m_debugFly = _wasFlying;
            _flying = false;
        }

        // Finally: near start = stand back now. Far away (test stopped mid-way) = game's own teleport bring player
        // home (it hold the player until ground is there); player keep flying until it arrive, then me land it.
        internal void End()
        {
            if (!_flying || _player == null)
            {
                return;
            }
            if (!Far && ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(Origin))
            {
                Land();
                return;
            }
            _player.m_teleportCooldown = 10f;
            _player.TeleportTo(Origin, _rotation, true);
            if (Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(LandLater());
            }
        }

        private IEnumerator LandLater()
        {
            var until = Now + 60f;
            yield return new WaitForSecondsRealtime(1f);
            while (_player != null && _player.IsTeleporting() && Now < until)
            {
                yield return null;
            }
            Land();
        }
    }

    // ---------- console

    private static int _marker;

    // Me run a console line like typed in F5 console; give the lines it printed (null = no console object).
    internal static List<string> Console(string line)
    {
        var con = global::Console.instance;
        if (con == null)
        {
            return null;
        }
        var marker = "[horizons self test " + ++_marker + "]";
        con.AddString(marker);
        con.TryRunCommand(line);
        var buffer = con.m_chatBuffer;
        var at = buffer.LastIndexOf(marker);
        var lines = new List<string>();
        if (at < 0)
        {
            return lines;
        }
        for (var i = at + 1; i < buffer.Count; i++)
        {
            lines.Add(buffer[i]);
        }
        buffer.RemoveAt(at);
        return lines;
    }

    internal static bool AnyStartsWith(List<string> lines, string start)
    {
        if (lines == null)
        {
            return false;
        }
        foreach (var l in lines)
        {
            if (l != null && l.TrimStart().StartsWith(start, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool AnyContains(List<string> lines, string text)
    {
        if (lines == null)
        {
            return false;
        }
        foreach (var l in lines)
        {
            if (l != null && l.IndexOf(text, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    internal static string First(List<string> lines) => lines == null ? "(no console)" : lines.Count == 0 ? "(nothing printed)" : lines[0];

    // ---------- state readers

    private static readonly CameraEvent[] PaintEvents =
    {
        CameraEvent.AfterGBuffer, CameraEvent.BeforeGBuffer, CameraEvent.AfterForwardOpaque,
        CameraEvent.BeforeForwardOpaque, CameraEvent.BeforeForwardAlpha,
    };

    internal const string TerrainBuffer = "DistantHorizons far terrain";
    internal const string SeaBuffer = "DistantHorizons far sea";

    // My command buffers on a camera (name null = any of mine).
    internal static int Buffers(Camera cam, string name)
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

    // Game's own distant water plane(s) (_IsLod water). hidden = pushed out of reach by me; visible = fade-in radius
    // of a plane not hidden (-1 = none); size = bounds of the first plane.
    internal static void LodPlanes(out int planes, out int hidden, out float visible, out Vector3 size)
    {
        planes = 0;
        hidden = 0;
        visible = -1f;
        size = Vector3.zero;
        var block = new MaterialPropertyBlock();
        foreach (var w in Water.Instances)
        {
            var r = w != null ? w.GetComponent<MeshRenderer>() : null;
            var m = r != null ? r.sharedMaterial : null;
            if (m == null || !m.HasProperty("_IsLod") || m.GetFloat("_IsLod") < 0.5f)
            {
                continue;
            }
            if (planes == 0)
            {
                size = r.bounds.size;
            }
            planes++;
            r.GetPropertyBlock(block);
            var v = block.GetFloat("_VisibleMaxDistance");
            if (v >= PlaneHidden)
            {
                hidden++;
            }
            else
            {
                visible = v;
            }
        }
    }

    // What the game itself write on its water (Water.ApplySettings): near simulation distance x zone size.
    internal static float VanillaPlaneDistance()
    {
        return ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance * ZoneSystem.instance.m_zoneSize;
    }

    // Root objects with this name in the loaded scenes (objects asked to be destroyed stay until end of frame).
    internal static int Roots(string name)
    {
        var n = 0;
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
            {
                continue;
            }
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go != null && go.name == name)
                {
                    n++;
                }
            }
        }
        return n;
    }

    // Harmony patch of this owner on a game method?
    internal static bool Patched(Type type, string method, string owner, Type[] args = null)
    {
        var m = AccessTools.Method(type, method, args);
        if (m == null)
        {
            return false;
        }
        var info = Harmony.GetPatchInfo(m);
        return info != null && info.Owners.Contains(owner);
    }

    private static readonly Type[] SetEnvArgs =
    {
        typeof(EnvSetup), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float),
    };

    // How many of the five game methods the live toggle patch carry my patch now (5 = on, 0 = off).
    internal static int LivePatches()
    {
        var n = 0;
        if (Patched(typeof(EnvMan), nameof(EnvMan.SetEnv), ModInfo.Guid, SetEnvArgs)) n++;
        if (Patched(typeof(Heightmap), nameof(Heightmap.ApplySettingsOnAll), ModInfo.Guid)) n++;
        if (Patched(typeof(TerrainLod), nameof(TerrainLod.OnEnable), ModInfo.Guid)) n++;
        if (Patched(typeof(TerrainLod), nameof(TerrainLod.OnDisable), ModInfo.Guid)) n++;
        if (Patched(typeof(Water), nameof(Water.ApplySettings), ModInfo.Guid)) n++;
        return n;
    }

    internal static bool BuilderPatch() => Patched(typeof(HeightmapBuilder), nameof(HeightmapBuilder.Build), ModInfo.Guid + ".alwayson");

    // Vanilla 3x3 tiles with a built mesh.
    internal static int VanillaMeshes(TerrainLod tl)
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

    // Far tiles the game could take for real ground: listed with the real heightmaps, or not flagged distant.
    internal static void FarTilesAsGround(out int tiles, out int listed, out int notDistant)
    {
        tiles = 0;
        listed = 0;
        notDistant = 0;
        var root = GameObject.Find("DistantHorizons_LOD");
        if (root == null)
        {
            return;
        }
        var real = new HashSet<Heightmap>(Heightmap.GetAllHeightmaps());
        foreach (var hm in root.GetComponentsInChildren<Heightmap>(true))
        {
            tiles++;
            if (real.Contains(hm))
            {
                listed++;
            }
            if (!hm.IsDistantLod)
            {
                notDistant++;
            }
        }
    }

    // Objects of mine that the network would know: ZNetView under my roots.
    internal static int NetworkedUnderRoots()
    {
        var n = 0;
        foreach (var name in new[] { "DistantHorizons_LOD", "DistantHorizons_Objects", "DH_ImpostorBakeRig" })
        {
            var root = GameObject.Find(name);
            if (root != null)
            {
                n += root.GetComponentsInChildren<ZNetView>(true).Length;
            }
        }
        return n;
    }

    // Every point of a grid around (cx, cz) inside the world disc lie on a shown surface tile? holes = points on none.
    // coarsest = highest tile level met.
    internal static void Coverage(LodTerrainManager mgr, float cx, float cz, float half, float step, out int points, out int holes, out int coarsest, out string firstHole)
    {
        points = 0;
        holes = 0;
        coarsest = -1;
        firstHole = "";
        var radius = Plugin.Cfg.V(Plugin.Cfg.WorldRadius) - 2f;
        LodTile hint = null;
        for (var z = cz - half; z <= cz + half; z += step)
        {
            for (var x = cx - half; x <= cx + half; x += step)
            {
                if (x * x + z * z > radius * radius)
                {
                    continue;
                }
                points++;
                if (mgr.TrySampleSurface(x, z, ref hint, out _, out var key))
                {
                    if (key.Level > coarsest)
                    {
                        coarsest = key.Level;
                    }
                    continue;
                }
                holes++;
                if (firstHole.Length == 0)
                {
                    firstHole = $"({x:0},{z:0})";
                }
            }
        }
    }

    internal static IEnumerator Shot(string test, string label)
    {
        SelfTest.Screenshot(test, label);
        yield return null;
        yield return null;
    }
}
#endif
