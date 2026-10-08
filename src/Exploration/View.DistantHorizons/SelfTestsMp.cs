#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1, scenario "modded"): the client with this mod
// joined to a real dedicated server. The mod is client-only (it never loads in the dedicated server program), so for
// it every dedicated server is "a server without the mod".
//   horizons.mp.server    the server has no copy of this mod (it knows no step of mine); the mod is active, far
//                         terrain whole, far sea painted; far objects only come from zones the server sent for the
//                         places this player has been: fresh join = spawn area only, flown 900 m away = the spawn
//                         area is drawn as far objects, still nothing beyond; nothing of mine is a network object;
//                         no object in the world is created from this mod's code (ObjectWatch below: who asked for
//                         each object this game made, by call stack. NOT a count of this player's objects: the one
//                         player on a dedicated server owns the zones, so the game's own spawner makes its deer,
//                         fish and gulls in this game, 2 to 4 of them in the runs); no error
//   horizons.mp.console   real settings (the run's config files are throwaway): "dh set FogDensityMultiplier 1" =
//                         normal fog at once and 1 in the file, back; "dh off" = mod off, message says how to turn
//                         it on, Status "Off...", "dh" unknown, vanilla distant terrain; Enabled = true written in the
//                         config FILE = active again without restart; Enabled = false in the file = off, Status
//                         "Off..."; Enabled set like ConfigurationManager does = active
// No server half can exist (the mod is not there): the server stands in only by what it sends.
internal static class MpTests
{
    private const string ServerName = "horizons.mp.server";
    private const string ConsoleName = "horizons.mp.console";
    // A copy of this mod on the server would answer this step. No copy = the server probe says it has no such step.
    private const string ServerStep = "horizons.mp.on-server";

    internal static void Register()
    {
        SelfTest.RegisterMultiplayer(ServerName, SelfTest.Modded, RunServer);
        SelfTest.RegisterMultiplayer(ConsoleName, SelfTest.Modded, RunConsole);
        SelfTest.RegisterServerStep(ServerStep, OnServer);
    }

    internal static void Unregister()
    {
        SelfTest.UnregisterMultiplayer(ServerName);
        SelfTest.UnregisterMultiplayer(ConsoleName);
        SelfTest.UnregisterServerStep(ServerStep);
    }

    private static IEnumerator OnServer(string arg, object[] reply)
    {
        yield return null;
        SelfTest.Answer(reply, true, $"{ModInfo.Guid} {ModInfo.Version} runs on the server");
    }

    private static bool Client => ZNet.instance != null && !ZNet.instance.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;

    // Lasting objects of this player's own session (what this game created in the world, by the game or by a mod).
    private static void OwnObjects(HashSet<ZDOID> into)
    {
        into.Clear();
        var session = ZDOMan.GetSessionID();
        foreach (var kv in ZDOMan.instance.m_objectsByID)
        {
            if (kv.Key.UserID == session && kv.Value != null && kv.Value.Persistent)
            {
                into.Add(kv.Key);
            }
        }
    }

    // Me make one throwaway object from test code (code of this mod's DLL): no prefab, not lasting, destroyed in the
    // same call, never sent (a client only sends objects whose data changed). The watch must see it and must find
    // this DLL on its call stack, or its "nothing came from the mod" would mean nothing.
    private static bool WatchSeesThisDll(Vector3 at)
    {
        ObjectWatch.Calibrating = true;
        try
        {
            var zdo = ZDOMan.instance.CreateNewZDO(at, 0);
            if (zdo != null)
            {
                ZDOMan.instance.DestroyZDO(zdo);
            }
        }
        finally
        {
            ObjectWatch.Calibrating = false;
        }
        foreach (var made in ObjectWatch.Seen)
        {
            if (made.Calibration)
            {
                return made.ModFrame.Length > 0;
            }
        }
        return false;
    }

    // ---------- horizons.mp.server

