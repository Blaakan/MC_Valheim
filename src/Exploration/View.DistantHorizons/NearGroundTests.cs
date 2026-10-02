#if DEBUG
using System;
using System.Collections;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Debug build only. In-world self test horizons.near (world probe, tools/Test-InWorld.ps1): ground right next to
// player. Me walk to open Meadows sea shore near spawn (just above sea level, where far tiles, real zones and shore
// bands all meet), evening sun, clear sky, then:
//   - check no far-tile vertex within 120 m sit above real ground (would poke through it);
//   - NOTE settings that shape near ground (simulation distance, shadows, hide distances) and how each real zone
//     there get drawn (game or painted copy, shrink factor);
//   - screenshots of same views in every draw mode (NearGroundCapture), then with mod off.
// Me put back camera, weather, time, player place; mod off view turn mod back on after.
internal static class NearGroundTests
{
    private const string Name = "horizons.near";
    private const float ProbeRadius = 120f;
    private const float SettleTimeout = 45f;
    private const float EveningTime = 0.65f;

    internal static void Register() => SelfTest.Register(Name, Run);

    internal static void Unregister() => SelfTest.Unregister(Name);

    private struct View
    {
        public string Label;
        public Vector3 Pos;
        public Vector3 Target;
    }

    private static IEnumerator Run()
    {
        var player = Player.m_localPlayer;
        var cam = Utils.GetMainCamera();
        if (player == null || cam == null || GameCamera.instance == null)
        {
            SelfTest.Fail(Name, "no local player or camera");
            yield break;
        }
        var origin = player.transform.position;
        var originRot = player.transform.rotation;
        var env = EnvMan.instance;
        var envDebug = env.m_debugEnv;
        var todOn = env.m_debugTimeOfDay;
        var tod = env.m_debugTime;
        var ok = true;
        try
        {
            if (!FindShore(origin, out var land, out var toWater, out var kind))
            {
                SelfTest.Note(Name, "no Meadows shore within 2 km of spawn: nothing to look at");
                SelfTest.Pass(Name, "skipped (no shore near spawn in this world)");
                yield break;
            }
            SelfTest.Note(Name, $"{kind} at ({land.x:0},{land.y:0.0},{land.z:0}), water towards ({toWater.x:0.00},{toWater.z:0.00})");

            var moved = new Box();
            yield return TeleportAndWait(player, land + Vector3.up, 40f, moved);
            if (!moved.Ok)
            {
                SelfTest.Fail(Name, "teleport to the shore did not finish within 40 s");
                yield break;
            }
            env.m_debugTimeOfDay = true;
            env.m_debugTime = EveningTime;
            env.m_debugEnv = "Clear";
            env.ForceInstantEnvironmentSwitch();
            var settled = new Box();
            yield return Settle(settled);
            SelfTest.Note(Name, settled.Detail);

            var mgr = LodTerrainManager.Instance;
            if (mgr == null)
            {
                SelfTest.Fail(Name, "no terrain manager after the teleport");
                yield break;
            }
            var ground = land;
            if (ZoneSystem.instance.GetGroundHeight(land, out var gh))
            {
                ground.y = gh;
            }
            SelfTest.Note(Name, NearGroundCapture.DescribeSettings(ground));

            // Camera on the land side, looking over the player to the water (like a third-person view at a shore).
            var side = new Vector3(-toWater.z, 0f, toWater.x);
            var views = new[]
            {
                new View { Label = "behind", Pos = ground - toWater * 6f + Vector3.up * 9f, Target = ground + toWater * 10f },
                new View { Label = "side", Pos = ground - toWater * 4f - side * 10f + Vector3.up * 8f, Target = ground + toWater * 6f },
                new View { Label = "high", Pos = ground - toWater * 10f + Vector3.up * 22f, Target = ground + toWater * 14f },
            };

            GameCamera.instance.enabled = false;
            foreach (var v in views)
            {
                cam.transform.SetPositionAndRotation(v.Pos, Quaternion.LookRotation(v.Target - v.Pos));
                yield return Frames(3);
                var probe = mgr.ProbeNearGround(v.Pos, ground, ProbeRadius, out var poking, out var mismatched);
                SelfTest.Note(Name, $"{v.Label}: {probe}");
                ok &= Check(poking == 0, $"{v.Label}: {poking} far-tile vertices above the real ground within {ProbeRadius:0} m");
                ok &= Check(mismatched == 0, $"{v.Label}: {mismatched} painted real zones whose depth renderer still tessellates");
                // Hardest case too: every zone painted, also the one under the camera.
                mgr.PaintAllRealZones = true;
                yield return Frames(3);
                mgr.ProbeNearGround(v.Pos, ground, ProbeRadius, out _, out var mismatchedAll);
                mgr.PaintAllRealZones = false;
                ok &= Check(mismatchedAll == 0, $"{v.Label}, every zone painted: {mismatchedAll} depth renderers still tessellate");
                var label = v.Label;
                yield return NearGroundCapture.Modes(mgr, mode => SelfTest.Screenshot(Name, label + "_" + mode));
            }

            // Mod off (same code the toggle run), same views, then back on.
            TerrainLink.Detach();
            yield return new WaitForSeconds(3f);
            foreach (var v in views)
            {
                cam.transform.SetPositionAndRotation(v.Pos, Quaternion.LookRotation(v.Target - v.Pos));
                yield return Frames(3);
                SelfTest.Screenshot(Name, v.Label + "_9-mod-off");
                yield return Frames(2);
            }
            TerrainLink.AttachIfInWorld();
            yield return Frames(2);

            GameCamera.instance.enabled = true;
            var back = new Box();
            yield return TeleportAndWait(player, origin, 40f, back);
            if (ok)
            {
                SelfTest.Pass(Name, "no far tile pokes through the real ground next to the player; screenshots taken");
            }
        }
        finally
        {
            Safe(() =>
            {
                var m = LodTerrainManager.Instance;
                if (m != null) m.PaintAllRealZones = false;
            });
            Safe(() =>
            {
                if (GameCamera.instance != null) GameCamera.instance.enabled = true;
            });
            Safe(() =>
            {
                env.m_debugEnv = envDebug;
                env.m_debugTimeOfDay = todOn;
                env.m_debugTime = tod;
                env.ForceInstantEnvironmentSwitch();
            });
            Safe(() =>
            {
                if (LodTerrainManager.Instance == null && Plugin.Instance != null && Plugin.Instance.IsActive)
                {
                    TerrainLink.AttachIfInWorld();
                }
            });
            Safe(() =>
            {
                if (player != null && Flat(player.transform.position - origin).magnitude > 30f && !player.IsTeleporting())
                {
                    player.m_teleportCooldown = 10f;
                    player.TeleportTo(origin, originRot, true);
                }
            });
        }
    }

