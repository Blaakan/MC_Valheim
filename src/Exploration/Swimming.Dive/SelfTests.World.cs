#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Debug build only. Live tests that need more than deep open water:
//   dive.view      camera under on zoom and orbit, small near plane close to the surface, each view setting alone
//                  (visibility 60, fog off, surface off, camera off), darker deeper and at night, weather change with no
//                  frame of normal fog, free camera, no local player, camera back above with the head, everything back at
//                  the surface (T15-T17, T22, T23)
//   dive.exits     what ends a dive: tar on top, dead, debug fly, another dive mod / feature off and on again, teleport
//                  from under water; tombstone spawned under water (NOTE) (T18, T20, T21, T24, L01, L02, X04)
//   dive.floor     sea floor 5-8 m down: stop just above it, hover, drain there, weapons stay put away, Jump taps, back
//                  up (T08, T14, X02)
//   dive.ceiling   under a ship hull and under a slab on the building layer: sideways swimming allowed, locked again once
//                  clear; Jump pressed against the hull; ship deck and seat: no dive (T13, T14, T29)
//   dive.shallow   water 1.6 m and 2 m deep: Crouch cannot go down, no drain, no bobbing (T32)
//   dive.ashlands  hot Ashlands sea: diving works, heat builds and burns the same at depth (T19)
//   dive.bottom    world-edge sea deeper than the water volume: stop above its bottom, still swimming, back up (T33)
//   dive.serpent   Serpent at the surface, diver 7-8 m down: no bite (T31; the Serpent's mood decides how much is proved)
internal static partial class SelfTests
{
    private const string ViewName = "dive.view";
    private const string ExitsName = "dive.exits";
    private const string FloorName = "dive.floor";
    private const string CeilingName = "dive.ceiling";
    private const string ShallowName = "dive.shallow";
    private const string AshlandsName = "dive.ashlands";
    private const string BottomName = "dive.bottom";
    private const string SerpentName = "dive.serpent";

    // ---------- shared tools ----------

    // Almost no wind (so almost no waves) for a test that is not about waves. Put back by the rig.
    private static void CalmWind(DiveRig rig) => PinWind(rig, 0.05f);

    // Wind held at one strength (console "wind 0 <intensity>"): wave size no longer follow the weather's gusts, so a
    // wave test meet the same sea every run. The game blend to it over 5 s. Put back by the rig.
    private static void PinWind(DiveRig rig, float strength)
    {
        var env = EnvMan.instance;
        var on = env.m_debugWind;
        var angle = env.m_debugWindAngle;
        var intensity = env.m_debugWindIntensity;
        rig.Undo("wind", () =>
        {
            var e = EnvMan.instance;
            if (e != null)
            {
                e.m_debugWind = on;
                e.m_debugWindAngle = angle;
                e.m_debugWindIntensity = intensity;
            }
        });
        env.SetDebugWind(0f, strength);
    }

    private static void MovePlayer(Player player, Vector3 position)
    {
        player.transform.position = position;
        var body = player.m_body;
        if (body != null)
        {
            body.position = position;
            body.linearVelocity = Vector3.zero;
        }
    }

    // Player put under water by hand: a short Crouch start the dive there, then no key.
    private static IEnumerator StartDive()
    {
        DiveInput.TestUp = false;
        DiveInput.TestDown = true;
        yield return Ticks(6);
        DiveInput.TestDown = false;
    }

    // Gap between the feet and the first solid thing under them (same ray as the dive's floor stop), or -1.
    private static float FloorGap(Player p)
    {
        var origin = p.transform.position + Vector3.up * 0.3f;
        return Physics.Raycast(origin, Vector3.down, out var hit, 30f, Character.s_groundRayMask, QueryTriggerInteraction.Ignore)
            ? hit.distance - 0.3f
            : -1f;
    }

    // Loaded water surfaces that are upside down now.
    private static int TurnedSurfaces()
    {
        var turned = 0;
        foreach (var v in WaterVolume.Instances)
        {
            if (v != null && v.m_waterSurface != null && v.m_waterSurface.transform.up.y < 0f)
            {
                turned++;
            }
        }
        return turned;
    }

    // Loaded surfaces that are safe to turn but not turned (or not known to the view).
    private static int NotTurned(out int safe)
    {
        safe = 0;
        var missing = 0;
        foreach (var v in WaterVolume.Instances)
        {
            if (v == null || v.m_waterSurface == null || !UnderwaterView.SafeToFlip(v))
            {
                continue;
            }
            safe++;
            if (!UnderwaterView.TryGetSaved(v, out _) || v.m_waterSurface.transform.up.y > 0f)
            {
                missing++;
            }
        }
        return missing;
    }

    // First loaded surfaces carry the vanilla _depth corners again.
    private static bool DepthBack(int sample, out int checkedCount)
    {
        var vanilla = new float[4];
        checkedCount = 0;
        foreach (var v in WaterVolume.Instances)
        {
            if (v == null || v.m_waterSurface == null)
            {
                continue;
            }
            UnderwaterView.VanillaCorners(v, vanilla);
            var now = v.m_waterSurface.material.GetFloatArray("_depth");
            if (now == null || now.Length < 4 || !Near(now[0], vanilla[0]) || !Near(now[1], vanilla[1])
                || !Near(now[2], vanilla[2]) || !Near(now[3], vanilla[3]))
            {
                return false;
            }
            if (++checkedCount >= sample)
            {
                break;
            }
        }
        return checkedCount > 0;
    }

    private static WaterVolume VolumeAt(Vector3 point)
    {
        foreach (var v in WaterVolume.Instances)
        {
            if (v != null && v.m_collider != null)
            {
                var b = v.m_collider.bounds;
                if (point.x >= b.min.x && point.x <= b.max.x && point.z >= b.min.z && point.z <= b.max.z)
                {
                    return v;
                }
            }
        }
        return null;
    }

    // Item given to the player for the test, taken away again by the rig.
    private static ItemDrop.ItemData GiveItem(DiveRig rig, string prefab)
    {
        var db = ObjectDB.instance;
        var player = rig.Player;
        if (db == null || db.GetItemPrefab(prefab) == null)
        {
            return null;
        }
        var item = player.GetInventory().AddItem(prefab, 1, 1, 0, 0L, "", false);
        if (item == null)
        {
            return null;
        }
        rig.Undo("item " + prefab, () =>
        {
            player.UnequipItem(item, false);
            player.GetInventory().RemoveItem(item);
        });
        return item;
    }