    private static IEnumerator RunServer()
    {
        const string N = ServerName;
        var c = new Kit.Checks(N);
        Kit.Flight flight = null;
        try
        {
            var player = Player.m_localPlayer;
            if (!c.Check(Client && player != null, "joined a server as a client, with a local player"))
            {
                c.Report("");
                yield break;
            }
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(ServerStep, "", reply);
            c.Check(reply.Answered && !reply.Ok && reply.Detail.IndexOf("has no step", StringComparison.OrdinalIgnoreCase) >= 0,
                $"the dedicated server runs no copy of Distant Horizons (its answer: {reply})");
            c.Check(Plugin.Instance != null && Plugin.Instance.IsActive && Plugin.Instance.StatusText == "Active.",
                $"on a server without the mod it is active ('{(Plugin.Instance != null ? Plugin.Instance.StatusText : "no plugin")}')");
            var up = new Kit.Box();
            yield return Kit.WaitTerrain(up);
            if (!c.Check(up.Ok, up.Detail))
            {
                c.Report("");
                yield break;
            }
            var cfg = Plugin.Cfg;
            Kit.Force(cfg.FarWater, true);
            Kit.Force(cfg.ObjectsEnabled, true);
            Kit.Force(cfg.DrawTrees, true);
            var errors = ErrorWatch.Errors;
            // From here: who asks for every object this game creates in the world.
            var own = new HashSet<ZDOID>();
            OwnObjects(own);
            var watching = ObjectWatch.Start();
            var watchSees = watching && WatchSeesThisDll(player.transform.position);
            c.Check(watchSees, "the test can tell who creates an object in the world: a throwaway object made by the test itself is seen, with this mod's DLL on its call stack"
                               + (ObjectWatch.Error.Length > 0 ? $" ({ObjectWatch.Error})" : ""));
            var settled = new Kit.Box();
            yield return Kit.Settle(settled, 45f, objects: true);
            var mgr = LodTerrainManager.Instance;
            var om = DistantObjectManager.Instance;
            if (!c.Check(settled.Ok && mgr != null && om != null, settled.Detail))
            {
                c.Report("");
                yield break;
            }
            var cam = Utils.GetMainCamera();
            c.Note(mgr.GetStats());
            c.Note(om.GetStats());

            // Far terrain and sea.
            Kit.Coverage(mgr, cam.transform.position.x, cam.transform.position.z, 6000f, 128f, out var points, out var holes, out _, out var firstHole);
            mgr.TestSurfaceCut(out var overlaps, out var notShown);
            c.Check(points > 500 && holes == 0 && overlaps == 0 && notShown == 0,
                $"far terrain works: far ground under every point within 6 km ({holes} of {points} without, first {firstHole}), nothing twice ({overlaps}), all shown ({notShown} not)");
            if (c.Check(Kit.Shrink, "FarTerrainDraw is 'shrink' (default) in the run's config"))
            {
                var sea = new Kit.Box();
                yield return Kit.WaitUntil(() => mgr.SeaPainting && mgr.TestSeaDraws == mgr.Water.BandCount, 15f, sea);
                Kit.LodPlanes(out var planes, out var hidden, out _, out _);
                c.Check(sea.Ok && hidden == planes, $"far sea works: {mgr.TestSeaDraws} bands painted, the game's distant water plane out of reach ({hidden} of {planes})");
            }

            // Far objects come from the objects the server sent this game: around the places this player has been.
            var spawn = player.transform.position;
            var sim = ZNet.instance.GetSyncedSimulationDistance();
            var sent = (sim.TotalSimulationDistance + 2) * 64f + 96f;
            var known = new HashSet<Vector2>();
            var list = new List<Vector2>();
            om.TestFarZoneCoords(list);
            foreach (var z in list)
            {
                known.Add(z);
            }
            om.TestFarZones(spawn.x, spawn.z, out var zones, out var farthest);
            c.Note($"before leaving (simulation near {sim.NearSimulationDistance} + far {sim.FarSimulationDistance}): far objects drawn for {zones} zones, the farthest {farthest:0} m from the player; "
                   + $"the server sends objects up to about {sent:0} m around a player" + (farthest > sent ? " (earlier tests of this run took this player elsewhere)" : ""));

            // Leave: the area the player has been at is drawn as far objects, and nothing new beyond what was sent on the way.
            flight = new Kit.Flight(player);
            var origin = flight.Origin;
            var radius = cfg.V(cfg.WorldRadius);
            var dir = (origin + Vector3.right * 900f).magnitude < radius - 600f ? Vector3.right : Vector3.left;
            var wg = WorldGenerator.instance;
            var water = ZoneSystem.instance.m_waterLevel;
            const float Away = 900f;
            const float Speed = 70f;
            for (var leg = 0; leg < 2; leg++)
            {
                var t0 = Kit.Now;
                while (true)
                {
                    var d = Mathf.Min(Away, (Kit.Now - t0) * Speed);
                    var p = origin + dir * (leg == 0 ? d : Away - d);
                    p.y = Mathf.Max(wg.GetHeight(p.x, p.z), water) + 80f;
                    flight.Put(p);
                    if (d >= Away)
                    {
                        break;
                    }
                    yield return null;
                }
                if (leg == 1)
                {
                    break;
                }
                // Hovering 900 m away.
                yield return new WaitForSecondsRealtime(1f);
                yield return ObjectTests.ObjectsIdle(25f);
                var here = om.TestFarZonesNear(origin.x, origin.z, 160f);
                list.Clear();
                om.TestFarZoneCoords(list);
                var fresh = 0;
                var outside = 0;
                var worst = 0f;
                foreach (var z in list)
                {
                    if (known.Contains(z))
                    {
                        continue;
                    }
                    fresh++;
                    // Distance of the zone centre to the way flown (a straight line from the start).
                    var rel = new Vector3(z.x * 64f - origin.x, 0f, z.y * 64f - origin.z);
                    var along = Mathf.Clamp(Vector3.Dot(rel, dir), 0f, Away);
                    var off = (rel - dir * along).magnitude;
                    if (off > worst)
                    {
                        worst = off;
                    }
                    if (off > sent)
                    {
                        outside++;
                    }
                }
                c.Note($"900 m away: far objects drawn for {list.Count} zones, {here} of them within 160 m of where the player stood, {fresh} zones new since leaving, the farthest of those {worst:0} m from the way flown");
                c.Check(here > 0, $"900 m away: the area the player has been at is drawn as far objects ({here} zones within 160 m of the start)");
                c.Check(outside == 0, $"900 m away: far objects appeared only around the way flown ({outside} of {fresh} new zones lie farther than {sent:0} m from it, worst {worst:0} m)");
                yield return Kit.Shot(N, "900m-away");
            }
            var loaded = new Kit.Box();
            yield return Kit.WaitUntil(() => ZoneSystem.instance.IsActiveAreaLoaded() && ZNetScene.instance.IsAreaReady(origin), 30f, loaded);
            c.Check(loaded.Ok, $"back at the start, the zones are loaded again ({loaded.Detail})");
            if (loaded.Ok)
            {
                flight.Land();
            }

            c.Check(Kit.NetworkedUnderRoots() == 0, $"nothing the mod draws is a network object ({Kit.NetworkedUnderRoots()} ZNetView under its objects)");
            c.Check(!Kit.Patched(typeof(ZNet), nameof(ZNet.OnNewConnection), ModInfo.Guid + ".framework"), "the mod hooks no connection handshake: it tells the server nothing");
            // Objects made meanwhile: none asked for by this mod's code, and every new lasting object of this player
            // was seen being made (so its maker is known: the game's spawner for the zones this player owns, or another mod).
            var now = new HashSet<ZDOID>();
            OwnObjects(now);
            ObjectWatch.Judge(own, now, out var made, out var byMod, out var untraced, out var who);
            ObjectWatch.Stop();
            c.Note($"objects this game created in the world meanwhile: {made} ({who}); lasting objects of this player {now.Count}, before {own.Count}");
            c.Check(watchSees && ObjectWatch.Error.Length == 0 && byMod == 0 && untraced == 0,
                $"no object in the world was created from the mod's code: of {made} object(s) this game created meanwhile ({who}), {byMod} had the mod's DLL on the call stack, "
                + $"and {untraced} new lasting object(s) of this player were not seen being made"
                + (ObjectWatch.Error.Length > 0 ? $" (watch error: {ObjectWatch.Error})" : "") + (watchSees ? "" : " (the watch does not work: nothing can be said)"));
            c.Check(Client, "still connected");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report("active on a server without it: far terrain and sea work, far objects only where this player has been");
        }
        finally
        {
            ObjectWatch.Stop();
            if (flight != null)
            {
                flight.End();
            }
            Kit.PutBack();
        }
    }

