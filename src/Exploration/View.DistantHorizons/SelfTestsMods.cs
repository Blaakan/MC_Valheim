#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. In-world self tests with one other mod loaded in the same game. Soft: other mod found by GUID
// (MC mods in the shared registry, others in BepInEx's plugin list), read through the game (status effect on the
// player, the game's own heightmap lookup), BepInEx config and reflection: never a type of theirs at compile time.
// Other mod missing = test FAILS and says so (a skipped test must not read as proof). One test per mod:
//   horizons.mods.sneak-fog        C02, Sneak Ambush: player crouched. Clear weather: Fog stealth icon the same with
//                                  the mod on (fog on screen thinned) and off. Then a foggy weather that my rule is
//                                  made to thin on screen too (FogStormDensity forced high, in memory): icon shown with
//                                  the same percent on and off, though the fog on screen differ by more than a third
//   horizons.mods.community-patch  C05, Valheim Community Patch: its "Verify Heightmap Registry" pass on (its own
//                                  switch object swapped for a stand-in that says on; the player's config never
//                                  touched). Control: one far tile put in the game's ground list for one lookup = it
//                                  logs DIVERGED (so the pass really runs). Then hundreds of lookups near and far, and
//                                  more while the far terrain rebuilds: no DIVERGED line, no far tile given as ground
//   horizons.mods.config-manager   T15, shudnal's Valheim Configuration Manager: its window opened, its own list for
//                                  this mod read: sections and their order, every section's settings in README order,
//                                  the four choice settings carry value lists, Status carry its own drawer; list drawn
//                                  for 20 frames without an error; screenshot
internal static class ModsTests
{
    private const string SneakFogName = "horizons.mods.sneak-fog";
    private const string CommunityPatchName = "horizons.mods.community-patch";
    private const string ConfigManagerName = "horizons.mods.config-manager";

    private const string SneakGuid = "MC.Combat.Sneak.Ambush";
    private const string VcpGuid = "MidnightsFX.ValheimCommunityPatch";
    private const string VcpLookupType = "ValheimCommunityPatch.Patches.Performance.HeightmapLookupPatch";
    private const string VcpVerifyKey = "Verify Heightmap Registry";
    private const string Diverged = "DIVERGED";
    private const string CmGuid = "_shudnal.ConfigurationManager";

    internal static void Register()
    {
        SelfTest.Register(SneakFogName, RunSneakFog);
        SelfTest.Register(CommunityPatchName, RunCommunityPatch);
        SelfTest.Register(ConfigManagerName, RunConfigManager);
    }

    internal static void Unregister()
    {
        SelfTest.Unregister(SneakFogName);
        SelfTest.Unregister(CommunityPatchName);
        SelfTest.Unregister(ConfigManagerName);
    }