    // Ground 0.8 to 2.5 m above sea level in the Meadows with water one way and land the other. First choice: open
    // ground (no forest) by the sea (deep 40 m out); then any sea shore; then any water edge (creek, pond).
    private static bool FindShore(Vector3 from, out Vector3 land, out Vector3 toWater, out string kind)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            if (FindShore(from, pass, out land, out toWater))
            {
                kind = pass == 0 ? "open sea shore" : pass == 1 ? "sea shore" : "water edge";
                return true;
            }
        }
        land = Vector3.zero;
        toWater = Vector3.forward;
        kind = "none";
        return false;
    }

    private static bool FindShore(Vector3 from, int pass, out Vector3 land, out Vector3 toWater)
    {
        var wg = WorldGenerator.instance;
        var water = ZoneSystem.instance.m_waterLevel;
        for (var r = 40f; r <= 2000f; r += 20f)
        {
            var steps = Mathf.Max(16, Mathf.RoundToInt(2f * Mathf.PI * r / 20f));
            for (var i = 0; i < steps; i++)
            {
                var a = i * 2f * Mathf.PI / steps;
                var p = from + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                var h = wg.GetHeight(p.x, p.z);
                if (h < water + 0.8f || h > water + 2.5f || wg.GetBiome(p) != Heightmap.Biome.Meadows)
                {
                    continue;
                }
                // Open ground: forest factor high around the spot and on the land side (Meadows trees grow where it is low).
                if (pass == 0 && (WorldGenerator.GetForestFactor(p) < 1.3f))
                {
                    continue;
                }
                for (var k = 0; k < 8; k++)
                {
                    var b = k * Mathf.PI / 4f;
                    var d = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                    var nearWater = wg.GetHeight(p.x + d.x * 15f, p.z + d.z * 15f) < water - 0.5f;
                    var sea = wg.GetHeight(p.x + d.x * 40f, p.z + d.z * 40f) < water - 3f;
                    var landBehind = wg.GetHeight(p.x - d.x * 12f, p.z - d.z * 12f) > water + 0.5f;
                    var openBehind = pass != 0 || WorldGenerator.GetForestFactor(p - d * 10f) >= 1.3f;
                    if (nearWater && landBehind && openBehind && (pass == 2 || sea))
                    {
                        land = new Vector3(p.x, h, p.z);
                        toWater = d;
                        return true;
                    }
                }
            }
        }
        land = Vector3.zero;
        toWater = Vector3.forward;
        return false;
    }

    // Area loaded and far terrain done building (no new tile for 3 s).
    private static IEnumerator Settle(Box result)
    {
        var start = Time.realtimeSinceStartup;
        var lastBuilds = -1;
        var stableSince = start;
        while (Time.realtimeSinceStartup - start < SettleTimeout)
        {
            var mgr = LodTerrainManager.Instance;
            var builds = mgr != null ? mgr.TotalBuilds : -1;
            if (builds != lastBuilds)
            {
                lastBuilds = builds;
                stableSince = Time.realtimeSinceStartup;
            }
            else if (ZoneSystem.instance.IsActiveAreaLoaded() && Time.realtimeSinceStartup - stableSince >= 3f)
            {
                result.Ok = true;
                result.Detail = $"settled after {Time.realtimeSinceStartup - start:0.0} s: {mgr.GetStats()}";
                yield break;
            }
            yield return new WaitForSeconds(0.25f);
        }
        var m = LodTerrainManager.Instance;
        result.Detail = $"still building after {SettleTimeout:0} s: {(m != null ? m.GetStats() : "no manager")}";
    }

    private static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return null;
        }
    }

    // Vanilla distant teleport (loading screen, hold until the area is there). Vanilla refuse a new one within 2 s of
    // the last: retry. (Copy of Deep North Awakening's.)
    private static IEnumerator TeleportAndWait(Player player, Vector3 target, float seconds, Box result)
    {
        result.Ok = false;
        var until = Time.time + seconds;
        while (!player.TeleportTo(target, player.transform.rotation, true))
        {
            if (Time.time > until)
            {
                yield break;
            }
            yield return new WaitForSeconds(0.25f);
        }
        while (player.IsTeleporting() && Time.time < until)
        {
            yield return new WaitForSeconds(0.25f);
        }
        result.Ok = !player.IsTeleporting() && Flat(player.transform.position - target).magnitude < 20f;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static bool Check(bool condition, string what)
    {
        if (!condition)
        {
            SelfTest.Fail(Name, what);
        }
        return condition;
    }

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log.Error($"{Name} clean-up failed: {e}");
        }
    }

    private sealed class Box
    {
        public bool Ok;
        public string Detail;
    }
}