    // ---------- horizons.mp.console

    // General/Enabled line of a BepInEx config text set to value.
    private static string WithEnabled(string text, bool value, out bool found)
    {
        found = false;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var section = "";
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line.Substring(1, line.Length - 2);
                continue;
            }
            if (section == "General" && line.StartsWith("Enabled", StringComparison.Ordinal) && line.IndexOf('=') > 0
                && line.Substring(0, line.IndexOf('=')).Trim() == "Enabled")
            {
                lines[i] = "Enabled = " + (value ? "true" : "false");
                found = true;
            }
        }
        return string.Join(Environment.NewLine, lines);
    }

    // Value of a setting line in the config file text ("" = not there).
    private static string FileValue(string path, string section, string key)
    {
        var current = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                current = line.Substring(1, line.Length - 2);
                continue;
            }
            var eq = line.IndexOf('=');
            if (current == section && eq > 0 && line.Substring(0, eq).Trim() == key)
            {
                return line.Substring(eq + 1).Trim();
            }
        }
        return "";
    }

    private static void OffChecks(Kit.Checks c, string how, TerrainLod tl, Camera cam)
    {
        var p = Plugin.Instance;
        c.Check(!p.IsActive && p.State == ModState.Disabled && p.StatusText.StartsWith("Off", StringComparison.Ordinal), $"{how}: the mod is off and Status says Off ('{p.StatusText}')");
        c.Check(LodTerrainManager.Instance == null && DistantObjectManager.Instance == null && Kit.LivePatches() == 0 && Kit.Buffers(cam, null) == 0,
            $"{how}: no manager, no live patch ({Kit.LivePatches()} of 5), no buffer on the camera ({Kit.Buffers(cam, null)})");
        LodTerrainManager.TestRealZoneLeftovers(out var zones, out var sunk, out var forcedOff, out _);
        Kit.LodPlanes(out var planes, out var hidden, out _, out _);
        c.Check(sunk == 0 && forcedOff == 0 && hidden == 0, $"{how}: real zones and the game's water plane are left as the game draws them ({sunk} of {zones} zones depth-only, {hidden} of {planes} planes out of reach)");
        c.Check(!Terminal.commands.ContainsKey("dh"), $"{how}: the dh console command is gone");
    }

    private static IEnumerator RunConsole()
    {
        const string N = ConsoleName;
        var c = new Kit.Checks(N);
        var weather = new Kit.Weather();
        var cfg = Plugin.Cfg;
        var multiplier = cfg.FogDensityMultiplier.Value;
        try
        {
            if (!c.Check(Client && Player.m_localPlayer != null && Plugin.Instance != null && Plugin.Instance.IsActive,
                    "joined a server as a client, Distant Horizons active"))
            {
                c.Report("");
                yield break;
            }
            var up = new Kit.Box();
            yield return Kit.WaitTerrain(up);
            if (!c.Check(up.Ok, up.Detail))
            {
                c.Report("");
                yield break;
            }
            var cam = Utils.GetMainCamera();
            var tl = TerrainLink.Current;
            var path = cfg.File.ConfigFilePath;
            var errors = ErrorWatch.Errors;
            var box = new Kit.Box();
            weather.Force("Clear", 0.5f);
            yield return Kit.WaitWeather("Clear", box);
            if (!c.Check(box.Ok, box.Detail))
            {
                c.Report("");
                yield break;
            }
            var clear = EnvMan.instance.GetEnv("Clear");
            var raw = clear.m_fogDensityDay;

            // dh set: a real change of a real setting.
            var lines = Kit.Console("dh set FogDensityMultiplier 0.25");
            yield return Kit.WaitFixed(2);
            c.Check(Kit.Near(RenderSettings.fogDensity, Kit.ExpectedFog(clear, raw)) && RenderSettings.fogDensity < raw,
                $"FogDensityMultiplier 0.25: clear-weather fog is thinned ({Kit.F(RenderSettings.fogDensity)}, weather {Kit.F(raw)})");
            lines = Kit.Console("dh set FogDensityMultiplier 1");
            c.Check(Kit.AnyStartsWith(lines, "DistantHorizons: FogDensityMultiplier = 1") && cfg.FogDensityMultiplier.Value == 1f,
                $"dh set FogDensityMultiplier 1 answers with the new value ({Kit.First(lines)})");
            c.Check(FileValue(path, DHConfig.RenderingSection, "FogDensityMultiplier") == "1", $"…and the config file shows 1 ('{FileValue(path, DHConfig.RenderingSection, "FogDensityMultiplier")}')");
            yield return Kit.WaitFixed(2);
            c.Check(Kit.Near(RenderSettings.fogDensity, raw), $"…and the fog is thicker at once: the game's normal fog ({Kit.F(RenderSettings.fogDensity)}, weather {Kit.F(raw)})");
            lines = Kit.Console("dh set FogDensityMultiplier 0.25");
            yield return Kit.WaitFixed(2);
            c.Check(cfg.FogDensityMultiplier.Value == 0.25f && FileValue(path, DHConfig.RenderingSection, "FogDensityMultiplier") == "0.25" && RenderSettings.fogDensity < raw,
                $"dh set FogDensityMultiplier 0.25: thin again, 0.25 in the file ({Kit.First(lines)})");

            // dh off.
            lines = Kit.Console("dh off");
            c.Check(Kit.AnyContains(lines, "OFF (vanilla distant terrain restored)") && Kit.AnyContains(lines, "MC Mods panel") && Kit.AnyContains(lines, "General.Enabled"),
                $"dh off says the mod is off and how to turn it back on ({Kit.First(lines)})");
            c.Check(!Plugin.Instance.Enabled.Value, "dh off switched General/Enabled off");
            OffChecks(c, "dh off", tl, cam);
            c.Check(Kit.AnyContains(Kit.Console("dh"), "is not a recognized command"), "after dh off, dh is an unknown command");
            c.Check(FileValue(path, "General", "Enabled") == "false" && FileValue(path, "General", "Status").StartsWith("Off", StringComparison.Ordinal),
                $"the config file shows Enabled = false and Status = Off… ('{FileValue(path, "General", "Status")}')");
            yield return Kit.Frames(2);
            c.Check(Kit.Roots("DistantHorizons_LOD") == 0 && Kit.Roots("DistantHorizons_Objects") == 0, "after dh off no far tile or far object is left in the scene");
            var meshes = new Kit.Box();
            yield return Kit.WaitUntil(() => tl != null && tl.m_heightmaps.Count > 0 && Kit.VanillaMeshes(tl) == tl.m_heightmaps.Count, 20f, meshes);
            c.Check(meshes.Ok, "after dh off the vanilla distant terrain has its meshes within 20 s");
            yield return Kit.WaitFixed(2);
            c.Check(Kit.Near(RenderSettings.fogDensity, raw), $"after dh off the fog is the game's own ({Kit.F(RenderSettings.fogDensity)})");

            // Back on by editing the config file, like a player with a text editor.
            File.WriteAllText(path, WithEnabled(File.ReadAllText(path), true, out var found));
            c.Check(found, "the config file has a General/Enabled line to edit");
            yield return Kit.WaitUntil(() => Plugin.Instance.IsActive, 10f, box);
            c.Check(box.Ok && Plugin.Instance.Enabled.Value && Plugin.Instance.StatusText == "Active.", $"Enabled = true written in the config file: active again without a restart ({box.Detail}, '{Plugin.Instance.StatusText}')");
            yield return Kit.WaitTerrain(up, 60f);
            c.Check(up.Ok && Terminal.commands.ContainsKey("dh") && Kit.LivePatches() == 5, "…the far terrain, the dh command and the patches are back: " + up.Detail);

            // Off by editing the file.
            File.WriteAllText(path, WithEnabled(File.ReadAllText(path), false, out found));
            yield return Kit.WaitUntil(() => !Plugin.Instance.IsActive, 10f, box);
            c.Check(found && box.Ok, $"Enabled = false written in the config file: the mod turns off while in the world ({box.Detail})");
            OffChecks(c, "Enabled = false in the file", tl, cam);
            yield return Kit.WaitUntil(() => tl != null && tl.m_heightmaps.Count > 0 && Kit.VanillaMeshes(tl) == tl.m_heightmaps.Count, 20f, meshes);
            c.Check(meshes.Ok, "Enabled = false in the file: the vanilla distant terrain has its meshes within 20 s");

            // On the way ConfigurationManager does it: the setting itself.
            Plugin.Instance.Enabled.Value = true;
            c.Check(Plugin.Instance.IsActive && Plugin.Instance.StatusText == "Active.", $"Enabled set to true (as ConfigurationManager does): active at once ('{Plugin.Instance.StatusText}')");
            yield return Kit.WaitTerrain(up, 60f);
            c.Check(up.Ok, "…and the far terrain is back: " + up.Detail);
            c.Check(Client, "still connected to the server after all this");
            c.Check(ErrorWatch.Errors == errors, $"no error from Distant Horizons ({ErrorWatch.Errors - errors}: {ErrorWatch.FirstError})");
            c.Report("dh set, dh off, and Enabled through the config file and through the setting all work live");
        }
        finally
        {
            Kit.Safe("enabled", () =>
            {
                if (Plugin.Instance != null && !Plugin.Instance.Enabled.Value)
                {
                    Plugin.Instance.Enabled.Value = true;
                }
            });
            Kit.Safe("fog setting", () =>
            {
                if (cfg.FogDensityMultiplier.Value != multiplier)
                {
                    cfg.FogDensityMultiplier.Value = multiplier;
                }
            });
            weather.Restore();
            Kit.PutBack();
        }
    }
}