    private static IEnumerator Ready(Kit.Checks c, Kit.Box ready)
    {
        ready.Ok = false;
        if (Player.m_localPlayer == null || Utils.GetMainCamera() == null || EnvMan.instance == null || ZNet.instance == null)
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

    // ---------- horizons.mods.sneak-fog (C02)

    // Sneak Ambush's Fog cue: status effect named "<its GUID>.Fog" (StealthCues of that mod), its tooltip carries
    // the percent ("creatures see you at N% of the distance").
    private static readonly int FogCueHash = (SneakGuid + ".Fog").GetStableHashCode();
    private static readonly Regex CuePercent = new Regex(@"at (\d+)% of the distance", RegexOptions.Compiled);

    private struct Cue
    {
        internal bool Shown;
        internal int Percent; // fog bonus in percent, -1 = not shown or not readable

        public override string ToString() => Shown ? (Percent >= 0 ? $"shown, -{Percent}%" : "shown, percent not readable") : "not shown";

        internal bool Same(Cue other) => Shown == other.Shown && Percent == other.Percent;
    }

    private static Cue ReadCue(Player player)
    {
        var cue = new Cue { Percent = -1 };
        var seman = player.GetSEMan();
        var effect = seman != null ? seman.GetStatusEffect(FogCueHash) : null;
        if (effect == null)
        {
            return cue;
        }
        cue.Shown = true;
        var m = CuePercent.Match(effect.GetTooltipString() ?? "");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var seen))
        {
            cue.Percent = 100 - seen;
        }
        return cue;
    }

    // Weather my fog rule leaves alone by default but Sneak Ambush gives a clear fog bonus for: day fog 0.03 to 0.3.
    // Mild ones first (not wet, not freezing, not always dark), then any not wet, then any.
    private static EnvSetup FoggyWeather()
    {
        EnvSetup best = null;
        var bestRank = int.MaxValue;
        foreach (var env in EnvMan.instance.m_environments)
        {
            if (env == null || string.IsNullOrEmpty(env.m_name) || env.m_fogDensityDay < 0.03f || env.m_fogDensityDay > 0.3f)
            {
                continue;
            }
            var rank = env.m_isWet ? 2 : env.m_isFreezing || env.m_isCold || env.m_alwaysDark ? 1 : 0;
            if (rank < bestRank)
            {
                best = env;
                bestRank = rank;
            }
        }
        return best;
    }

    // Weather shown, fog written, two stealth refreshes of the game (0.5 s each) gone by.
    private static IEnumerator ShowWeather(Kit.Weather weather, string env, Kit.Box box)
    {
        weather.Force(env, 0.5f);
        yield return Kit.WaitWeather(env, box);
        yield return Kit.WaitFixed(3);
        yield return new WaitForSeconds(1.6f);
    }

    private static IEnumerator RunSneakFog()
    {
        const string N = SneakFogName;
        var c = new Kit.Checks(N);
        var weather = new Kit.Weather();
        var player = Player.m_localPlayer;
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            if (!InputTests.ModActive(SneakGuid, out var state))
            {
                c.Check(false, $"Sneak Ambush is not loaded and active ({state}): nothing was checked");
                c.Report("");
                yield break;
            }
            // Its icons are each player's own choice: off = nothing to look at.
            var haveShow = InputTests.OtherSetting<bool>(SneakGuid, "Stealth", "ShowStealthCues", out var show);
            if (!c.Check(haveShow && show, $"Sneak Ambush's ShowStealthCues is on in its settings ({(haveShow ? show.ToString() : "not found")}): without its icons there is nothing to compare"))
            {
                c.Report("");
                yield break;
            }
            var foggy = FoggyWeather();
            if (!c.Check(foggy != null, "the game has a weather with a day fog density between 0.03 and 0.3"))
            {
                c.Report("");
                yield break;
            }
            if (!c.Check(!player.InInterior() && !player.IsSwimming(), "the player stands outdoors, not in water"))
            {
                c.Report("");
                yield break;
            }
            var cfg = Plugin.Cfg;
            var errors = ErrorWatch.Errors;
            Kit.ForceFogDefaults();
            var clearEnv = EnvMan.instance.GetEnv("Clear");
            var rawClear = clearEnv.m_fogDensityDay;
            var thinClear = Kit.ExpectedFog(clearEnv, rawClear);
            var rawFoggy = foggy.m_fogDensityDay;

            player.SetCrouch(true);
            var box = new Kit.Box();
            yield return Kit.WaitUntil(() => player.IsCrouching(), 3f, box);
            if (!c.Check(box.Ok, "the player crouches (Sneak Ambush shows its Fog icon only while crouching)"))
            {
                c.Report("");
                yield break;
            }

            // Mod on, clear weather: fog on screen thinned.
            yield return ShowWeather(weather, "Clear", box);
            c.Check(box.Ok, "clear weather at noon: " + (box.Ok ? "shown" : box.Detail));
            var clearOn = ReadCue(player);
            var clearOnFog = RenderSettings.fogDensity;
            var crouched = player.IsCrouching();
            c.Check(Kit.Near(clearOnFog, thinClear) && thinClear < rawClear * 0.9f, $"mod on, clear weather: the fog on screen is thinned ({Kit.F(clearOnFog)}, weather {Kit.F(rawClear)})");

            // Mod on, foggy weather, my rule made to thin it on screen too (default rule leaves it alone).
            Kit.Force(cfg.FogStormDensity, 1f);
            Kit.Force(cfg.KeepWetWeatherFog, false);
            yield return ShowWeather(weather, foggy.m_name, box);
            c.Check(box.Ok, $"weather '{foggy.m_name}' at noon: " + (box.Ok ? "shown" : box.Detail));
            var foggyOn = ReadCue(player);
            var foggyOnFog = RenderSettings.fogDensity;
            crouched &= player.IsCrouching();
            c.Check(foggyOnFog <= rawFoggy * 0.6f,
                $"mod on, '{foggy.m_name}' with FogStormDensity forced to 1: its fog on screen is thinned too ({Kit.F(foggyOnFog)}, weather {Kit.F(rawFoggy)})");
            c.Check(foggyOn.Shown && foggyOn.Percent >= 2, $"mod on, '{foggy.m_name}': Sneak Ambush shows its Fog icon with a bonus ({foggyOn}; none when its FogBonus setting is 0)");

            // Mod off (framework path), same two weathers.
            Kit.SwitchOff();
            c.Check(!Plugin.Instance.IsActive, $"Distant Horizons is off ('{Plugin.Instance.StatusText}')");
            yield return ShowWeather(weather, foggy.m_name, box);
            var foggyOff = ReadCue(player);
            var foggyOffFog = RenderSettings.fogDensity;
            crouched &= player.IsCrouching();
            c.Check(Kit.Near(foggyOffFog, rawFoggy), $"mod off, '{foggy.m_name}': the fog on screen is the weather's own ({Kit.F(foggyOffFog)}, weather {Kit.F(rawFoggy)})");
            yield return ShowWeather(weather, "Clear", box);
            var clearOff = ReadCue(player);
            var clearOffFog = RenderSettings.fogDensity;
            crouched &= player.IsCrouching();
            c.Check(Kit.Near(clearOffFog, rawClear), $"mod off, clear weather: the fog on screen is the weather's own ({Kit.F(clearOffFog)}, weather {Kit.F(rawClear)})");

            c.Check(crouched, "the player stayed crouched at all four looks");
            c.Check(clearOn.Same(clearOff),
                $"clear weather: the Fog stealth icon is the same with and without Distant Horizons (with: {clearOn}, fog on screen {Kit.F(clearOnFog)}; without: {clearOff}, fog on screen {Kit.F(clearOffFog)})");
            c.Check(foggyOn.Same(foggyOff),
                $"'{foggy.m_name}': the Fog stealth icon follows the weather, not the fog on screen (with the mod: {foggyOn}, fog on screen {Kit.F(foggyOnFog)}; without: {foggyOff}, fog on screen {Kit.F(foggyOffFog)})");

            Kit.SwitchOn();
            var up = new Kit.Box();
            yield return Kit.WaitTerrain(up, 30f);
            c.Check(up.Ok && Plugin.Instance.IsActive, "Distant Horizons on again: " + up.Detail);
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report($"Sneak Ambush's Fog icon: clear weather {clearOn} with and without the mod, '{foggy.m_name}' {foggyOn} though the fog on screen went from {Kit.F(foggyOnFog)} to {Kit.F(foggyOffFog)}");
        }
        finally
        {
            Kit.Safe("stand up", () =>
            {
                if (player != null)
                {
                    player.SetCrouch(false);
                }
            });
            weather.Restore();
            Kit.PutBack();
        }
    }

    // ---------- horizons.mods.community-patch (C05)

    // Log lines of anybody that carry a text (self test lines never count: they quote the word).
    private sealed class LineWatch : ILogListener
    {
        private readonly string _text;
        private readonly object _gate = new object();
        private int _count;
        private string _first = "";

        internal LineWatch(string text) => _text = text;

        internal int Count
        {
            get
            {
                lock (_gate)
                {
                    return _count;
                }
            }
        }

        internal string First
        {
            get
            {
                lock (_gate)
                {
                    return _first;
                }
            }
        }

        public void LogEvent(object sender, LogEventArgs e)
        {
            var text = e != null && e.Data != null ? e.Data.ToString() : "";
            if (text.IndexOf(_text, StringComparison.Ordinal) < 0 || text.IndexOf(SelfTest.Prefix, StringComparison.Ordinal) >= 0)
            {
                return;
            }
            lock (_gate)
            {
                _count++;
                if (_first.Length == 0)
                {
                    _first = text.Length > 200 ? text.Substring(0, 200) : text;
                }
            }
        }

        public void Dispose()
        {
        }
    }

    // Lookups through the game's own Heightmap.FindHeightmap (what Valheim Community Patch replaces and verifies).
    // farTiles = answers that are a far tile of mine or any distant-LOD heightmap (never real ground).
    private static void Lookups(List<Vector3> points, int from, int count, Transform root, ref int calls, ref int found, ref int farTiles)
    {
        for (var i = 0; i < count && points.Count > 0; i++)
        {
            var h = Heightmap.FindHeightmap(points[(from + i) % points.Count]);
            calls++;
            if (h == null)
            {
                continue;
            }
            found++;
            if (h.IsDistantLod || (root != null && h.transform.IsChildOf(root)))
            {
                farTiles++;
            }
        }
    }

    private static IEnumerator RunCommunityPatch()
    {
        const string N = CommunityPatchName;
        var c = new Kit.Checks(N);
        var watch = new LineWatch(Diverged);
        FieldInfo field = null;
        ConfigEntry<bool> real = null;
        var swapped = false;
        var listening = false;
        try
        {
            var ready = new Kit.Box();
            yield return Ready(c, ready);
            if (!ready.Ok)
            {
                c.Report("");
                yield break;
            }
            if (!Chainloader.PluginInfos.TryGetValue(VcpGuid, out var info) || info.Instance == null)
            {
                c.Check(false, $"Valheim Community Patch is not installed (no loaded plugin {VcpGuid}): nothing was checked");
                c.Report("");
                yield break;
            }
            c.Note($"Valheim Community Patch {info.Metadata.Version} is loaded");
            var type = info.Instance.GetType().Assembly.GetType(VcpLookupType);
            field = type != null ? type.GetField("Verify", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) : null;
            real = field != null ? field.GetValue(null) as ConfigEntry<bool> : null;
            if (!c.Check(real != null && real.Definition.Key == VcpVerifyKey,
                    $"this Valheim Community Patch has the '{VcpVerifyKey}' switch where version 0.31.0 keeps it ({VcpLookupType}.Verify: {(real != null ? real.Definition.Key : "not found")})"))
            {
                c.Report("");
                yield break;
            }
            var lookup = AccessTools.Method(typeof(Heightmap), nameof(Heightmap.FindHeightmap), new[] { typeof(Vector3) });
            var patches = lookup != null ? Harmony.GetPatchInfo(lookup) : null;
            var hooked = false;
            if (patches != null)
            {
                foreach (var prefix in patches.Prefixes)
                {
                    var owner = prefix.PatchMethod != null ? prefix.PatchMethod.DeclaringType : null;
                    hooked |= owner != null && owner.FullName != null && owner.FullName.StartsWith("ValheimCommunityPatch", StringComparison.Ordinal);
                }
            }
            if (!c.Check(hooked, "Valheim Community Patch's lookup patch is on the game's Heightmap.FindHeightmap (without it its verify pass checks nothing)"))
            {
                c.Report("");
                yield break;
            }

            // Verify pass on. Player's config never set: the patch reads its switch object each lookup, me swap that
            // object for a stand-in from a config file that is never saved, and put the real one back in finally.
            if (!real.Value)
            {
                var file = new ConfigFile(Path.Combine(Paths.CachePath, "MC.DistantHorizons.selftest.never-saved.cfg"), false) { SaveOnConfigSet = false };
                var standIn = file.Bind(real.Definition.Section, real.Definition.Key, true, "Self test stand-in. Never saved.");
                field.SetValue(null, standIn);
                swapped = true;
            }
            var live = field.GetValue(null) as ConfigEntry<bool>;
            c.Check(live != null && live.Value, $"'{VcpVerifyKey}' reads on for this test ({(swapped ? "stand-in switch, the player's setting stays off" : "the player's own setting is on")})");
            BepInEx.Logging.Logger.Listeners.Add(watch);
            listening = true;

            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 30f);
            c.Check(settled.Ok, settled.Detail);
            var player = Player.m_localPlayer;
            var at = player.transform.position;
            var root = GameObject.Find("DistantHorizons_LOD");
            var rootT = root != null ? root.transform : null;
            var errors = ErrorWatch.Errors;

            // Control: a far tile far from the loaded zones. Asked as it is: no ground, no DIVERGED. Then put in the
            // game's list of real ground for ONE lookup (same call, nothing else runs between): the verify pass must
            // report it. That is the fault this item guards against, so it also proves the pass really runs.
            Heightmap pick = null;
            var pickAt = Vector3.zero;
            if (root != null)
            {
                foreach (var hm in root.GetComponentsInChildren<Heightmap>(false))
                {
                    var p = hm.transform.position;
                    if (Utils.DistanceXZ(p, at) < 1500f || !hm.IsPointInside(p))
                    {
                        continue;
                    }
                    pick = hm;
                    pickAt = p;
                    break;
                }
            }
            if (!c.Check(pick != null, "a shown far tile more than 1.5 km away exists for the control"))
            {
                c.Report("");
                yield break;
            }
            var plain = Heightmap.FindHeightmap(pickAt);
            c.Check(plain == null && watch.Count == 0,
                $"far tile at ({pickAt.x:0},{pickAt.z:0}) asked as it is: the game finds no ground there and nothing is reported ({watch.Count} DIVERGED line(s): {watch.First})");
            var before = watch.Count;
            var ground = Heightmap.GetAllHeightmaps();
            SelfTest.Note(N, $"control: the next '{Diverged}' error line of Valheim Community Patch is made by this test on purpose (one far tile listed as real ground for one lookup)");
            Heightmap asGround;
            ground.Add(pick);
            try
            {
                asGround = Heightmap.FindHeightmap(pickAt);
            }
            finally
            {
                ground.Remove(pick);
            }
            var control = watch.Count - before;
            if (!c.Check(control == 1 && ReferenceEquals(asGround, pick) && !ground.Contains(pick),
                    $"control: with that far tile listed as real ground for one lookup, Valheim Community Patch reports it ({control} DIVERGED line(s), expected 1): its verify pass really runs"))
            {
                c.Report("");
                yield break;
            }
            var allowed = watch.Count;

            // Near (loaded zones) and far (only far tiles there), all through the verified lookup.
            var points = new List<Vector3>();
            for (var z = -160f; z <= 160f; z += 32f)
            {
                for (var x = -160f; x <= 160f; x += 32f)
                {
                    points.Add(new Vector3(at.x + x, at.y, at.z + z));
                }
            }
            var near = points.Count;
            var radius = Plugin.Cfg.V(Plugin.Cfg.WorldRadius);
            for (var i = 0; i < 16; i++)
            {
                var a = i * Mathf.PI / 8f;
                for (var r = 300f; r <= 9900f; r += 400f)
                {
                    var p = new Vector3(at.x + Mathf.Sin(a) * r, at.y, at.z + Mathf.Cos(a) * r);
                    if (p.x * p.x + p.z * p.z < radius * radius)
                    {
                        points.Add(p);
                    }
                }
            }
            int calls = 0, found = 0, farTiles = 0;
            Lookups(points, 0, points.Count, rootT, ref calls, ref found, ref farTiles);
            c.Check(found > 0, $"the lookups near the player find real ground ({found} of {near} near points; {points.Count - near} far points)");
            c.Check(watch.Count == allowed, $"settled far terrain, {calls} verified lookups: no DIVERGED line ({watch.Count - allowed}: {watch.First})");

            // Far terrain thrown away and built again (console "dh rebuild"), lookups every frame meanwhile.
            var lines = Kit.Console("dh rebuild");
            c.Check(lines != null, "the dh rebuild console command ran: " + Kit.First(lines));
            var start = Kit.Now;
            var idleSince = -1f;
            var cursor = 0;
            var rebuiltFrames = 0;
            var sawWork = false;
            while (Kit.Now - start < 30f)
            {
                Lookups(points, cursor, 48, rootT, ref calls, ref found, ref farTiles);
                cursor += 48;
                rebuiltFrames++;
                var m = LodTerrainManager.Instance;
                var busy = m == null || m.TestPendingBuilds > 0 || m.TestRebuildPending || m.TestSurfaceCount == 0;
                sawWork |= busy;
                if (busy)
                {
                    idleSince = -1f;
                }
                else if (idleSince < 0f)
                {
                    idleSince = Kit.Now;
                }
                else if (Kit.Now - idleSince >= 2f && Kit.Now - start >= 4f)
                {
                    break;
                }
                yield return null;
            }
            c.Check(sawWork && idleSince >= 0f, $"the far terrain was rebuilt meanwhile (work seen: {sawWork}, done after {Kit.Now - start:0.0} s, {rebuiltFrames} frames)");
            rootT = GameObject.Find("DistantHorizons_LOD") != null ? GameObject.Find("DistantHorizons_LOD").transform : null;
            Lookups(points, 0, points.Count, rootT, ref calls, ref found, ref farTiles);
            // The game's own lookups of these seconds (grass, creatures, the player) went through the same pass.
            c.Check(watch.Count == allowed, $"while the far terrain was rebuilt and after, {calls} verified lookups by this test plus the game's own: no DIVERGED line ({watch.Count - allowed}: {watch.First})");
            c.Check(farTiles == 0, $"no lookup ever answered with a far tile or a distant-LOD heightmap ({farTiles} of {found} answers)");
            Kit.FarTilesAsGround(out var tiles, out var listed, out var notDistant);
            c.Check(tiles > 0 && listed == 0 && notDistant == 0, $"no far tile is listed with the game's real ground heightmaps ({listed} of {tiles} listed, {notDistant} not flagged distant)");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report($"Valheim Community Patch verified {calls} lookups (and the game's own) with far terrain settled and rebuilding: no DIVERGED line beside the control");
        }
        finally
        {
            if (listening)
            {
                Kit.Safe("log watch", () => BepInEx.Logging.Logger.Listeners.Remove(watch));
            }
            if (swapped && field != null && real != null)
            {
                Kit.Safe("verify switch", () => field.SetValue(null, real));
            }
            Kit.PutBack();
        }
    }

    // ---------- horizons.mods.config-manager (T15)

    private const BindingFlags AnyMember = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // Field or property of an object of another plugin, by name (base classes too). Null = not there.
    private static object Member(object target, string name)
    {
        if (target == null)
        {
            return null;
        }
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, AnyMember);
            if (f != null)
            {
                return f.GetValue(f.IsStatic ? null : target);
            }
            var p = t.GetProperty(name, AnyMember);
            var getter = p != null ? p.GetGetMethod(true) : null;
            if (getter != null)
            {
                return p.GetValue(getter.IsStatic ? null : target, null);
            }
        }
        return null;
    }

    private static bool SetMember(object target, string name, object value)
    {
        if (target == null)
        {
            return false;
        }
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, AnyMember);
            if (f != null)
            {
                f.SetValue(f.IsStatic ? null : target, value);
                return true;
            }
            var p = t.GetProperty(name, AnyMember);
            var setter = p != null ? p.GetSetMethod(true) : null;
            if (setter != null)
            {
                p.SetValue(setter.IsStatic ? null : target, value, null);
                return true;
            }
        }
        return false;
    }

    // Value of a bool setting the other plugin keeps in a field (null = no such field).
    private static bool? Switch(object target, string field)
    {
        var entry = Member(target, field) as ConfigEntryBase;
        return entry != null && entry.BoxedValue is bool b ? b : (bool?)null;
    }

    private static int AllErrors() => ErrorWatch.Errors + ErrorWatch.OtherErrors + ErrorWatch.KnownForeignErrors;

    private static IEnumerator RunConfigManager()
    {
        const string N = ConfigManagerName;
        var c = new Kit.Checks(N);
        BaseUnityPlugin cm = null;
        PropertyInfo window = null;
        var opened = false;
        object mine = null;
        object wasCollapsed = null;
        object wasSelected = null;
        var selectedSet = false;
        try
        {
            if (Plugin.Instance == null || Plugin.Cfg == null)
            {
                c.Check(false, "Distant Horizons is not loaded");
                c.Report("");
                yield break;
            }
            if (!Chainloader.PluginInfos.TryGetValue(CmGuid, out var info) || info.Instance == null)
            {
                c.Check(false, $"shudnal's Valheim Configuration Manager is not installed (no loaded plugin {CmGuid}): nothing was checked");
                c.Report("");
                yield break;
            }
            cm = info.Instance;
            c.Note($"{info.Metadata.Name} {info.Metadata.Version} is loaded");
            window = cm.GetType().GetProperty("DisplayingWindow", BindingFlags.Instance | BindingFlags.Public);
            if (!c.Check(window != null && window.CanRead && window.CanWrite && window.PropertyType == typeof(bool), "its window can be opened (DisplayingWindow, what its hotkey sets)"))
            {
                c.Report("");
                yield break;
            }
            var errorsBefore = AllErrors();
            var mineBefore = ErrorWatch.Errors;
            if (!(bool)window.GetValue(cm, null))
            {
                window.SetValue(cm, true, null);
                opened = true;
            }
            yield return Kit.Frames(3);
            c.Check((bool)window.GetValue(cm, null), "the Configuration Manager window is open");
            // Opening makes it read the settings of every installed plugin: errors of that moment may be anybody's.
            if (AllErrors() != errorsBefore)
            {
                c.Note($"{AllErrors() - errorsBefore} error line(s) while the window opened and read every plugin's settings (last of others: {ErrorWatch.LastOtherError})");
            }
            var searching = Member(cm, "IsSearching") as bool?;
            var keybindsOnly = Switch(cm, "_showKeybinds");
            var sortByName = Switch(cm, "_sortCategoriesByName");
            var plugins = Member(cm, "_filteredSetings") as IEnumerable;
            if (!c.Check(plugins != null, "its list of plugins and settings can be read (_filteredSetings of version 1.1.22)"))
            {
                c.Report("");
                yield break;
            }
            var listed = 0;
            foreach (var p in plugins)
            {
                listed++;
                var meta = Member(p, "Info") as BepInPlugin;
                if (meta != null && meta.GUID == ModInfo.Guid)
                {
                    mine = p;
                }
            }
            if (!c.Check(mine != null,
                    $"the window lists Distant Horizons ({listed} plugins listed; its search box in use: {searching}, 'keybinds only' filter: {keybindsOnly})"))
            {
                c.Report("");
                yield break;
            }
            // Mine open and picked, like after a click on its name: its settings get drawn.
            wasCollapsed = Member(mine, "Collapsed");
            SetMember(mine, "Collapsed", false);
            wasSelected = Member(cm, "_selectedPlugin");
            selectedSet = SetMember(cm, "_selectedPlugin", ModInfo.Guid);
            var errors = AllErrors();
            yield return Kit.Frames(20);
            var drawErrors = AllErrors() - errors;

            var sections = new List<string>();
            var bySection = new Dictionary<string, List<object>>();
            if (Member(mine, "Categories") is IEnumerable categories)
            {
                foreach (var category in categories)
                {
                    var name = Member(category, "Name") as string ?? "";
                    sections.Add(name);
                    var entries = new List<object>();
                    if (Member(category, "Settings") is IEnumerable settings)
                    {
                        foreach (var s in settings)
                        {
                            entries.Add(s);
                        }
                    }
                    bySection[name] = entries;
                }
            }
            var want = new List<string>();
            foreach (var section in LifeTests.ReadmeOrder)
            {
                want.Add(section[0]);
            }
            if (sortByName == true)
            {
                want.Sort(StringComparer.CurrentCulture);
            }
            c.Check(string.Join("|", sections.ToArray()) == string.Join("|", want.ToArray()),
                $"sections as listed: {string.Join(", ", sections.ToArray())} (expected {string.Join(", ", want.ToArray())}; this Configuration Manager sorts sections by name: {sortByName})");

            var byKey = new Dictionary<string, object>();
            foreach (var section in LifeTests.ReadmeOrder)
            {
                if (!bySection.TryGetValue(section[0], out var entries))
                {
                    continue;
                }
                var have = new List<string>();
                foreach (var e in entries)
                {
                    var key = Member(e, "DispName") as string ?? "";
                    have.Add(key);
                    byKey[section[0] + "/" + key] = e;
                }
                var expected = new List<string>(section);
                expected.RemoveAt(0);
                // Status is read-only: a Configuration Manager set to hide read-only settings leaves it out.
                if (section[0] == "General" && !have.Contains("Status"))
                {
                    expected.Remove("Status");
                    c.Note("this Configuration Manager hides read-only settings: Status is not listed");
                }
                c.Check(string.Join(",", have.ToArray()) == string.Join(",", expected.ToArray()),
                    $"section {section[0]} lists its settings in the README's order (listed: {string.Join(", ", have.ToArray())})");
            }

            foreach (var key in new[] { DHConfig.LodSection + "/TerrainMaterial", DHConfig.LodSection + "/FarTerrainDraw", DHConfig.ObjectsSection + "/ImpostorResolution", DHConfig.ObjectsSection + "/ImpostorShader" })
            {
                var values = byKey.TryGetValue(key, out var e) ? Member(e, "AcceptableValues") as Array : null;
                c.Check(values != null && values.Length >= 2, $"{key} is a drop-down choice in the window ({(values != null ? values.Length : 0)} values)");
            }
            if (byKey.TryGetValue("General/Status", out var status))
            {
                c.Check(Member(status, "CustomDrawer") != null && Member(status, "ReadOnly") as bool? == true,
                    "Status is drawn by the mod's own plain-text drawer and is read-only");
            }
            c.Check(byKey.ContainsKey("General/Enabled"), "General lists Enabled");
            yield return Kit.Shot(N, "distant-horizons-settings");
            c.Check(drawErrors == 0,
                $"with the Distant Horizons list open, 20 frames of the window were drawn without an error line from anybody ({drawErrors}; last of others: {ErrorWatch.LastOtherError})");
            c.Check(ErrorWatch.Errors == mineBefore, $"no error from Distant Horizons from opening the window on ({ErrorWatch.Errors - mineBefore}: {ErrorWatch.FirstError})");
            c.Report($"shudnal's Configuration Manager lists Distant Horizons: {sections.Count} sections, {byKey.Count} settings, README order");
        }
        finally
        {
            if (mine != null && wasCollapsed != null)
            {
                Kit.Safe("plugin row", () => SetMember(mine, "Collapsed", wasCollapsed));
            }
            if (cm != null && selectedSet)
            {
                Kit.Safe("picked plugin", () => SetMember(cm, "_selectedPlugin", wasSelected));
            }
            if (opened && cm != null && window != null)
            {
                Kit.Safe("window", () => window.SetValue(cm, false, null));
            }
        }
    }
}
#endif