    // Small numbers (heat per second): five decimals.
    private static string F5(float v) => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);

    // New hit on the player since `last`, of this kind.
    private static bool NewHit(Player p, ref HitData last, HitData.HitType type)
    {
        if (ReferenceEquals(p.m_lastHit, last))
        {
            return false;
        }
        last = p.m_lastHit;
        return last != null && last.m_hitType == type;
    }

    // Fog density Sneak Ambush counts for its fog bonus (its own value, from the weather), null = mod missing or unknown.
    private static float? SneakFog()
    {
        const string guid = "MC.Combat.Sneak.Ambush";
        const string type = "MC.Combat.SneakAmbushMod.StealthState";
        if (TryReadStatic(guid, type, "FogDensityKnown", out var known) && known is bool yes && yes
            && TryReadStatic(guid, type, "FogDensity", out var value) && value is float density)
        {
            return density;
        }
        return null;
    }

    // ---------- dive.view ----------

    private static IEnumerator RunView()
    {
        yield return Settle();
        var s = Begin(ViewName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            CalmWind(rig);
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            rig.Noon();
            var box = new Box();
            var cam = GameCamera.instance;
            var tint = Visuals.DefaultFogColor;
            var waterDistance = cam.m_minWaterDistance;
            var distance = cam.m_distance;
            rig.Undo("camera distance", () =>
            {
                if (GameCamera.instance != null)
                {
                    GameCamera.instance.m_distance = distance;
                }
            });
            yield return new WaitForSeconds(0.5f);
            yield return ViewSettled();
            var preDensity = RenderSettings.fogDensity;
            var preColor = RenderSettings.fogColor;
            var preSun = Shader.GetGlobalVector(UnderwaterView.SunFogId);
            var sneakBefore = SneakFog();
            var rotations = new List<KeyValuePair<WaterVolume, Quaternion>>();
            foreach (var v in WaterVolume.Instances)
            {
                if (v != null && v.m_waterSurface != null)
                {
                    rotations.Add(new KeyValuePair<WaterVolume, Quaternion>(v, v.m_waterSurface.transform.localRotation));
                }
            }
            c.Check(!UnderwaterView.Active && !UnderwaterView.FogApplied && TurnedSurfaces() == 0 && rotations.Count > 0,
                $"before the dive: normal view ({rotations.Count} water surfaces loaded, {TurnedSurfaces()} upside down)");

            // ---- 3 m down: camera under, tint, 25 m, sun side tinted too, every surface turned (T15-T17) ----
            yield return DiveTo(player, 2.5f, 6f, box);
            c.Check(box.Ok, "dive to 2.5 m: " + box.Detail);
            yield return Ticks(65);
            yield return ViewSettled(2);
            var camPos = cam.transform.position;
            var surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            c.Check(surface > -9000f && camPos.y < surface - 0.04f && DiveCamera.Lifted && UnderwaterView.Active,
                $"camera under the surface with the diver (camera {F(surface - camPos.y)} m under)");
            var depthShallow = UnderwaterView.LastDepth;
            var colorShallow = RenderSettings.fogColor;
            var want25 = Mathf.Max(UnderwaterView.BaseDensity, UnderwaterView.Density(RenderSettings.fogMode, Visuals.DefaultVisibility));
            c.Check(UnderwaterView.FogApplied && Near(RenderSettings.fogDensity, want25, 0.0001f)
                    && Near(colorShallow, UnderwaterView.Tint(UnderwaterView.BaseColor, tint, depthShallow)),
                $"under water: fog colour {F(colorShallow)} is the tint for this light and depth, density {F(RenderSettings.fogDensity)} "
                + $"is the one of {F(Visuals.DefaultVisibility)} m visibility ({F(want25)})");
            c.Check(NearRaw(Shader.GetGlobalVector(UnderwaterView.SunFogId), Raw(colorShallow)),
                "under water: the fog toward the sun has the same tint (no sun-side haze)");
            // Sneak Ambush (X03): its fog bonus reads the weather's fog, so the tint on screen changes nothing for it.
            var sneakUnder = SneakFog();
            if (sneakBefore.HasValue && sneakUnder.HasValue)
            {
                c.Check(Near(sneakBefore.Value, sneakUnder.Value, 0.0005f),
                    $"Sneak Ambush: the fog it counts is the weather's, the same under water ({F(sneakUnder.Value)}) as before the "
                    + $"dive ({F(sneakBefore.Value)}), while the fog on screen went from {F(preDensity)} to {F(RenderSettings.fogDensity)}");
            }
            else
            {
                c.Note("Sneak Ambush is not loaded (or its fog value is not known yet): its fog bonus was not compared");
            }
            var missing = 1;
            var safe = 0;
            for (var i = 0; i < 4 && missing > 0; i++)
            {
                yield return ViewSettled();
                missing = NotTurned(out safe);
            }
            c.Check(safe > 0 && missing == 0 && UnderwaterView.FlippedCount >= safe,
                $"under water: every loaded water surface is turned for the view from below ({safe - missing} of {safe})");

            // ---- zoomed out, looking up, level, down: the camera stays under; near plane small near the surface (T15) ----
            cam.m_distance = cam.m_maxDistance;
            var frames = 0;
            var notUnder = 0;
            var near = 0;
            var nearBad = 0;
            foreach (var pitch in new[] { -60f, 0f, 45f, 75f })
            {
                rig.Look(pitch);
                yield return null;
                yield return null;
                for (var i = 0; i < 6; i++)
                {
                    yield return FrameEnd;
                    camPos = cam.transform.position;
                    surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
                    frames++;
                    if (!(surface > -9000f && camPos.y < surface - 0.04f && DiveCamera.Lifted))
                    {
                        notUnder++;
                    }
                    if (surface - camPos.y < DiveCamera.WaterClipDepth - 0.05f)
                    {
                        near++;
                        if (cam.m_camera == null || cam.m_camera.nearClipPlane > cam.m_nearClipPlaneMin + 0.0001f)
                        {
                            nearBad++;
                        }
                    }
                    yield return null;
                }
            }
            c.Check(frames > 0 && notUnder == 0,
                $"zoomed out and looking up, level and down: the camera stays under the surface ({notUnder} of {frames} frames not)");
            c.Check(near > 0 && nearBad == 0,
                $"camera within 1 m of the surface: small near plane, so the surface is not cut open ({nearBad} of {near} frames not)");
            c.Check(Mathf.Abs(cam.m_minWaterDistance - waterDistance) < 0.0001f,
                $"camera water distance never left changed ({F(cam.m_minWaterDistance)})");
            SelfTest.Screenshot(ViewName, "zoomed-out-looking-down");
            yield return null;
            yield return null;
            rig.Look(-50f);
            yield return null;
            yield return null;
            yield return null;
            SelfTest.Screenshot(ViewName, "looking-up");
            yield return null;
            yield return null;
            rig.Look(0f);
            cam.m_distance = distance;
            yield return ViewSettled(2);

            // ---- each view setting alone (T15-T17) ----
            Visuals.TestVisibility = 60f;
            yield return ViewSettled();
            var want60 = Mathf.Max(UnderwaterView.BaseDensity, UnderwaterView.Density(RenderSettings.fogMode, 60f));
            // Thinner than at 25 m, unless the weather's own fog is thicker than both (then it stays).
            c.Check(UnderwaterView.FogApplied && Near(RenderSettings.fogDensity, want60, 0.0001f)
                    && (want60 < want25 || UnderwaterView.BaseDensity >= want25 - 0.0001f),
                $"UnderwaterVisibility 60: fog density {F(RenderSettings.fogDensity)} is the one of 60 m ({F(want60)}; at 25 m "
                + $"{F(want25)}, the weather's own fog {F(UnderwaterView.BaseDensity)})");
            Visuals.TestVisibility = null;
            Visuals.TestFog = false;
            yield return ViewSettled();
            c.Check(UnderwaterView.Active && !UnderwaterView.FogApplied && UnderwaterView.FlippedCount > 0
                    && RenderSettings.fogDensity <= UnderwaterView.BaseDensity * 1.05f + 0.0001f
                    && !Near(RenderSettings.fogColor, colorShallow, 0.01f),
                $"UnderwaterFog off: no tint under water (fog density {F(RenderSettings.fogDensity)}, colour "
                + $"{F(RenderSettings.fogColor)}), surfaces still turned");
            Visuals.TestFog = null;
            yield return ViewSettled();
            c.Check(UnderwaterView.FogApplied && Near(RenderSettings.fogDensity, want25, 0.0001f), "UnderwaterFog on again: tint back");
            Visuals.TestSurface = false;
            yield return ViewSettled();
            c.Check(UnderwaterView.Active && UnderwaterView.FogApplied && UnderwaterView.FlippedCount == 0 && TurnedSurfaces() == 0,
                $"SurfaceFromBelow off: no surface is turned ({TurnedSurfaces()} upside down), tint stays");
            Visuals.TestSurface = null;
            yield return ViewSettled();
            c.Check(UnderwaterView.FlippedCount > 0 && TurnedSurfaces() > 0, "SurfaceFromBelow on again: surfaces turned again");
            Visuals.TestCamera = false;
            yield return ViewSettled(2);
            camPos = cam.transform.position;
            surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            c.Check(DiveState.Diving && DiveState.Deep && !DiveCamera.Lifted && camPos.y >= surface + cam.m_minWaterDistance - 0.02f
                    && !UnderwaterView.Active && !UnderwaterView.FogApplied && UnderwaterView.FlippedCount == 0,
                $"UnderwaterCamera off: the camera stays above the water as in the normal game (camera {F(camPos.y - surface)} m "
                + "above), so no tint and no turned surface");
            Visuals.TestCamera = null;
            yield return ViewSettled(2);
            c.Check(DiveCamera.Lifted && UnderwaterView.Active && UnderwaterView.FogApplied, "UnderwaterCamera on again: camera under again");

            // ---- free camera: the normal game's view, even under water (T23) ----
            DiveInput.ClearTest();
            var yFree = player.transform.position.y;
            rig.Undo("free camera", () =>
            {
                if (GameCamera.InFreeFly() && GameCamera.instance != null)
                {
                    GameCamera.instance.ToggleFreeFly();
                }
            });
            cam.ToggleFreeFly();
            yield return ViewSettled(2);
            camPos = cam.transform.position;
            surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            c.Check(GameCamera.InFreeFly() && surface > -9000f && camPos.y < surface - UnderwaterView.ViewMargin
                    && !UnderwaterView.Active && !UnderwaterView.FogApplied && UnderwaterView.FlippedCount == 0 && TurnedSurfaces() == 0,
                $"free camera under water ({F(surface - camPos.y)} m under): the normal game's view, no tint, no turned surface");
            yield return new WaitForSeconds(1f);
            c.Check(DiveState.Diving && DiveState.Deep && Mathf.Abs(player.transform.position.y - yFree) < 0.3f,
                $"free camera: the diver stays at depth (moved {F(player.transform.position.y - yFree)} m in 1 s)");
            cam.ToggleFreeFly();
            yield return ViewSettled(2);
            c.Check(!GameCamera.InFreeFly() && UnderwaterView.Active && UnderwaterView.FogApplied && UnderwaterView.FlippedCount > 0
                    && DiveCamera.Lifted,
                "free camera off: the diver's view under water is back (camera under, tint, surfaces turned)");
            DiveInput.TestDown = false;
            DiveInput.TestUp = false;

            // ---- no local player (log out): the view is put back the same frame (T22) ----
            yield return ViewSettled();
            var local = Player.m_localPlayer;
            bool loggedOut;
            try
            {
                Player.m_localPlayer = null;
                UnderwaterView.Tick(cam);
                loggedOut = !UnderwaterView.Active && !UnderwaterView.FogApplied && UnderwaterView.FlippedCount == 0
                            && TurnedSurfaces() == 0;
            }
            finally
            {
                Player.m_localPlayer = local;
            }
            c.Check(loggedOut, "no local player any more (as after a log out): tint off and surfaces back at once");
            yield return ViewSettled();
            c.Check(UnderwaterView.Active && UnderwaterView.FogApplied && UnderwaterView.FlippedCount > 0, "player there again: view under water again");

            // ---- deeper = darker (T16) ----
            yield return DiveTo(player, 7.5f, 8f, box);
            c.Check(box.Ok, "dive to 7.5 m: " + box.Detail);
            yield return Ticks(65);
            yield return ViewSettled();
            var depthDeep = UnderwaterView.LastDepth;
            var colorDeep = RenderSettings.fogColor;
            var baseNow = UnderwaterView.BaseColor;
            c.Check(depthDeep > depthShallow + 2f && Near(colorDeep, UnderwaterView.Tint(baseNow, tint, depthDeep))
                    && colorDeep.grayscale < UnderwaterView.Tint(baseNow, tint, depthShallow).grayscale - 0.001f,
                $"camera {F(depthDeep)} m under instead of {F(depthShallow)} m: darker tint ({F(colorDeep)}, was {F(colorShallow)})");

            // ---- night = darker (T16) ----
            var dayBase = UnderwaterView.BaseColor;
            rig.TimeOfDay(0f);
            yield return new WaitForSeconds(0.6f);
            yield return ViewSettled();
            var nightBase = UnderwaterView.BaseColor;
            var nightColor = RenderSettings.fogColor;
            c.Check(UnderwaterView.FogApplied && Near(nightColor, UnderwaterView.Tint(nightBase, tint, UnderwaterView.LastDepth)),
                $"midnight under water: fog colour {F(nightColor)} is the tint for the night's light");
            var dayBright = Mathf.Clamp(dayBase.grayscale / UnderwaterView.ReferenceBrightness, UnderwaterView.MinBrightness, 1f);
            var nightBright = Mathf.Clamp(nightBase.grayscale / UnderwaterView.ReferenceBrightness, UnderwaterView.MinBrightness, 1f);
            if (nightBright < dayBright - 0.02f)
            {
                c.Check(nightColor.grayscale < colorDeep.grayscale - 0.001f,
                    $"midnight under water: darker than at noon ({F(nightColor)}, noon {F(colorDeep)})");
            }
            else
            {
                c.Note($"the normal fog is not darker at midnight than at noon in this weather (brightness {F(nightBright)} vs "
                       + $"{F(dayBright)}): darker at night not shown");
            }
            SelfTest.Screenshot(ViewName, "midnight");
            yield return null;
            yield return null;
            rig.Noon();
            yield return new WaitForSeconds(0.4f);

            // ---- weather change: no frame without the under-water fog (T16) ----
            var storm = StormEnv();
            if (c.Check(!string.IsNullOrEmpty(storm), "the game has a weather to force (EnvMan.m_environments)"))
            {
                var envBefore = EnvMan.instance.m_debugEnv;
                rig.Undo("weather", () => EnvMan.instance.m_debugEnv = envBefore);
                yield return ViewSettled();
                var baseStart = UnderwaterView.BaseColor;
                var densityStart = UnderwaterView.BaseDensity;
                EnvMan.instance.m_debugEnv = storm;
                var flicker = 0;
                var changed = false;
                frames = 0;
                var until = Time.time + 3f;
                while (Time.time < until)
                {
                    yield return FrameEnd;
                    frames++;
                    var want = Mathf.Max(UnderwaterView.BaseDensity,
                        UnderwaterView.Density(RenderSettings.fogMode, Visuals.DefaultVisibility));
                    if (!UnderwaterView.FogApplied || !Near(RenderSettings.fogDensity, want, 0.0001f)
                        || !Near(RenderSettings.fogColor, UnderwaterView.Tint(UnderwaterView.BaseColor, tint, UnderwaterView.LastDepth)))
                    {
                        flicker++;
                    }
                    changed |= !Near(UnderwaterView.BaseColor, baseStart) || !Near(UnderwaterView.BaseDensity, densityStart, 0.00001f);
                    yield return null;
                }
                c.Check(frames > 20 && flicker == 0,
                    $"weather changing to '{storm}' under water: every frame shows the under-water fog ({flicker} of {frames} frames "
                    + "did not)");
                c.Note($"the normal fog under the tint changed with the weather: {changed} (colour {F(baseStart)} -> "
                       + $"{F(UnderwaterView.BaseColor)}, density {F(densityStart)} -> {F(UnderwaterView.BaseDensity)})");
                EnvMan.instance.m_debugEnv = envBefore;
                yield return new WaitForSeconds(2.5f);
            }

            // ---- rising: camera under while the head is under, above once it is out; fog back that very frame ----
            var lastTint = RenderSettings.fogColor;
            var earlyAbove = false;
            var lateUnder = false;
            var sawOff = false;
            var offClean = false;
            var wasActive = UnderwaterView.Active;
            DiveInput.TestUp = true;
            var t0 = Time.time;
            while (Time.time - t0 < 12f)
            {
                yield return FrameEnd;
                var eyeDepth = player.GetLiquidLevel() - player.m_eye.position.y;
                if (DiveState.Diving && eyeDepth > 0.6f && !DiveCamera.Lifted)
                {
                    earlyAbove = true;
                }
                if (eyeDepth < 0.05f && DiveCamera.Lifted)
                {
                    lateUnder = true;
                }
                if (wasActive && !UnderwaterView.Active && !sawOff)
                {
                    sawOff = true;
                    offClean = !UnderwaterView.FogApplied && UnderwaterView.FlippedCount == 0
                               && Near(RenderSettings.fogDensity, UnderwaterView.BaseDensity, 0.0001f + UnderwaterView.BaseDensity * 0.05f)
                               && Near(RenderSettings.fogColor, UnderwaterView.BaseColor, 0.02f);
                }
                wasActive = UnderwaterView.Active;
                if (!DiveState.Diving && sawOff)
                {
                    break;
                }
                yield return null;
            }
            DiveInput.TestUp = false;
            c.Check(!earlyAbove && !lateUnder,
                $"rising: the camera is under while the head is under ({!earlyAbove}) and above once the head is out ({!lateUnder})");
            c.Check(sawOff && offClean,
                $"camera leaving the water: the fog is the normal game's again that very frame (seen {sawOff}, normal values {offClean})");

            // ---- at the surface: everything as before the dive (T15-T17) ----
            yield return new WaitForSeconds(1.5f);
            yield return ViewSettled();
            camPos = cam.transform.position;
            surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            c.Check(!DiveState.Diving && !UnderwaterView.Active && !UnderwaterView.FogApplied && UnderwaterView.FlippedCount == 0
                    && !DiveCamera.Lifted && camPos.y > surface,
                $"at the surface: camera above the water, no tint, no turned surface (camera {F(camPos.y - surface)} m above)");
            var moved = 0;
            var alive = 0;
            foreach (var pair in rotations)
            {
                if (pair.Key == null || pair.Key.m_waterSurface == null)
                {
                    continue;
                }
                alive++;
                if (!SameRotation(pair.Key.m_waterSurface.transform.localRotation, pair.Value))
                {
                    moved++;
                }
            }
            var depthOk = DepthBack(8, out var depthChecked);
            c.Check(alive > 0 && moved == 0 && TurnedSurfaces() == 0 && depthOk,
                $"at the surface: every water surface is as before the dive ({alive - moved} of {alive} rotations the same, "
                + $"{TurnedSurfaces()} upside down, depth corners back on {depthChecked} checked: {depthOk})");
            var sunNow = Shader.GetGlobalVector(UnderwaterView.SunFogId);
            c.Check(!Near(RenderSettings.fogColor, lastTint, 0.01f) && !NearRaw(sunNow, Raw(lastTint)),
                "at the surface: the fog and the fog toward the sun are no longer the under-water tint");
            c.Note($"fog before the dive: density {F(preDensity)}, colour {F(preColor)}, sun side raw {F(preSun)}; after: density "
                   + $"{F(RenderSettings.fogDensity)}, colour {F(RenderSettings.fogColor)}, sun side raw {F(sunNow)} (same noon; the "
                   + "weather may have moved on)");
            SelfTest.Screenshot(ViewName, "surface-after");
            yield return null;
            yield return null;

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.exits ----------

    private const string FakeDiveGuid = "MainStreetGaming.BetterDiving";
    private const string FakeDiveName = "BetterDiving";
    private const string FakeDiveStatus = "Inactive: BetterDiving also handles diving. Remove one of them.";

    private static IEnumerator RunExits()
    {
        yield return Settle();
        var s = Begin(ExitsName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        var tap = s.Tap;
        try
        {
            ApplyDefaults();
            CalmWind(rig);
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            var cam = GameCamera.instance;
            var zdo = player.m_nview.GetZDO();

            // ---- a tombstone 4 m under the surface (T20, vanilla): looked at again before leaving ----
            GameObject stone = null;
            var stoneAt = Time.time;
            if (player.m_tombstone != null)
            {
                var here = player.transform.position;
                stone = UnityEngine.Object.Instantiate(player.m_tombstone,
                    new Vector3(here.x + 5f, player.GetLiquidLevel() - 4f, here.z), Quaternion.identity);
                rig.Undo("tombstone", () =>
                {
                    if (stone != null && ZNetScene.instance != null)
                    {
                        ZNetScene.instance.Destroy(stone);
                    }
                });
                // An empty tombstone removes itself after 2 s: one item keeps it.
                var grave = stone.GetComponent<Container>();
                var filler = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Club") : null;
                if (grave != null && filler != null)
                {
                    grave.GetInventory().AddItem(filler, 1);
                }
            }

            // ---- tar is the top liquid: no dive (T18; tar level faked on the player) ----
            var tarBefore = player.m_tarLevel;
            rig.Undo("tar", () =>
            {
                player.SetLiquidLevel(tarBefore, LiquidType.Tar, null);
                player.m_seman.RemoveStatusEffect(SEMan.s_statusEffectTared, true);
            });
            player.SetLiquidLevel(player.m_waterLevel + 0.4f, LiquidType.Tar, null);
            yield return Ticks(5);
            var inTar = !player.InWater() && player.InLiquid() && player.IsSwimming();
            var yTar = player.transform.position.y;
            var dived = false;
            DiveInput.TestDown = true;
            for (var i = 0; i < 75; i++)
            {
                yield return FixedTick;
                dived |= DiveState.Diving;
            }
            DiveInput.TestDown = false;
            c.Check(inTar && !dived && player.transform.position.y > yTar - 0.5f,
                $"tar above the water (faked): Crouch held 1.5 s does not dive (in tar {inTar}, dive {dived}, feet moved "
                + $"{F(player.transform.position.y - yTar)} m)");
            player.SetLiquidLevel(tarBefore, LiquidType.Tar, null);
            player.m_seman.RemoveStatusEffect(SEMan.s_statusEffectTared, true);
            yield return new WaitForSeconds(1.5f);
            yield return DiveTo(player, 2.5f, 6f, box);
            yield return Ticks(30);
            tap.Clear();
            var deepBefore = DiveState.Diving && DiveState.Deep;
            player.SetLiquidLevel(player.m_waterLevel + 0.4f, LiquidType.Tar, null);
            yield return Ticks(3);
            c.Check(deepBefore && !DiveState.Diving && player.m_body.useGravity && tap.Count("Dive ended: NotWater.") == 1,
                $"tar comes over a diver (faked): the dive ends (\"Dive ended: NotWater.\" lines {tap.Count("Dive ended: NotWater.")}, "
                + $"gravity {player.m_body.useGravity})");
            player.SetLiquidLevel(tarBefore, LiquidType.Tar, null);
            player.m_seman.RemoveStatusEffect(SEMan.s_statusEffectTared, true);
            yield return new WaitForSeconds(2f);

            // ---- dead: the dive ends (T20; the dead flag set for 3 physics ticks, no real death) ----
            yield return DiveTo(player, 2.5f, 6f, box);
            yield return Ticks(30);
            tap.Clear();
            deepBefore = DiveState.Diving && DiveState.Deep;
            rig.Undo("dead flag", () => zdo.Set(ZDOVars.s_dead, false));
            zdo.Set(ZDOVars.s_dead, true);
            yield return Ticks(3);
            var deadOver = !DiveState.Diving && player.m_body.useGravity && tap.Count("Dive ended: Dead.") == 1;
            zdo.Set(ZDOVars.s_dead, false);
            c.Check(deepBefore && deadOver,
                $"dead under water (flag only): the dive ends, gravity back (\"Dive ended: Dead.\" lines {tap.Count("Dive ended: Dead.")})");
            yield return new WaitForSeconds(2f);

            // ---- debug fly ends the dive; fly off = back to normal swimming (T24) ----
            yield return DiveTo(player, 3.5f, 6f, box);
            yield return Ticks(40);
            tap.Clear();
            deepBefore = DiveState.Diving && DiveState.Deep;
            rig.Undo("debug fly", () =>
            {
                if (player.m_debugFly)
                {
                    player.m_debugFly = false;
                    zdo.Set(ZDOVars.s_debugFly, false);
                }
            });
            player.ToggleDebugFly();
            yield return Ticks(2);
            c.Check(deepBefore && player.IsDebugFlying() && !DiveState.Diving && tap.Count("Dive ended: DebugFly.") == 1,
                $"debug fly under water: the dive ends at once (\"Dive ended: DebugFly.\" lines {tap.Count("Dive ended: DebugFly.")})");
            var flyFrom = player.transform.position;
            yield return Ticks(50);
            var hover = Mathf.Abs(player.transform.position.y - flyFrom.y);
            for (var i = 0; i < 15; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            rig.Drive(Vector3.zero);
            var flew = Vector3.Distance(Flat(flyFrom), Flat(player.transform.position));
            c.Check(hover < 0.3f && flew > 1f && !DiveState.Diving,
                $"debug fly under water: flying as usual (stays in place {F(hover)} m, then {F(flew)} m forward in 0.3 s)");
            yield return Ticks(20);
            player.ToggleDebugFly();
            var t0 = Time.time;
            while (Under(player) > 0.5f && Time.time - t0 < 5f)
            {
                yield return FixedTick;
            }
            yield return Ticks(25);
            c.Check(!player.IsDebugFlying() && player.IsSwimming() && !DiveState.Diving && Under(player) < 0.5f && player.m_body.useGravity,
                $"debug fly off: back in the water, swimming normally at the surface {F(Time.time - t0)} s later "
                + $"({F(Under(player))} m under the rest height)");
            yield return new WaitForSeconds(1f);

            // ---- another dive mod appears / the feature goes off while under water, then on again (L01, L02, X04).
            // A made-up BetterDiving entry turns the mod off through the same path as the Enabled switch. ----
            yield return DiveTo(player, 3.5f, 6f, box);
            yield return Ticks(65);
            yield return ViewSettled();
            var viewBefore = DiveState.Diving && DiveState.Deep && UnderwaterView.Active && UnderwaterView.FogApplied
                             && UnderwaterView.FlippedCount > 0 && DiveCamera.Lifted;
            var waterDistance = cam.m_minWaterDistance;
            tap.Clear();
            KeepOverrides = true;
            rig.Undo("made-up dive mod", () =>
            {
                ForeignMods.TestPlugins = null;
                ForeignMods.ForgetTest(FakeDiveGuid);
                KeepOverrides = false;
                FeatureRegistry.RefreshAll();
            });
            ForeignMods.TestPlugins = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(FakeDiveGuid, FakeDiveName),
            };
            FeatureRegistry.RefreshAll();
            var view = FeatureRegistry.Find(ModInfo.Guid);
            var state = view != null ? view.Value.State : "?";
            var status = view != null ? view.Value.Status : "?";
            c.Check(viewBefore, "under water with camera, tint and turned surfaces before the mod goes off");
            c.Check(view != null && !view.Value.IsActive && state == nameof(ModState.Conflict),
                $"another dive mod loaded: {ModInfo.Name} goes inactive (state {state})");
            c.Check(status == FakeDiveStatus, $"Status text \"{status}\" (expected \"{FakeDiveStatus}\")");
            c.Check(!DiveState.Diving && player.m_body.useGravity && !UnderwaterView.Active && !UnderwaterView.FogApplied
                    && UnderwaterView.FlippedCount == 0 && TurnedSurfaces() == 0 && !DiveCamera.Lifted,
                "turned off under water: the dive stops, tint and surfaces are back at once");
            FeatureRegistry.RefreshAll();
            c.Check(tap.Count("BetterDiving also handles diving, so") == 1,
                $"the log warns once about the other dive mod ({tap.Count("BetterDiving also handles diving, so")} line(s) after two checks)");
            t0 = Time.time;
            while (Under(player) > 0.4f && Time.time - t0 < 3f)
            {
                yield return FixedTick;
            }
            c.Check(Under(player) <= 0.4f, $"turned off under water: the normal game lifts the swimmer to the surface in {F(Time.time - t0)} s");
            yield return ViewSettled(2);
            var camPos = cam.transform.position;
            var surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            c.Check(camPos.y > surface && Mathf.Abs(cam.m_minWaterDistance - waterDistance) < 0.0001f,
                $"turned off: the camera is above the water (camera {F(camPos.y - surface)} m above)");
            // While off: Crouch (the game's own crouch input too) does nothing in water.
            DiveInput.TestDown = true;
            rig.Drive(Vector3.zero, true, false, false);
            var maxUnder = 0f;
            for (var i = 0; i < 100; i++)
            {
                yield return FixedTick;
                rig.Drive(Vector3.zero);
                maxUnder = Mathf.Max(maxUnder, Under(player));
            }
            DiveInput.TestDown = false;
            yield return ViewSettled();
            camPos = cam.transform.position;
            surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            c.Check(maxUnder < 0.45f && !DiveState.Diving && !UnderwaterView.Active && !DiveCamera.Lifted && camPos.y > surface,
                $"while the mod is off: Crouch held 2 s does nothing in water (feet at most {F(maxUnder)} m under the rest height), "
                + "the camera stays above");
            ForeignMods.TestPlugins = null;
            FeatureRegistry.RefreshAll();
            ForeignMods.ForgetTest(FakeDiveGuid);
            KeepOverrides = false;
            view = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(view != null && view.Value.IsActive, $"other dive mod gone: active again ({(view != null ? view.Value.State : "?")})");
            ApplyDefaults();
            yield return Ticks(5);
            yield return DiveTo(player, 1.5f, 5f, box);
            c.Check(box.Ok, "turned on again: Crouch dives again: " + box.Detail);
            yield return Surface(player, 6f, box);
            yield return new WaitForSeconds(1f);

            // ---- the tombstone (vanilla, noted only) ----
            if (stone != null)
            {
                var stoneY = stone.transform.position.y;
                var stoneSurface = Floating.GetLiquidLevel(stone.transform.position, 1f, LiquidType.Water);
                c.Note($"tombstone spawned 4 m under the surface: {F(Time.time - stoneAt)} s later it is "
                       + $"{F(stoneSurface - stoneY)} m under the surface (floats up as in the normal game: {stoneSurface - stoneY < 1f})");
                ZNetScene.instance.Destroy(stone);
                stone = null;
            }
            else
            {
                c.Note("no tombstone prefab on the player: tombstone not looked at");
            }

            // ---- teleport from under water = the trip home (T21) ----
            yield return DiveTo(player, 3.5f, 6f, box);
            yield return Ticks(40);
            yield return ViewSettled();
            viewBefore = DiveState.Diving && DiveState.Deep && UnderwaterView.Active && UnderwaterView.FogApplied
                         && UnderwaterView.FlippedCount > 0;
            tap.Clear();
            var started = false;
            t0 = Time.time;
            while (!started && Time.time - t0 < 6f)
            {
                started = player.TeleportTo(rig.Origin, rig.OriginRotation, true);
                if (!started)
                {
                    yield return new WaitForSeconds(0.25f);
                }
            }
            if (c.Check(viewBefore && started, $"teleport started from 3.5 m under water (view on {viewBefore}, accepted {started})"))
            {
                yield return Ticks(2);
                c.Check(!DiveState.Diving && player.m_body.useGravity && tap.Count("Dive ended: Teleporting.") == 1,
                    $"teleport under water: the dive ends (\"Dive ended: Teleporting.\" lines {tap.Count("Dive ended: Teleporting.")})");
                t0 = Time.time;
                while (player.IsTeleporting() && Time.time - t0 < TravelTimeout)
                {
                    yield return new WaitForSeconds(0.25f);
                }
                rig.Back = !player.IsTeleporting();
                ClearOverrides();
                yield return new WaitForSeconds(1f);
                yield return ViewSettled();
                var home = Vector3.Distance(Flat(player.transform.position), Flat(rig.Origin));
                c.Check(rig.Back && home < 5f && !player.IsSwimming() && !DiveState.Diving && !UnderwaterView.Active
                        && !UnderwaterView.FogApplied && UnderwaterView.FlippedCount == 0 && TurnedSurfaces() == 0 && !DiveCamera.Lifted,
                    $"arrived {F(home)} m from the start: no tint, no turned surface left ({TurnedSurfaces()} upside down), camera normal");
            }
            else
            {
                yield return Surface(player, 6f, box);
                yield return GoHome(s);
            }
            CheckLog(s, "also handles diving");
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.floor ----------

    // Jump effects (sound, puff) of the test player played while the counter patch is on (Harmony prefix of
    // EffectList.Create: one reference compare for every other effect list).
    private static EffectList _jumpEffects;
    private static int _jumpEffectsPlayed;

    private static void CountJumpEffects(EffectList __instance)
    {
        if (_jumpEffects != null && ReferenceEquals(__instance, _jumpEffects))
        {
            _jumpEffectsPlayed++;
        }
    }

    private static IEnumerator RunFloor()
    {
        yield return Settle();
        var s = Begin(FloorName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            CalmWind(rig);
            if (!FindWater(rig.Origin, 5.5f, 7.5f, 5f, 1f, out var spot, out var mapDepth))
            {
                c.Check(false, "no water 5.5 to 7.5 m deep with an even floor within 5 km of the start: not tested");
                c.Report();
                yield break;
            }
            c.Note($"water about {F(mapDepth)} m deep at {F(spot)}, {F(Vector3.Distance(Flat(spot), Flat(rig.Origin)))} m from the "
                   + "start (WorldGenerator)");

            // Weapons in hand on land first (the game refuses to equip while swimming). With Dual Wielding the second one
            // goes to the off hand.
            var hadRight = player.m_rightItem ?? player.m_hiddenRightItem;
            var hadLeft = player.m_leftItem ?? player.m_hiddenLeftItem;
            // Runs after the test weapons are taken away again: what was in hand before goes back.
            rig.Undo("hands", () =>
            {
                if (hadRight != null && player.GetInventory().ContainsItem(hadRight))
                {
                    player.EquipItem(hadRight, false);
                }
                if (hadLeft != null && player.GetInventory().ContainsItem(hadLeft))
                {
                    player.EquipItem(hadLeft, false);
                }
            });
            var club = GiveItem(rig, "Club");
            var knife = GiveItem(rig, "KnifeFlint");
            if (!c.Check(club != null && knife != null, $"test weapons given (Club {club != null}, KnifeFlint {knife != null})"))
            {
                c.Report();
                yield break;
            }
            player.EquipItem(club, false);
            yield return Ticks(5);
            player.EquipItem(knife, false);
            yield return Ticks(10);
            var dualWield = Chainloader.PluginInfos.ContainsKey("MC.Combat.Weapons.DualWield");
            var pair = player.m_rightItem != null && player.m_leftItem != null
                       && (ReferenceEquals(player.m_leftItem, club) || ReferenceEquals(player.m_leftItem, knife));
            if (!c.Check(player.m_rightItem != null, "a weapon is in the right hand before the swim"))
            {
                c.Report();
                yield break;
            }
            c.Note($"Dual Wielding loaded {dualWield}; two weapons in hand as a pair {pair}");

            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var dt = Time.fixedDeltaTime;
            var box = new Box();
            var ground = ZoneSystem.instance.GetGroundHeight(player.transform.position);
            var depth = player.GetLiquidLevel() - ground;
            if (!c.Check(depth > 4.5f && depth < 9f, $"the water is {F(depth)} m deep here (wanted 5 to 8 m)"))
            {
                c.Report();
                yield break;
            }
            c.Check(player.m_rightItem == null && player.m_leftItem == null && player.m_hiddenRightItem != null
                    && (!pair || player.m_hiddenLeftItem != null),
                $"swimming: weapons put away (right hand empty {player.m_rightItem == null}, left hand empty {player.m_leftItem == null})");
            rig.TakeStaminaRate();
            yield return new WaitForSeconds(0.4f);
            rig.Refill();
            var drain = SwimDrain(player);

            // ---- Crouch held: stop just above the floor and hover (T08) ----
            DiveInput.TestUp = false;
            DiveInput.TestDown = true;
            var still = 0;
            var ticks = 0;
            while (ticks < 500 && still < 20)
            {
                yield return FixedTick;
                ticks++;
                still = DiveState.Diving && Mathf.Abs(DiveState.Vy) < 0.001f && Under(player) > 2f ? still + 1 : 0;
            }
            rig.Refill();
            var gap = FloorGap(player);
            c.Check(still >= 20 && DiveState.Deep && gap > 0.05f && gap < 0.5f && player.IsSwimming(),
                $"Crouch held: the diver stops {F(gap)} m above the sea floor after {F(ticks * dt)} s ({F(Under(player))} m under "
                + $"the rest height, deep {DiveState.Deep}, swimming {player.IsSwimming()})");
            // Hover: W does not walk on the floor; the gap stays.
            var from = Flat(player.transform.position);
            var gapMin = gap;
            var gapMax = gap;
            var grounded = 0;
            for (var i = 0; i < 75; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
                var g = FloorGap(player);
                gapMin = Mathf.Min(gapMin, g);
                gapMax = Mathf.Max(gapMax, g);
                if (player.IsOnGround())
                {
                    grounded++;
                }
            }
            rig.Drive(Vector3.zero);
            var walked = Vector3.Distance(from, Flat(player.transform.position));
            c.Check(walked < 0.3f && gapMin > 0.03f && gapMax < 0.6f && player.IsSwimming(),
                $"at the sea floor with Crouch and W held: hovering, no walking ({F(walked)} m in 1.5 s, gap {F(gapMin)}..{F(gapMax)} m, "
                + $"on ground {grounded} of 75 ticks)");
            // Drain goes on at the floor.
            rig.Refill();
            var a = player.GetStamina();
            yield return Ticks(100);
            var rate = (a - player.GetStamina()) / (100 * dt);
            c.Check(Mathf.Abs(rate - drain) <= 0.15f * drain,
                $"hovering at the sea floor with Crouch held: stamina drains {F(rate)} per second (swim drain {F(drain)})");
            // Weapons stay away while hovering.
            c.Check(player.m_rightItem == null && player.m_leftItem == null && player.m_hiddenRightItem != null
                    && (!pair || player.m_hiddenLeftItem != null),
                $"hovering above the sea floor: weapons stay put away{(pair ? " (both of the pair)" : "")}");
            // Touching the floor = "on ground" for the game, which then skips its swim drain: the drain must go on (T08).
            // The hovering diver does not touch, so the touch is faked with the field vanilla reads (IsOnGround:
            // m_lastGroundTouch), as dive.multiplier does.
            rig.Refill();
            var groundTicks = 0;
            for (var i = 0; i < 15; i++)
            {
                player.m_lastGroundTouch = 0f;
                yield return FixedTick;
            }
            a = player.GetStamina();
            for (var i = 0; i < 75; i++)
            {
                player.m_lastGroundTouch = 0f;
                yield return FixedTick;
                if (player.IsOnGround())
                {
                    groundTicks++;
                }
            }
            var touchRate = (a - player.GetStamina()) / (75 * dt);
            c.Check(groundTicks >= 70 && DiveState.Deep && Mathf.Abs(touchRate - drain) <= 0.15f * drain,
                $"touching the sea floor with Crouch held (ground contact faked, on ground {groundTicks} of 75 ticks): stamina "
                + $"keeps draining, {F(touchRate)} per second (swim drain {F(drain)})");
            yield return Ticks(15);
            if (dualWield)
            {
                c.Check(pair, "Dual Wielding: the two test weapons were a pair before the swim");
            }
            DiveInput.TestDown = false;

            // ---- Jump tapped at the floor: only up (T14; the touch of the floor faked, see dive.ceiling for a real one) ----
            var jumpSkill = rig.SaveSkill(Skills.SkillType.Jump);
            var skillLevel = jumpSkill.m_level;
            var skillProgress = jumpSkill.m_accumulator;
            var profile = Game.instance.GetPlayerProfile();
            var jumps = profile.GetStat(PlayerStatType.Jumps);
            var jumpTimer = player.m_jumpTimer;
            var jumped = false;
            // The jump sound and puff are the player's jump effects: every time the game plays them is counted
            // (Harmony prefix on EffectList.Create, this player's m_jumpEffects only), taken off again below.
            var effectCounter = new HarmonyLib.Harmony(ModInfo.Guid + ".selftest.jump");
            rig.Undo("jump effect counter", () =>
            {
                effectCounter.UnpatchSelf();
                _jumpEffects = null;
            });
            _jumpEffects = player.m_jumpEffects;
            _jumpEffectsPlayed = 0;
            effectCounter.Patch(HarmonyLib.AccessTools.Method(typeof(EffectList), nameof(EffectList.Create)),
                prefix: new HarmonyLib.HarmonyMethod(typeof(SelfTests), nameof(CountJumpEffects)));
            rig.Refill();
            a = player.GetStamina();
            var yTap = player.transform.position.y;
            var tapTicks = 0;
            for (var press = 0; press < 4; press++)
            {
                for (var i = 0; i < 16; i++)
                {
                    player.m_hitWorldTime = 0f;
                    DiveInput.TestUp = i < 8;
                    rig.Drive(Vector3.zero, false, i == 0, false);
                    yield return FixedTick;
                    tapTicks++;
                    jumped |= player.m_jumpTimer < jumpTimer;
                    jumpTimer = player.m_jumpTimer;
                }
            }
            DiveInput.TestUp = false;
            rig.Drive(Vector3.zero);
            var used = a - player.GetStamina();
            var soundsUnder = _jumpEffectsPlayed;
            c.Check(!jumped && profile.GetStat(PlayerStatType.Jumps) == jumps && jumpSkill.m_level == skillLevel
                    && jumpSkill.m_accumulator == skillProgress && used <= drain * tapTicks * dt * 1.15f + 0.5f
                    && player.transform.position.y > yTap + 0.05f,
                $"Jump tapped 4 times at the sea floor: only up ({F(player.transform.position.y - yTap)} m), no jump (jump {jumped}), "
                + $"no jump stamina ({F(used)} in {F(tapTicks * dt)} s, swim drain {F(drain)} per second), no Jump skill");

            // ---- Jump held: back up (T08) ----
            rig.Refill();
            var yUp = player.transform.position.y;
            DiveInput.TestUp = true;
            yield return Ticks(75);
            var rose = player.transform.position.y - yUp;
            yield return Surface(player, 8f, box);
            c.Check(rose > 1f && box.Ok, $"Jump held at the sea floor: up {F(rose)} m in 1.5 s, back at the surface: {box.Detail}");
            rig.GiveStaminaRateBack();
            yield return new WaitForSeconds(1f);

            // ---- no jump sound under water (T14). Control: the same Jump with the same faked touch at the surface, not
            // diving, is a real jump and plays the jump effects, so the counter does count. ----
            _jumpEffectsPlayed = 0;
            jumpTimer = player.m_jumpTimer;
            var control = false;
            for (var i = 0; i < 6 && !control; i++)
            {
                player.m_hitWorldTime = 0f;
                rig.Drive(Vector3.zero, false, true, false);
                yield return FixedTick;
                control |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
            }
            rig.Drive(Vector3.zero);
            var soundsControl = _jumpEffectsPlayed;
            effectCounter.UnpatchSelf();
            _jumpEffects = null;
            c.Check(control && soundsControl >= 1 && soundsUnder == 0,
                $"the jump sound: played {soundsUnder} time(s) during the 4 Jump taps at the sea floor, and {soundsControl} time(s) "
                + $"for a real jump at the surface afterwards (same key, same faked touch, not diving: jump {control})");
            yield return new WaitForSeconds(2f);

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.ceiling ----------

    // Spawned ship for the hull and deck checks (prefab Karve: 1.0.16 runtime dump, used by the Sailing tests too).
    private sealed class TestShip
    {
        internal GameObject Go;
        internal Ship Boat;
        private ZDO _zdo;

        internal bool Spawn(Vector3 position, Quaternion rotation)
        {
            var scene = ZNetScene.instance;
            var prefab = scene != null ? scene.GetPrefab("Karve") : null;
            if (prefab == null || prefab.GetComponent<Ship>() == null)
            {
                prefab = null;
                if (scene != null)
                {
                    foreach (var p in scene.m_prefabs)
                    {
                        if (p != null && p.GetComponent<Ship>() != null)
                        {
                            prefab = p;
                            break;
                        }
                    }
                }
            }
            if (prefab == null)
            {
                return false;
            }
            Go = UnityEngine.Object.Instantiate(prefab, position, rotation);
            Boat = Go.GetComponent<Ship>();
            var nview = Go.GetComponent<ZNetView>();
            _zdo = nview != null ? nview.GetZDO() : null;
            return Boat != null && _zdo != null;
        }

        // Player still counted in the ship's volume = taken out by hand (a destroyed ship sends no trigger exit).
        internal void Destroy(Player player)
        {
            if (Boat != null)
            {
                if (player != null && Boat.IsPlayerInBoat(player))
                {
                    Boat.m_players.Remove(player);
                    player.InNumShipVolumes = Mathf.Max(0, player.InNumShipVolumes - 1);
                }
                Ship.s_currentShips.Remove(Boat);
            }
            var scene = ZNetScene.instance;
            if (Go != null && scene != null)
            {
                scene.Destroy(Go);
            }
            else if (_zdo != null && _zdo.IsValid() && ZDOMan.instance != null)
            {
                _zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(_zdo);
            }
            Go = null;
            Boat = null;
            _zdo = null;
        }
    }

    private static IEnumerator RunCeiling()
    {
        yield return Settle();
        var s = Begin(CeilingName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            CalmWind(rig);
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            var here = player.transform.position;
            var level = player.GetLiquidLevel();
            var ship = new TestShip();
            rig.Undo("ship", () => ship.Destroy(player));
            if (!c.Check(ship.Spawn(new Vector3(here.x + 10f, level + 0.3f, here.z), Quaternion.identity),
                    "no ship prefab (Karve) or it did not spawn"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(3f);
            var boat = ship.Boat;
            if (!c.Check(boat != null && boat.m_body != null && boat.transform.up.y > 0.8f, "the spawned ship floats upright"))
            {
                c.Report();
                yield break;
            }
            // Hull stays where it floats (a diver pushing up would lift a free ship). Frozen, not kinematic: the ship
            // writes its body speed every physics tick, and Unity warns twice per tick about that on a kinematic body
            // (2522 warning lines in the run of 2026-10-07).
            boat.m_body.constraints = RigidbodyConstraints.FreezeAll;
            var centre = boat.transform.position;
            c.Note($"{Utils.GetPrefabName(ship.Go)} floats at height {F(centre.y)} (water {F(level)})");

            // ---- under the hull: sideways swimming allowed (T13, D5) ----
            MovePlayer(player, new Vector3(centre.x, Rest(player) - 3.5f, centre.z));
            player.SetLookDir(Flat(boat.transform.forward));
            yield return StartDive();
            yield return Ticks(50);
            c.Check(DiveState.Diving && DiveState.Deep && DiveState.BlockedAbove,
                $"diver under the hull: something solid above (diving {DiveState.Diving}, deep {DiveState.Deep}, above "
                + $"{DiveState.BlockedAbove}, {F(Under(player))} m under the rest height)");
            var path = 0f;
            var keptAll = true;
            var blockedAll = true;
            var last = Flat(player.transform.position);
            foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
            {
                // Along the keel half a second each way, across the (narrow) hull a little less.
                var steps = dir.x != 0f ? 15 : 25;
                for (var i = 0; i < steps; i++)
                {
                    rig.Drive(dir);
                    yield return FixedTick;
                    var now = Flat(player.transform.position);
                    path += Vector3.Distance(last, now);
                    last = now;
                    keptAll &= player.m_moveDir.sqrMagnitude > 0.25f;
                    blockedAll &= DiveState.BlockedAbove;
                }
            }
            c.Check(blockedAll && keptAll && path > 0.6f,
                $"under the hull W, S, D, A swim sideways ({F(path)} m swum in 1.6 s, under the hull all the time {blockedAll}, "
                + $"movement kept {keptAll})");
            // Out from under, across the ship.
            var t0 = Time.time;
            while (DiveState.BlockedAbove && Time.time - t0 < 6f)
            {
                rig.Drive(Vector3.right);
                yield return FixedTick;
            }
            var freeAfter = Time.time - t0;
            var free = !DiveState.BlockedAbove && DiveState.Diving && DiveState.Deep;
            var locked = 0;
            for (var i = 0; i < 100; i++)
            {
                rig.Drive(Vector3.right);
                yield return FixedTick;
                if (player.m_moveDir.sqrMagnitude < 0.0001f && !DiveState.BlockedAbove)
                {
                    locked++;
                }
            }
            var p1 = Flat(player.transform.position);
            for (var i = 0; i < 50; i++)
            {
                rig.Drive(Vector3.right);
                yield return FixedTick;
            }
            rig.Drive(Vector3.zero);
            var coast = Vector3.Distance(p1, Flat(player.transform.position));
            c.Check(free, $"swimming sideways gets the diver out from under the hull ({F(freeAfter)} s)");
            c.Check(free && DiveState.Deep && locked == 100 && coast < 0.15f,
                $"nothing above any more: the movement keys do nothing again (movement dropped on {locked} of 100 ticks, {F(coast)} m "
                + "moved in the third second)");

            // ---- Jump pressed while really touching the hull under water: no jump (T14) ----
            // Under the hull again, Jump held: up into it. From the first touch the Jump key goes down again every 6th
            // tick (the game's own jump input), for as long as the diver touches the hull or until 0.8 s of touch.
            MovePlayer(player, new Vector3(centre.x, Rest(player) - 3f, centre.z));
            yield return StartDive();
            yield return Ticks(30);
            var profile = Game.instance.GetPlayerProfile();
            var jumps = profile.GetStat(PlayerStatType.Jumps);
            var jumpTimer = player.m_jumpTimer;
            var jumped = false;
            var contact = 0;
            var presses = 0;
            var rise = 0;
            DiveInput.TestUp = true;
            while (rise < 250 && DiveState.Diving && contact < 40)
            {
                var press = contact > 0 && contact % 6 == 1;
                rig.Drive(Vector3.zero, false, press, false);
                yield return FixedTick;
                rise++;
                if (press)
                {
                    presses++;
                }
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
                if (DiveState.Diving && player.m_hitWorldTime < 0.1f)
                {
                    contact++;
                }
            }
            rig.Drive(Vector3.zero);
            DiveInput.TestUp = false;
            c.Check(contact >= 12 && presses >= 2 && !jumped && profile.GetStat(PlayerStatType.Jumps) == jumps,
                $"Jump pressed {presses} times while the diver touches the hull under water (touching on {contact} ticks): no jump "
                + $"(jump {jumped}, jumps counted {F(profile.GetStat(PlayerStatType.Jumps) - jumps)})");
            c.Note($"rising into the hull: {(contact >= 40 ? "the diver stayed against it" : "the diver slid along it")} "
                   + $"({F(Under(player))} m under the rest height after {F(rise * Time.fixedDeltaTime)} s, diving {DiveState.Diving})");
            yield return Ticks(10);

            // ---- under a built floor: a solid slab on the building layer stands in for a dock (T13) ----
            MovePlayer(player, new Vector3(centre.x - 14f, Rest(player) - 3f, centre.z));
            yield return StartDive();
            yield return Ticks(60);
            var openWater = DiveState.Diving && DiveState.Deep && !DiveState.BlockedAbove;
            var layer = LayerMask.NameToLayer("piece");
            var slab = new GameObject("MC_SwimDive_SelfTest_Slab");
            rig.Undo("slab", () =>
            {
                if (slab != null)
                {
                    UnityEngine.Object.Destroy(slab);
                }
            });
            if (layer >= 0)
            {
                slab.layer = layer;
            }
            slab.transform.position = new Vector3(player.transform.position.x, player.GetLiquidLevel() + 0.1f, player.transform.position.z);
            slab.AddComponent<BoxCollider>().size = new Vector3(6f, 0.3f, 6f);
            yield return Ticks(3);
            var underSlab = DiveState.BlockedAbove;
            path = 0f;
            keptAll = true;
            last = Flat(player.transform.position);
            for (var i = 0; i < 50; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
                var now = Flat(player.transform.position);
                path += Vector3.Distance(last, now);
                last = now;
                keptAll &= player.m_moveDir.sqrMagnitude > 0.25f;
            }
            rig.Drive(Vector3.zero);
            c.Check(layer >= 0 && openWater && underSlab && keptAll && path > 0.3f,
                $"under a floor on the building layer over the water: W swims sideways ({F(path)} m in 1 s; open water before "
                + $"{openWater}, floor seen above {underSlab})");
            slab.SetActive(false);
            UnityEngine.Object.Destroy(slab);
            slab = null;
            yield return null;
            yield return Ticks(3);
            var slabGone = !DiveState.BlockedAbove;
            locked = 0;
            for (var i = 0; i < 100; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
                if (player.m_moveDir.sqrMagnitude < 0.0001f)
                {
                    locked++;
                }
            }
            p1 = Flat(player.transform.position);
            for (var i = 0; i < 50; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            rig.Drive(Vector3.zero);
            coast = Vector3.Distance(p1, Flat(player.transform.position));
            c.Check(slabGone && DiveState.Deep && locked == 100 && coast < 0.15f,
                $"floor gone: the movement keys do nothing again (movement dropped on {locked} of 100 ticks, {F(coast)} m moved in "
                + "the third second)");
            yield return Surface(player, 6f, box);

            // ---- on the ship's deck: Crouch crouches, no dive (T29) ----
            var controls = boat.m_shipControlls;
            var helm = controls != null ? controls.m_attachPoint : null;
            var deck = helm != null ? Vector3.Lerp(centre, helm.position, 0.5f) : centre;
            MovePlayer(player, new Vector3(deck.x, Mathf.Max(deck.y, centre.y) + 1.5f, deck.z));
            t0 = Time.time;
            while ((player.GetStandingOnShip() != boat || player.IsSwimming()) && Time.time - t0 < 5f)
            {
                yield return FixedTick;
            }
            if (c.Check(player.GetStandingOnShip() == boat && !player.IsSwimming(),
                    $"standing on the ship's deck (on the ship {player.GetStandingOnShip() == boat}, swimming {player.IsSwimming()})"))
            {
                yield return Ticks(10);
                var deckY = player.transform.position.y;
                var dived = false;
                DiveInput.TestDown = true;
                rig.Drive(Vector3.zero, true, false, false);
                for (var i = 0; i < 75; i++)
                {
                    yield return FixedTick;
                    rig.Drive(Vector3.zero);
                    dived |= DiveState.Diving;
                }
                DiveInput.TestDown = false;
                c.Check(!dived && player.GetStandingOnShip() == boat && Mathf.Abs(player.transform.position.y - deckY) < 0.3f
                        && player.m_crouchToggled,
                    $"Crouch held on the deck over deep water: no dive, the player crouches as usual (dive {dived}, crouching "
                    + $"{player.m_crouchToggled})");
                // Stand up again.
                rig.Drive(Vector3.zero, true, false, false);
                yield return FixedTick;
                rig.Drive(Vector3.zero);
            }

            // ---- sitting on the ship: no dive (T29) ----
            var chair = ship.Go.GetComponentInChildren<Chair>();
            var attach = chair != null ? chair.m_attachPoint : helm;
            if (c.Check(attach != null, "the ship has a seat or a helm to sit at"))
            {
                rig.Undo("seat", () =>
                {
                    if (player.IsAttached())
                    {
                        player.AttachStop();
                    }
                });
                player.AttachStart(attach, null, false, false, chair == null || chair.m_inShip,
                    chair != null ? chair.m_attachAnimation : controls.m_attachAnimation,
                    chair != null ? chair.m_detachOffset : controls.m_detachOffset);
                yield return Ticks(5);
                var dived = false;
                DiveInput.TestDown = true;
                for (var i = 0; i < 75; i++)
                {
                    yield return FixedTick;
                    dived |= DiveState.Diving;
                }
                DiveInput.TestDown = false;
                c.Check(player.IsAttached() && !dived,
                    $"sitting {(chair != null ? "in a seat of the ship" : "at the helm (this ship has no seat)")} with Crouch held: "
                    + $"no dive (dive {dived})");
                player.AttachStop();
                yield return Ticks(5);
            }

            // ---- back in the water beside the ship: Crouch dives (T29) ----
            MovePlayer(player, new Vector3(centre.x + 7f, level + 0.3f, centre.z));
            t0 = Time.time;
            while (!player.IsSwimming() && Time.time - t0 < 5f)
            {
                yield return FixedTick;
            }
            yield return new WaitForSeconds(1.5f);
            yield return DiveTo(player, 1.5f, 5f, box);
            c.Check(box.Ok, "in the water beside the ship: Crouch dives: " + box.Detail);
            yield return Surface(player, 6f, box);
            ship.Destroy(player);

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.shallow ----------

    // Loaded terrain around a point: the spot whose real calm-water depth is closest to `want` (within `tolerance`), on
    // a gentle floor (the 4 points 0.7 m around within 0.2 m of it).
    private static bool FindRealDepth(Vector3 around, float radius, float want, float tolerance, out Vector3 found, out float depth)
    {
        var zone = ZoneSystem.instance;
        var level = zone.m_waterLevel;
        var best = float.MaxValue;
        found = around;
        depth = 0f;
        for (var dx = -radius; dx <= radius; dx += 0.5f)
        {
            for (var dz = -radius; dz <= radius; dz += 0.5f)
            {
                var p = new Vector3(around.x + dx, level, around.z + dz);
                if (!zone.GetGroundHeight(p, out var h))
                {
                    continue;
                }
                var d = level - h;
                var off = Mathf.Abs(d - want);
                if (off > tolerance || off >= best)
                {
                    continue;
                }
                if (!GentleFloor(zone, p, d, level))
                {
                    continue;
                }
                best = off;
                found = p;
                depth = d;
            }
        }
        return best < float.MaxValue;
    }

    private static bool GentleFloor(ZoneSystem zone, Vector3 p, float depth, float level)
    {
        for (var i = 0; i < 4; i++)
        {
            var q = p + new Vector3(i == 0 ? 0.7f : i == 1 ? -0.7f : 0f, 0f, i == 2 ? 0.7f : i == 3 ? -0.7f : 0f);
            if (!zone.GetGroundHeight(q, out var h) || Mathf.Abs(level - h - depth) > 0.2f)
            {
                return false;
            }
        }
        return true;
    }

    private static IEnumerator RunShallow()
    {
        yield return Settle();
        var s = Begin(ShallowName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            // The shallow checks are about the floor, not about waves: calm sea, rest height = water level.
            CalmWind(rig);
            if (!FindWater(rig.Origin, 1.75f, 2.05f, 2f, 0.6f, out var spot, out var mapDepth))
            {
                c.Check(false, "no water about 2 m deep on a gentle slope within 5 km of the start: not tested");
                c.Report();
                yield break;
            }
            c.Note($"shallow water about {F(mapDepth)} m deep at {F(spot)}, {F(Vector3.Distance(Flat(spot), Flat(rig.Origin)))} m "
                   + "from the start (WorldGenerator)");
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var arrival = player.transform.position;
            var level = ZoneSystem.instance.m_waterLevel;
            rig.TakeStaminaRate();
            yield return new WaitForSeconds(0.4f);
            var drain = SwimDrain(player);
            c.Note($"swim drain {F(drain)} per second, wind {F(EnvMan.instance.GetWindIntensity())}");

            foreach (var want in new[] { 1.6f, 2f })
            {
                if (!c.Check(FindRealDepth(arrival, 24f, want, 0.04f, out var p, out var depth),
                        $"no spot {F(want)} m deep on a gentle floor within 24 m of the arrival point: {F(want)} m not tested"))
                {
                    continue;
                }
                MovePlayer(player, new Vector3(p.x, level - 1.45f, p.z));
                rig.Drive(Vector3.zero);
                yield return new WaitForSeconds(2.5f);
                if (!c.Check(player.IsSwimming() && !DiveState.Diving && Mathf.Abs(Under(player)) < 0.3f,
                        $"{F(want)} m water: swimming at the rest height before Crouch (swimming {player.IsSwimming()}, "
                        + $"{F(Under(player))} m under it)"))
                {
                    continue;
                }
                c.Note($"{F(want)} m water: real depth {F(depth)} m, floor {F(FloorGap(player))} m under the feet, liquid level "
                       + $"{F(player.GetLiquidLevel())}");
                rig.Refill();
                var before = player.GetStamina();
                var afterTwo = before;
                var ticks = 0;
                var deepTicks = 0;
                var ownTicks = 0;
                var lastOwn = 0;
                var retakes = 0;
                var maxUnder = float.NegativeInfinity;
                var wasHanded = false;
                DiveInput.TestUp = false;
                DiveInput.TestDown = true;
                for (var i = 0; i < 500; i++)
                {
                    yield return FixedTick;
                    ticks++;
                    if (DiveState.Deep)
                    {
                        deepTicks++;
                    }
                    var own = DiveState.Diving && !DiveState.HandedBack;
                    if (own)
                    {
                        ownTicks++;
                        lastOwn = ticks;
                        if (wasHanded && ticks > 100)
                        {
                            retakes++;
                        }
                    }
                    wasHanded = DiveState.Diving && DiveState.HandedBack;
                    maxUnder = Mathf.Max(maxUnder, Under(player));
                    if (ticks == 100)
                    {
                        afterTwo = player.GetStamina();
                    }
                }
                var after = player.GetStamina();
                var heldDiving = DiveState.Diving;
                var endUnder = Under(player);
                DiveInput.TestDown = false;
                var facts = $"deep {deepTicks} of {ticks} ticks, own vertical speed on {ownTicks} ticks (last at {F(lastOwn * Time.fixedDeltaTime)} s), "
                            + $"taken again after 2 s {retakes} time(s), feet at most {F(maxUnder)} m under the rest height, "
                            + $"{F(endUnder)} m at the end, stamina {F(before)} -> {F(afterTwo)} (2 s) -> {F(after)} (10 s)";
                if (want < 1.8f)
                {
                    c.Check(heldDiving && deepTicks == 0 && ownTicks == 0 && maxUnder < 0.45f && Mathf.Abs(endUnder) < 0.3f,
                        $"{F(want)} m water, Crouch held 10 s: the swimmer floats at the normal height ({facts})");
                    c.Check(Mathf.Abs(after - before) < 0.2f, $"{F(want)} m water, Crouch held 10 s: the stamina bar stays full ({facts})");
                }
                else
                {
                    c.Check(heldDiving && deepTicks == 0 && lastOwn <= 100 && retakes == 0 && maxUnder < 0.5f && Mathf.Abs(endUnder) < 0.3f,
                        $"{F(want)} m water, Crouch held 10 s: at most a first small dip, then floating at the normal height with no "
                        + $"bobbing ({facts})");
                    c.Check(before - after < 4f && Mathf.Abs(afterTwo - after) < 0.2f,
                        $"{F(want)} m water, Crouch held 10 s: a small cost for the first dip at most, then no drain ({facts})");
                }
                yield return new WaitForSeconds(1.5f);
            }
            rig.GiveStaminaRateBack();

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.ashlands ----------

    // Hot Ashlands sea (WorldGenerator, no zone needed): inside the Ashlands border where the heat gradient is 0.35 to
    // 0.9, water at least MinWaterDepth deep, columns south of the world centre.
    private static bool FindAshlandsWater(out Vector3 spot, out float depth, out float gradient)
    {
        var gen = WorldGenerator.instance;
        var level = ZoneSystem.instance.m_waterLevel;
        for (var column = 0; column <= 12; column++)
        {
            var x = (column % 2 == 0 ? 1f : -1f) * ((column + 1) / 2) * 400f;
            for (var z = -7600f; z >= -9800f; z -= 20f)
            {
                if (x * x + z * z > 9900f * 9900f)
                {
                    break;
                }
                var g = WorldGenerator.GetAshlandsOceanGradient(x, z);
                if (g < 0.35f)
                {
                    continue;
                }
                if (g > 0.9f)
                {
                    break;
                }
                var d = level - gen.GetHeight(x, z);
                if (d < MinWaterDepth || gen.GetBiome(x, z) != Heightmap.Biome.AshLands)
                {
                    continue;
                }
                if (level - gen.GetHeight(x + 12f, z) < MinWaterDepth - 4f || level - gen.GetHeight(x - 12f, z) < MinWaterDepth - 4f
                    || level - gen.GetHeight(x, z + 12f) < MinWaterDepth - 4f || level - gen.GetHeight(x, z - 12f) < MinWaterDepth - 4f)
                {
                    continue;
                }
                spot = new Vector3(x, level, z);
                depth = d;
                gradient = g;
                return true;
            }
        }
        spot = Vector3.zero;
        depth = 0f;
        gradient = 0f;
        return false;
    }

    // Ashlands sea heat. The game adds (heat gradient of the place) x m_heatBuildupWater x (1 - heat resistance) per second
    // while swimming (Character.UpdateAshlandsWater). On the player m_heatBuildupWater is 0.025 (run of 2026-10-07; the
    // field default 2 in the code is not what the prefab has): from zero the burning level 0.7 takes 70 s at gradient
    // 0.4. So the heat is started `lead` under the burning level and the game's own build-up carries it over: seconds
    // until that level (Value, -1 = never in maxTicks), then the heat hits in the next hitTicks ticks (Point.x), the
    // damage of one (Point.y), and the heat gained while waiting (Point.z).
    private static IEnumerator MeasureHeat(Player p, float lead, int maxTicks, int hitTicks, Box result)
    {
        var threshold = p.m_heatLevelFirstDamageThreshold;
        var from = Mathf.Max(0f, threshold - lead);
        p.m_ashlandsOceanHeatLevel = from;
        var reach = -1;
        for (var i = 1; i <= maxTicks; i++)
        {
            yield return FixedTick;
            if (p.m_ashlandsOceanHeatLevel >= threshold - 0.0001f)
            {
                reach = i;
                break;
            }
        }
        var gained = p.m_ashlandsOceanHeatLevel - from;
        var last = p.m_lastHit;
        var hits = 0;
        var damage = 0f;
        for (var i = 0; i < hitTicks; i++)
        {
            yield return FixedTick;
            if (NewHit(p, ref last, HitData.HitType.AshlandsOcean))
            {
                hits++;
                damage = last.GetTotalDamage();
            }
        }
        result.Ok = reach > 0;
        result.Value = reach > 0 ? reach * Time.fixedDeltaTime : -1f;
        result.Point = new Vector3(hits, damage, gained);
    }

    private static IEnumerator RunAshlands()
    {
        yield return Settle();
        var s = Begin(AshlandsName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            CalmWind(rig);
            if (!FindAshlandsWater(out var spot, out var mapDepth, out var gradient))
            {
                c.Check(false, "no deep hot water found at the Ashlands border (WorldGenerator): not tested");
                c.Report();
                yield break;
            }
            c.Note($"hot Ashlands sea at {F(spot)}, about {F(mapDepth)} m deep, heat gradient {F(gradient)} (WorldGenerator)");
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            rig.SaveHealth();
            rig.Undo("sea heat", () => player.m_ashlandsOceanHeatLevel = 0f);
            player.SetHealth(player.GetMaxHealth());
            var here = WorldGenerator.GetAshlandsOceanGradient(player.transform.position);
            // Heat per second by the game's formula (Character.UpdateAshlandsWater, swimming).
            var rate = Mathf.Clamp01(here) * player.m_heatBuildupWater * (1f - player.GetEquipmentHeatResistanceModifier());
            c.Note($"heat gradient at the swimmer {F(here)}, burning starts at heat {F(player.m_heatLevelFirstDamageThreshold)}, "
                   + $"build-up in water {F(player.m_heatBuildupWater)} x gradient = {F5(rate)} "
                   + $"heat per second by the game's formula (from zero: {F(rate > 0f ? player.m_heatLevelFirstDamageThreshold / rate : -1f)} s), "
                   + $"fire tolerant {player.m_tolerateFire}");
            if (!c.Check(rate > 0.0005f && !player.m_tolerateFire,
                    $"the hot sea heats a swimmer here by the game's formula ({F5(rate)} per second)"))
            {
                c.Report();
                yield break;
            }
            // Started 2 s of build-up under the burning level (not at zero: that takes over a minute here).
            var lead = rate * 2f;

            // ---- at the surface: the heat as the normal game has it ----
            var top = new Box();
            yield return MeasureHeat(player, lead, 400, 150, top);
            if (!c.Check(top.Ok && top.Point.x >= 1f,
                    $"swimming at the surface of the hot sea: the heat, started 2 s of build-up under the burning level, reaches it "
                    + $"after {F(top.Value)} s and burns ({F(top.Point.x)} hits in 3 s, {F(top.Point.y)} each; heat gained while "
                    + $"waiting {F5(top.Point.z)})"))
            {
                c.Report();
                yield break;
            }

            // ---- diving works there, and burns the same ----
            player.SetHealth(player.GetMaxHealth());
            yield return DiveTo(player, 4f, 6f, box);
            c.Check(box.Ok, "hot Ashlands sea: Crouch dives: " + box.Detail);
            yield return Ticks(65);
            var deep = new Box();
            var underBefore = Under(player);
            yield return MeasureHeat(player, lead, 400, 150, deep);
            c.Check(DiveState.Diving && DiveState.Deep && underBefore > 3f && Under(player) > 3f && player.IsSwimming() && player.InWater(),
                $"holding at {F(Under(player))} m under the rest height in the hot sea (was {F(underBefore)} m before the heat was measured)");
            c.Check(deep.Ok && Mathf.Abs(deep.Value - top.Value) <= Mathf.Max(0.1f, 0.1f * top.Value),
                $"at depth the heat, started the same way, reaches the burning level after {F(deep.Value)} s, as at the surface "
                + $"({F(top.Value)} s): it builds up no faster and no slower");
            c.Check(deep.Point.x >= 1f && Mathf.Abs(deep.Point.x - top.Point.x) <= 2f
                    && Mathf.Abs(deep.Point.y - top.Point.y) <= 0.05f * top.Point.y + 0.01f,
                $"at depth the heat burns as at the surface: {F(deep.Point.x)} hits in 3 s of {F(deep.Point.y)} each (surface "
                + $"{F(top.Point.x)} of {F(top.Point.y)})");
            player.SetHealth(player.GetMaxHealth());
            yield return Surface(player, 8f, box);
            c.Check(box.Ok, "hot Ashlands sea: Jump rises back to the surface: " + box.Detail);

            yield return GoHome(s);
            player.m_ashlandsOceanHeatLevel = 0f;
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.bottom ----------

    // Sea deeper than the water volume (its trigger reaches 50 m under the water level in 1.0.16): only in the ring at the
    // world edge where the floor drops, inside the 10420 m line where the game kills swimmers below 40 m.
    private static bool FindEdgeDeep(out Vector3 spot, out float depth)
    {
        var gen = WorldGenerator.instance;
        var level = ZoneSystem.instance.m_waterLevel;
        for (var r = 10300f; r <= 10381f; r += 40f)
        {
            for (var a = 0; a < 360; a += 5)
            {
                var x = Mathf.Sin(a * Mathf.Deg2Rad) * r;
                var z = Mathf.Cos(a * Mathf.Deg2Rad) * r;
                // Plain sea only: not the hot Ashlands side (south), not the Deep North side (the game calls its sea
                // Ocean too, so the place itself is asked).
                var biome = gen.GetBiome(x, z);
                if (biome == Heightmap.Biome.AshLands || biome == Heightmap.Biome.DeepNorth || WorldGenerator.IsAshlands(x, z)
                    || WorldGenerator.IsDeepnorth(x, z))
                {
                    continue;
                }
                var d = level - gen.GetHeight(x, z);
                if (d < 55f)
                {
                    continue;
                }
                if (level - gen.GetHeight(x + 15f, z) < 53f || level - gen.GetHeight(x - 15f, z) < 53f
                    || level - gen.GetHeight(x, z + 15f) < 53f || level - gen.GetHeight(x, z - 15f) < 53f)
                {
                    continue;
                }
                spot = new Vector3(x, level, z);
                depth = d;
                return true;
            }
        }
        spot = Vector3.zero;
        depth = 0f;
        return false;
    }

    private static IEnumerator RunBottom()
    {
        yield return Settle();
        var s = Begin(BottomName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            // The game blows hard toward the world edge out there: calm sea for the test.
            CalmWind(rig);
            if (!FindEdgeDeep(out var spot, out var mapDepth))
            {
                c.Check(false, "no sea 55 m deep found in the ring 10300 to 10380 m from the world centre (WorldGenerator): not tested");
                c.Report();
                yield break;
            }
            c.Note($"world-edge sea at {F(spot)}, {F(Utils.LengthXZ(spot))} m from the world centre, about {F(mapDepth)} m deep "
                   + "(WorldGenerator)");
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var dt = Time.fixedDeltaTime;
            var box = new Box();
            var start = player.transform.position;
            var volume = VolumeAt(start);
            var ground = ZoneSystem.instance.GetGroundHeight(start);
            if (!c.Check(volume != null && volume.m_collider != null, "a water volume around the swimmer"))
            {
                c.Report();
                yield break;
            }
            var bottom = volume.m_collider.bounds.min.y;
            if (!c.Check(Utils.LengthXZ(start) < 10410f && bottom - ground >= 2f,
                    $"the sea floor ({F(ground)}) is under the bottom of the water volume ({F(bottom)}) here, "
                    + $"{F(Utils.LengthXZ(start))} m from the world centre"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new DiveRules { DiveSpeedMultiplier = DiveRules.DiveSpeedMax };
            rig.SaveHealth();
            var health = player.GetHealth();
            var hit = player.m_lastHit;
            var fallHit = false;

            // ---- Crouch held all the way down ----
            DiveInput.TestUp = false;
            DiveInput.TestDown = true;
            var ticks = 0;
            var still = 0;
            var lost = 0;
            var tooFar = false;
            while (ticks < 1250 && still < 25)
            {
                yield return FixedTick;
                ticks++;
                if (!(player.GetLiquidLevel() > -9000f && player.IsSwimming() && player.InWater()))
                {
                    lost++;
                }
                fallHit |= NewHit(player, ref hit, HitData.HitType.Fall);
                still = DiveState.Diving && Mathf.Abs(DiveState.Vy) < 0.001f && Under(player) > 10f ? still + 1 : 0;
                if (Utils.LengthXZ(player.transform.position) > 10415f)
                {
                    tooFar = true;
                    break;
                }
            }
            var feet = player.transform.position.y;
            var above = feet - bottom;
            c.Check(!tooFar, "the diver stayed inside the 10420 m line");
            c.Check(still >= 25 && above > 0.35f && above < 0.85f,
                $"Crouch held: the diver stops {F(above)} m above the bottom of the water ({F(bottom)}) after {F(ticks * dt)} s, "
                + $"{F(Under(player))} m under the rest height; the sea floor is {F(feet - ground)} m further down");
            var low = feet;
            var high = feet;
            var held = true;
            for (var i = 0; i < 100; i++)
            {
                yield return FixedTick;
                if (!(player.GetLiquidLevel() > -9000f && player.IsSwimming() && player.InWater()))
                {
                    lost++;
                }
                fallHit |= NewHit(player, ref hit, HitData.HitType.Fall);
                low = Mathf.Min(low, player.transform.position.y);
                high = Mathf.Max(high, player.transform.position.y);
                held &= DiveState.Diving && DiveState.Deep && !DiveState.HandedBack;
            }
            c.Check(lost == 0, $"in the water and swimming on every tick of the way down and at the bottom ({lost} tick(s) not)");
            c.Check(held && high - low < 0.1f, $"Crouch still held 2 s at the bottom of the water: stays there (moved {F(high - low)} m)");
            c.Check(!fallHit && player.GetHealth() >= health - 0.01f,
                $"no fall, no damage (health {F(health)} -> {F(player.GetHealth())}, fall hit {fallHit})");
            DiveInput.TestDown = false;

            // ---- Jump: back to the surface ----
            yield return Surface(player, 20f, box);
            yield return Ticks(50);
            c.Check(box.Ok && player.IsSwimming() && Mathf.Abs(Under(player)) < 0.5f,
                $"Jump held: back at the surface from the bottom of the water: {box.Detail}");

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.serpent ----------

    private static bool NewHitFrom(Player p, ref HitData last, Character attacker)
    {
        if (ReferenceEquals(p.m_lastHit, last))
        {
            return false;
        }
        last = p.m_lastHit;
        return last != null && attacker != null && last.GetAttacker() == attacker;
    }

    private static IEnumerator RunSerpent()
    {
        yield return Settle();
        var s = Begin(SerpentName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            CalmWind(rig);
            // Serpent: prefab name from the 1.0.16 runtime dump (TESTING.md, "Names checked").
            var prefab = ZNetScene.instance.GetPrefab("Serpent");
            if (!c.Check(prefab != null && prefab.GetComponent<Character>() != null, "the game has a Serpent creature"))
            {
                c.Report();
                yield break;
            }
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            // Night: sea serpents hunt at night.
            rig.TimeOfDay(0f);
            rig.SaveHealth();
            player.SetHealth(player.GetMaxHealth());
            var here = player.transform.position;
            GameObject go = UnityEngine.Object.Instantiate(prefab, new Vector3(here.x + 14f, player.GetLiquidLevel() - 0.5f, here.z),
                Quaternion.identity);
            rig.Undo("serpent", () =>
            {
                if (go != null && ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(go);
                }
            });
            var serpent = go.GetComponent<Character>();
            yield return new WaitForSeconds(1f);

            // ---- control: floating at the surface, does it bite? ----
            var last = player.m_lastHit;
            var bitten = false;
            var nearest = float.MaxValue;
            var t0 = Time.time;
            while (Time.time - t0 < 20f && !bitten && go != null)
            {
                yield return FixedTick;
                if (go == null || serpent == null)
                {
                    break;
                }
                bitten = NewHitFrom(player, ref last, serpent);
                nearest = Mathf.Min(nearest, Vector3.Distance(serpent.transform.position, player.GetCenterPoint()));
            }
            c.Note($"swimmer at the surface: bitten by the Serpent {bitten} after {F(Time.time - t0)} s (nearest {F(nearest)} m)");
            player.SetHealth(player.GetMaxHealth());

            // ---- 7 to 8 m down for 12 s ----
            yield return DiveTo(player, 7f, 8f, box);
            c.Check(box.Ok, "dive to 7 m: " + box.Detail);
            var bites = 0;
            var closest = float.MaxValue;
            var serpentDeepest = float.NegativeInfinity;
            t0 = Time.time;
            while (Time.time - t0 < 12f && go != null)
            {
                yield return FixedTick;
                if (go == null || serpent == null)
                {
                    break;
                }
                if (NewHitFrom(player, ref last, serpent))
                {
                    bites++;
                }
                closest = Mathf.Min(closest, Vector3.Distance(serpent.transform.position, player.GetCenterPoint()));
                serpentDeepest = Mathf.Max(serpentDeepest, player.GetLiquidLevel() - serpent.transform.position.y);
            }
            var facts = $"{bites} bite(s) in {F(Time.time - t0)} s at {F(Under(player))} m under the rest height, the Serpent came "
                        + $"within {F(closest)} m and went at most {F(serpentDeepest)} m under the surface";
            c.Note("diver under the Serpent: " + facts);
            if (bitten)
            {
                c.Check(bites == 0 && DiveState.Deep, $"a Serpent that bit the swimmer at the surface cannot bite the diver 7 m down ({facts})");
            }
            else
            {
                c.Note("the Serpent never bit the swimmer at the surface in 20 s, so the dive proves little this time: " + facts);
                c.Check(bites == 0 && DiveState.Deep, $"no Serpent bite 7 m down ({facts})");
            }
            yield return Surface(player, 10f, box);
            if (go != null)
            {
                ZNetScene.instance.Destroy(go);
                go = null;
            }
            player.SetHealth(player.GetMaxHealth());

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }
}
#endif