// Debug build only. Me tell WHO asked for an object this game creates in the world while a test runs. An object a game
// makes itself gets its ZDO from ZDOMan.CreateNewZDO(position, prefab) (ZNetView.Awake calls it; objects of other
// players and of the server come in by another door). For the time of one test me hang a postfix on it (own Harmony
// id, taken off again; not a patch of the mod) and read the call stack: a frame of this mod's DLL on it = code of the
// mod asked for the object. A count of this player's objects can not tell that: the game's SpawnSystem runs in the
// game that owns the zone, and next to a dedicated server that is the one player.
internal static class ObjectWatch
{
    internal sealed class Made
    {
        internal ZDOID Id;
        internal int Prefab;
        internal bool Calibration;
        // First frame of this mod's DLL on the stack ("" = none).
        internal string ModFrame = "";
        // First game or other-mod frame that is not ZDO / ZNetView / Unity plumbing.
        internal string Caller = "";
    }

    private static readonly string Owner = ModInfo.Guid + ".selftest.objectwatch";
    private static readonly Assembly Mine = typeof(ObjectWatch).Assembly;
    private static Harmony _harmony;
    private static MethodBase _target;
    private static MethodInfo _postfix;

    internal static readonly List<Made> Seen = new List<Made>();
    internal static bool Calibrating;
    internal static string Error = "";