// Debug build only. Same view, one screenshot per draw mode, so a person can see which part of the mod draw a wrong
// pixel: 1 mod as is, 2 far objects hidden, 3 real zones left to game, 4 paint only (depth renderers hidden), 6 far
// tiles hidden, 7 far tiles cast no shadow, 8 depth renderers alone (no real-zone copies). Used by horizons.near and
// console "dh terrain abshots". Me put every mode back, also when stopped early.
internal static class NearGroundCapture
{
    // Global keyword the game set from its Tessellation graphics setting (GraphicsSettingsManager.ApplyTesselation).
    private const string TessellationKeyword = "TESSELATION_ON";

    internal static IEnumerator Modes(LodTerrainManager mgr, Action<string> shot)
    {
        var objects = GameObject.Find("DistantHorizons_Objects");
        var tiles = GameObject.Find("DistantHorizons_LOD");
        var copyMode = mgr.CopyMode;
        var tessellation = Shader.IsKeywordEnabled(TessellationKeyword);
        try
        {
            yield return Shot(shot, "1-mod");
            if (objects != null)
            {
                objects.SetActive(false);
                yield return Shot(shot, "2-no-far-objects");
                objects.SetActive(true);
            }
            mgr.NoRealCopies = true;
            yield return Shot(shot, "3-real-zones-by-game");
            mgr.NoRealCopies = false;
            mgr.PaintOnly = true;
            yield return Shot(shot, "4-paint-only");
            mgr.PaintOnly = false;
            if (tiles != null)
            {
                tiles.SetActive(false);
                yield return Shot(shot, "6-no-far-tiles");
                ShowTiles(mgr, tiles);
            }
            mgr.SetTileShadowsOffForTest(true);
            yield return Shot(shot, "7-no-tile-shadows");
            mgr.SetTileShadowsOffForTest(false);
            mgr.CopyMode = 0;
            yield return Shot(shot, "8-depth-renderers-only");
            mgr.CopyMode = copyMode;
            // Hardest case: every real zone painted, also the one under the camera. Then same without the game's
            // tessellation keyword (its domain stage add a world-position bump to the ground).
            mgr.PaintAllRealZones = true;
            yield return Shot(shot, "a-all-zones-painted");
            if (tessellation)
            {
                Shader.DisableKeyword(TessellationKeyword);
                yield return Shot(shot, "b-all-zones-painted-no-tessellation");
                Shader.EnableKeyword(TessellationKeyword);
            }
            mgr.PaintAllRealZones = false;
            yield return Frames(2);
        }
        finally
        {
            if (mgr != null)
            {
                mgr.NoRealCopies = false;
                mgr.PaintOnly = false;
                mgr.PaintAllRealZones = false;
                mgr.CopyMode = copyMode;
                mgr.SetTileShadowsOffForTest(false);
            }
            if (tessellation && !Shader.IsKeywordEnabled(TessellationKeyword)) Shader.EnableKeyword(TessellationKeyword);
            if (objects != null && !objects.activeSelf) objects.SetActive(true);
            if (tiles != null && !tiles.activeSelf) ShowTiles(mgr, tiles);
        }
    }