    internal static bool Start()
    {
        Stop();
        Seen.Clear();
        Error = "";
        Calibrating = false;
        try
        {
            if (ZDOMan.instance == null)
            {
                Error = "no ZDOMan";
                return false;
            }
            // Compiler pick the overload the game's ZNetView.Awake calls.
            Func<Vector3, int, ZDO> create = ZDOMan.instance.CreateNewZDO;
            _target = create.Method;
            _postfix = AccessTools.Method(typeof(ObjectWatch), nameof(After));
            if (_target == null || _postfix == null)
            {
                Error = "ZDOMan.CreateNewZDO not found";
                return false;
            }
            _harmony = new Harmony(Owner);
            _harmony.Patch(_target, postfix: new HarmonyMethod(_postfix));
            return true;
        }
        catch (Exception e)
        {
            Error = "watch not put on: " + e.Message;
            _harmony = null;
            return false;
        }
    }

    internal static void Stop()
    {
        if (_harmony == null)
        {
            return;
        }
        try
        {
            _harmony.Unpatch(_target, _postfix);
        }
        catch (Exception e)
        {
            Error = "watch not taken off: " + e.Message;
        }
        _harmony = null;
    }

    // __1 = prefab hash (second argument, by place: no name of the game needed).
    private static void After(ZDO __result, int __1)
    {
        try
        {
            if (__result == null)
            {
                return;
            }
            var made = new Made { Id = __result.m_uid, Prefab = __1, Calibration = Calibrating };
            var trace = new System.Diagnostics.StackTrace(false);
            for (var i = 0; i < trace.FrameCount; i++)
            {
                var frame = trace.GetFrame(i);
                var method = frame != null ? frame.GetMethod() : null;
                var type = method != null ? method.DeclaringType : null;
                // No type = wrapper of a patched method. Me = this watch.
                if (type == null || type == typeof(ObjectWatch))
                {
                    continue;
                }
                if (type.Assembly == Mine)
                {
                    if (made.ModFrame.Length == 0)
                    {
                        made.ModFrame = type.FullName + "." + method.Name;
                    }
                    continue;
                }
                if (made.Caller.Length == 0 && type != typeof(ZDOMan) && type != typeof(ZNetView)
                    && (type.Namespace == null || !type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal)))
                {
                    made.Caller = type.Name + "." + method.Name;
                }
            }
            Seen.Add(made);
        }
        catch (Exception e)
        {
            Error = "watch failed: " + e.Message;
        }
    }

    // made = objects seen being made (the test's own throwaway one not counted); byMod = of those, with this mod's DLL
    // on the stack (lasting or not); untraced = lasting objects of this player that are new (in now, not in before)
    // and were not seen being made; who = "2 x Deer by SpawnSystem.Spawn; ...".
    internal static void Judge(HashSet<ZDOID> before, HashSet<ZDOID> now, out int made, out int byMod, out int untraced, out string who)
    {
        made = 0;
        byMod = 0;
        var seen = new HashSet<ZDOID>();
        var groups = new Dictionary<string, int>();
        var order = new List<string>();
        foreach (var m in Seen)
        {
            seen.Add(m.Id);
            if (m.Calibration)
            {
                continue;
            }
            made++;
            var prefab = ZNetScene.instance != null && m.Prefab != 0 ? ZNetScene.instance.GetPrefab(m.Prefab) : null;
            var name = prefab != null ? prefab.name : "prefab " + m.Prefab;
            string key;
            if (m.ModFrame.Length > 0)
            {
                byMod++;
                key = $"{name} BY THE MOD ({m.ModFrame})";
            }
            else
            {
                key = $"{name} by {(m.Caller.Length > 0 ? m.Caller : "an unknown caller")}";
            }
            if (!now.Contains(m.Id))
            {
                key += ", not lasting or gone again";
            }
            if (groups.TryGetValue(key, out var n))
            {
                groups[key] = n + 1;
            }
            else
            {
                groups[key] = 1;
                order.Add(key);
            }
        }
        untraced = 0;
        foreach (var id in now)
        {
            if (!before.Contains(id) && !seen.Contains(id))
            {
                untraced++;
            }
        }
        var parts = new List<string>();
        foreach (var key in order)
        {
            parts.Add($"{groups[key]} x {key}");
        }
        who = parts.Count > 0 ? string.Join("; ", parts.ToArray()) : "none";
    }
}
#endif