    // Heightmap.OnEnable put vanilla hide distance and shadows back on every tile: me put mine back after.
    private static void ShowTiles(LodTerrainManager mgr, GameObject tiles)
    {
        tiles.SetActive(true);
        if (mgr != null)
        {
            mgr.ReapplyHideDistances();
            mgr.SetTileShadowsOffForTest(false);
        }
    }

    private static IEnumerator Shot(Action<string> shot, string label)
    {
        yield return Frames(3);
        shot(label);
        yield return Frames(2);
    }

    private static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return null;
        }
    }

    // Simulation distance, shadows, terrain material values and the real zone renderer under pos (what shape near
    // ground beside this mod).
    internal static string DescribeSettings(Vector3 pos)
    {
        var sim = ZNet.instance != null ? ZNet.instance.GetSyncedSimulationDistance() : SimulationDistance.OriginalDistance;
        var text = $"simulation near={sim.NearSimulationDistance} total={sim.TotalSimulationDistance} classic={sim.IsClassic}"
                   + $" | shadows distance={QualitySettings.shadowDistance:0} cascades={QualitySettings.shadowCascades}"
                   + $" lodBias={QualitySettings.lodBias:0.##} tessellation={Shader.IsKeywordEnabled(TessellationKeyword)}";
        var prefabHm = ZoneSystem.instance != null && ZoneSystem.instance.m_zonePrefab != null
            ? ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>(true)
            : null;
        var zm = prefabHm != null ? prefabHm.m_material : null;
        if (zm != null)
        {
            text += $" | zone material {zm.shader.name}: _LodHideModifier={Get(zm, "_LodHideModifier")} _UVScale={Get(zm, "_UVScale")}"
                    + $" _Tess={Get(zm, "_Tess")} _Displacement={Get(zm, "_Displacement")} keywords=[{string.Join(" ", zm.shaderKeywords)}]";
        }
        var hm = Heightmap.FindHeightmap(pos);
        var r = hm != null ? hm.GetComponent<MeshRenderer>() : null;
        if (r != null)
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            text += $" | real zone at ({hm.transform.position.x:0},{hm.transform.position.z:0}): block _LodHideDistance={block.GetFloat("_LodHideDistance"):0.#}"
                    + $" _WaterLevel={block.GetFloat("_WaterLevel"):0.#} cast={r.shadowCastingMode} receive={r.receiveShadows}"
                    + $" forceOff={r.forceRenderingOff} material keywords=[{(r.sharedMaterial != null ? string.Join(" ", r.sharedMaterial.shaderKeywords) : "-")}]";
        }
        var env = EnvMan.instance;
        if (env != null)
        {
            text += $" | time={env.GetDayFraction():0.###} env={(env.GetCurrentEnvironment() != null ? env.GetCurrentEnvironment().m_name : "-")}"
                    + $" _Wet={Shader.GetGlobalFloat("_Wet"):0.##}";
        }
        return text;
    }

    private static string Get(Material m, string prop) => m.HasProperty(prop) ? m.GetFloat(prop).ToString("0.###") : "none";
}
#endif
