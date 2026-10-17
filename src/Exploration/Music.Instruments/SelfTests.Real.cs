#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.MusicInstrumentsMod;

// Debug build only. Self tests that do the real thing where an older test took a shortcut or left a look or a
// listen to a person (same class as SelfTests.cs). One small test per thing:
//   music.drop.rest      T02  a dropped flute comes to rest and stays near where it landed: four throws (north, east,
//                             south, west) from one level open place, so where an earlier test left the player does
//                             not count
//   music.rhythm.drawn   T08  the picture on screen: each falling note is drawn in its lane's colour where the game
//                             has it, lower a moment later, in every one of the four lanes; the hit line crosses the
//                             four lanes (pixels read back)
//   music.hud.scale      T23  the rhythm game HUD at the player's GUI scale and at another one: size follows the
//                             scale, lanes right of the character and on screen, key labels under the line, judgement
//                             near it, meter text beside the lanes, stop hint under them; status line of a song too
//   music.volume.heard   T16  the sound at the game's output (after the mixer): the instrument is heard, and what the
//                             Sound effects and Master sliders call makes it much quieter there
//   music.listen.rest    M04  listener side by a real fire: another player's Encore within range gives Music, the
//                             game's own Resting shows comfort 3 higher and Rested 3 minutes longer, outdoors; an
//                             Encore from farther away gives nothing
internal static partial class SelfTests
{
    private const string DropRestName = "music.drop.rest";
    private const string RhythmDrawnName = "music.rhythm.drawn";
    private const string HudScaleName = "music.hud.scale";
    private const string VolumeHeardName = "music.volume.heard";
    private const string ListenRestName = "music.listen.rest";

    // ---------- music.drop.rest (T02) ----------

    // Four throws from one place, one per heading: the ground each throw reach is part of the result, so me pick that
    // place (level open ground near the player) instead of taking wherever an earlier test left the player.
    private static readonly float[] DropHeadings = { 0f, 90f, 180f, 270f };
    // Lane = strip a throw can reach: thrown about 5 m ahead, plus a roll long enough to fail the test.
    private const float DropLaneLength = 8f;
    private const float DropLaneHalf = 2f;
    // Spots looked at within this far of the player, every DropSpotStep m; ground read every DropMapStep m. Start area of
    // test world is stone circle in forest: 24 m found no place (run 2026-10-17), so me look far.
    private const float DropSearch = 64f;
    private const float DropSpotStep = 4f;
    private const float DropMapStep = 1f;
    // Level enough: most rise from one ground point to the next (about 14 degrees) and over one lane (about 10 degrees).
    private const float DropStepRise = 0.25f;
    private const float DropLaneRise = 1.5f;
    // A throw that lies still this long has come to rest; one still moving this long after landing fails.
    private const float DropRestSeconds = 1.5f;
    private const float DropRestLimit = 12f;

    // Ground heights round a spot, one ray from high above per point. Open = the ray hit terrain (not rock, tree,
    // building or anything else solid) above the water.
    private sealed class GroundMap
    {
        private readonly float _x0;
        private readonly float _z0;
        private readonly float _step;
        private readonly int _size;
        private readonly float[] _height;
        private readonly bool[] _open;

        internal GroundMap(Vector3 centre, float half, float step)
        {
            _step = step;
            _size = Mathf.CeilToInt(half * 2f / step) + 1;
            _x0 = centre.x - half;
            _z0 = centre.z - half;
            _height = new float[_size * _size];
            _open = new bool[_size * _size];
            var zone = ZoneSystem.instance;
            var terrain = LayerMask.NameToLayer("terrain");
            var top = centre.y + 80f;
            for (var i = 0; i < _size; i++)
            {
                for (var j = 0; j < _size; j++)
                {
                    var k = i * _size + j;
                    var from = new Vector3(_x0 + i * step, top, _z0 + j * step);
                    if (Physics.Raycast(from, Vector3.down, out var hit, 200f, zone.m_solidRayMask))
                    {
                        _height[k] = hit.point.y;
                        _open[k] = hit.collider != null && hit.collider.gameObject.layer == terrain && hit.point.y > zone.m_waterLevel + 0.5f;
                    }
                }
            }
        }

        // Nearest ground point; false = not open there (or outside the map).
        internal bool At(Vector3 p, out float height)
        {
            var i = Mathf.RoundToInt((p.x - _x0) / _step);
            var j = Mathf.RoundToInt((p.z - _z0) / _step);
            height = 0f;
            if (i < 0 || j < 0 || i >= _size || j >= _size || !_open[i * _size + j])
            {
                return false;
            }
            height = _height[i * _size + j];
            return true;
        }
    }

    // How the four lanes from a spot look: 0 = level and open (rise = worst lane), 1 = something in the way, 2 = too
    // steep.
    private static int DropLanes(GroundMap map, Vector3 spot, out float rise)
    {
        rise = 0f;
        foreach (var yaw in DropHeadings)
        {
            var ahead = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var side = Vector3.Cross(Vector3.up, ahead);
            var low = float.MaxValue;
            var high = float.MinValue;
            for (var s = -DropLaneHalf; s <= DropLaneHalf + 0.01f; s += DropMapStep)
            {
                var last = float.NaN;
                // From behind the player's feet to the lane's end.
                for (var t = -1f; t <= DropLaneLength + 0.01f; t += DropMapStep)
                {
                    if (!map.At(spot + ahead * t + side * s, out var h))
                    {
                        return 1;
                    }
                    if (!float.IsNaN(last) && Mathf.Abs(h - last) > DropStepRise)
                    {
                        return 2;
                    }
                    last = h;
                    low = Mathf.Min(low, h);
                    high = Mathf.Max(high, h);
                }
            }
            if (high - low > DropLaneRise)
            {
                return 2;
            }
            rise = Mathf.Max(rise, high - low);
        }
        return 0;
    }

    // Nearest spot to 'near' whose four lanes are level open ground. Place = on the ground there.
    private static bool FindDropPlace(Vector3 near, out Vector3 place, out string detail)
    {
        var map = new GroundMap(near, DropSearch + DropLaneLength + DropLaneHalf + 1f, DropMapStep);
        var spots = new List<Vector3>();
        for (var dx = -DropSearch; dx <= DropSearch + 0.01f; dx += DropSpotStep)
        {
            for (var dz = -DropSearch; dz <= DropSearch + 0.01f; dz += DropSpotStep)
            {
                if (dx * dx + dz * dz <= DropSearch * DropSearch + 0.01f)
                {
                    spots.Add(new Vector3(near.x + dx, near.y, near.z + dz));
                }
            }
        }
        spots.Sort((a, b) => Flat(a, near).CompareTo(Flat(b, near)));
        var blocked = 0;
        var steep = 0;
        foreach (var spot in spots)
        {
            var how = DropLanes(map, spot, out var rise);
            if (how == 0 && map.At(spot, out var ground))
            {
                place = new Vector3(spot.x, ground, spot.z);
                detail = $"{F(Flat(spot, near))} m from where the player stood, ground rises at most {F(rise)} m along a throw's lane; "
                         + $"{blocked} nearer spots had something in the way, {steep} were too steep";
                return true;
            }
            if (how == 1)
            {
                blocked++;
            }
            else
            {
                steep++;
            }
        }
        place = near;
        detail = $"none of {spots.Count} spots within {F(DropSearch)} m has level open ground {F(DropLaneLength)} m out in the four "
                 + $"headings ({blocked} with rock, tree, building or water in the way, {steep} too steep)";
        return false;
    }

    private static void Face(Player player, Rig rig, float yaw)
    {
        var turn = Quaternion.Euler(0f, yaw, 0f);
        rig.Look(yaw, 0f);
        player.transform.rotation = turn;
        if (player.m_body != null)
        {
            player.m_body.rotation = turn;
        }
    }

    private static HashSet<ItemDrop> FluteDrops()
    {
        var set = new HashSet<ItemDrop>();
        foreach (var d in ItemDrop.s_instances)
        {
            if (d != null && InstrumentContent.KindOf(d.m_itemData) == InstrumentKind.Flute)
            {
                set.Add(d);
            }
        }
        return set;
    }

    private static IEnumerator RunDropRest()
    {
        var c = new Checks(DropRestName);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null || ZNetScene.instance == null)
        {
            SelfTest.Fail(DropRestName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        var rotation = player.transform.rotation;
        try
        {
            rig.TakeControls();
            var stood = player.transform.position;
            if (!c.Check(FindDropPlace(stood, out var place, out var where), "level open ground for the throws near the player: " + where))
            {
                c.Report();
                yield break;
            }
            c.Note($"throws from {F(place)}: {where}");
            var noted = false;
            foreach (var yaw in DropHeadings)
            {
                var label = $"thrown toward {F(yaw)} degrees";
                PutPlayer(player, place + Vector3.up * 0.1f);
                Face(player, rig, yaw);
                yield return new WaitForSeconds(0.4f);
                var facing = player.transform.eulerAngles.y;
                if (Mathf.Abs(Mathf.DeltaAngle(facing, yaw)) > 5f || Flat(player.transform.position, place) > 0.5f)
                {
                    c.Note($"{label}: the player faces {F(facing)} degrees, stands {F(Flat(player.transform.position, place))} m from the place");
                }
                var before = FluteDrops();
                var item = rig.Give(InstrumentContent.ItemName(InstrumentKind.Flute));
                if (!c.Check(item != null && player.DropItem(player.GetInventory(), item, 1), label + ": a flute dropped through the game"))
                {
                    continue;
                }
                yield return Frames(2);
                ItemDrop drop = null;
                foreach (var d in FluteDrops())
                {
                    if (!before.Contains(d))
                    {
                        drop = d;
                    }
                }
                if (!c.Check(drop != null, label + ": the flute is an object in the world"))
                {
                    continue;
                }
                rig.Spawned(drop.gameObject);
                var body = drop.GetComponent<Rigidbody>();
                if (!noted)
                {
                    noted = true;
                    var collider = drop.GetComponentInChildren<Collider>();
                    c.Note($"flute body: mass {(body != null ? F(body.mass) : "-")}, damping {(body != null ? F(body.linearDamping) : "-")} linear, "
                           + $"{(body != null ? F(body.angularDamping) : "-")} angular; collider {(collider != null ? collider.GetType().Name : "none")}");
                }
                // It is thrown a little: where it lies once it has landed is where the rolling starts.
                yield return new WaitForSeconds(1.5f);
                if (!c.Check(drop != null, label + ": still there after landing"))
                {
                    continue;
                }
                var landed = drop.transform.position;
                var began = Time.time;
                var restSince = -1f;
                var rested = false;
                var speed = 0f;
                while (drop != null && Time.time - began < DropRestLimit)
                {
                    var still = body == null || body.IsSleeping();
                    speed = still ? 0f : body.linearVelocity.magnitude;
                    if (still || (speed < 0.02f && body.angularVelocity.magnitude < 0.5f))
                    {
                        if (restSince < 0f)
                        {
                            restSince = Time.time;
                        }
                        if (Time.time - restSince > DropRestSeconds)
                        {
                            rested = true;
                            break;
                        }
                    }
                    else
                    {
                        restSince = -1f;
                    }
                    yield return null;
                }
                if (!c.Check(drop != null, label + ": the flute was not lost while it lay there"))
                {
                    continue;
                }
                var end = drop.transform.position;
                var rolled = Flat(end, landed);
                var slope = SolidBelow(landed) - SolidBelow(end);
                var ground = ZoneSystem.instance.GetGroundHeight(end);
                c.Note($"{label}: landed at {F(landed)}, lies at {F(end)} after {F(Time.time - began)} s: {F(rolled)} m away, ground "
                       + $"{F(slope)} m lower there, {F(Flat(end, place))} m from the place, speed {F(speed)} m/s");
                c.Check(rested, $"{label}: the flute comes to rest within {F(DropRestLimit)} s of landing (speed now {F(speed)} m/s)");
                c.Check(rolled < 2f, $"{label}: it does not roll away: {F(rolled)} m from where it landed");
                c.Check(end.y > ground - 0.3f && end.y < ground + 0.5f, $"{label}: it lies on the ground, not under it ({F(end.y - ground)} m)");
                ZNetScene.instance.Destroy(drop.gameObject);
                yield return Frames(2);
            }
            c.Report();
        }
        finally
        {
            try
            {
                player.transform.rotation = rotation;
                if (player.m_body != null)
                {
                    player.m_body.rotation = rotation;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (facing) failed: {e.Message}");
            }
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.rhythm.drawn (T08: the picture of the falling notes) ----------

    // The lane's note colour (NoteField.Palette: amber, teal, crimson, moss green), told by which channels lead: holds
    // whatever the screen's brightness curve is.
    private static bool LaneColour(int lane, Color p)
    {
        switch (lane & 3)
        {
            case 0: return p.r > p.g + 0.08f && p.g > p.b + 0.08f;
            case 1: return p.g > p.r + 0.12f && p.b > p.r + 0.12f;
            case 2: return p.r > p.g + 0.2f && p.r > p.b + 0.2f;
            default: return p.g > p.r + 0.04f && p.r > p.b + 0.08f;
        }
    }

    private static float Bright(Color p) => Mathf.Max(p.r, Mathf.Max(p.g, p.b));

    private static Color PixelAt(Texture2D picture, float screenX, float screenY)
    {
        var px = Mathf.Clamp(Mathf.RoundToInt(screenX * picture.width / Mathf.Max(1, Screen.width)), 0, picture.width - 1);
        var py = Mathf.Clamp(Mathf.RoundToInt(screenY * picture.height / Mathf.Max(1, Screen.height)), 0, picture.height - 1);
        return picture.GetPixel(px, py);
    }

    // Where a note of the running game is drawn now, in note field units above the lane bottom (NoteField.Draw).
    private static float NoteHeight(MiniGame game, KeyValuePair<int, int> note) =>
        NoteField.HitY + (game.NoteTime(note.Key, note.Value) - game.Clock) / Mathf.Max(0.1f, game.LookAhead) * (NoteField.LaneH - NoteField.HitY);

    // No note of that lane is drawn within 'margin' units of a height.
    private static bool LaneFree(MiniGame game, int lane, float height, float margin)
    {
        foreach (var v in game.Visible)
        {
            if (game.Lane(v.Value) == lane && Mathf.Abs(NoteHeight(game, v) - height) < margin)
            {
                return false;
            }
        }
        foreach (var miss in game.RecentMisses)
        {
            if (game.Lane(miss.Key.Value) == lane && Mathf.Abs(NoteHeight(game, miss.Key) - height) < margin)
            {
                return false;
            }
        }
        return true;
    }

    private static IEnumerator RunRhythmDrawn()
    {
        var c = new Checks(RhythmDrawnName);
        var player = Player.m_localPlayer;
        if (player == null || Hud.instance == null)
        {
            SelfTest.Fail(RhythmDrawnName, "no player or HUD");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            PadInput.Test = false;
            Plugin.TestNoteSpeed = 1f;
            Hud.instance.m_userHidden = false;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            if (!c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), "performs: " + error))
            {
                c.Report();
                yield break;
            }
            yield return Frames(4);
            var game = Performance.Game;
            var looks = 0;
            var good = 0;
            var pictures = 0;
            var lanesSeen = new HashSet<int>();
            var heights = new Dictionary<long, List<float>>();
            var lineLanes = new HashSet<int>();
            var bad = new List<string>();
            var began = Time.realtimeSinceStartup;
            var nextAt = 0f;
            var shot = false;
            // Tune start use only lane 1 and 4 (lane 3 come 4.4 s in, lane 2 6.4 s in): 12 pictures in a row would
            // end before them. So: 12 pictures at least, then on till every lane had a note looked at (40 at most).
            while (game != null && Performance.Mode == PerformanceMode.MiniGame && Time.realtimeSinceStartup - began < 14f
                   && (pictures < 12 || (lanesSeen.Count < Chart.Lanes && pictures < 40)))
            {
                yield return new WaitForEndOfFrame();
                if (Time.realtimeSinceStartup < nextAt || !MiniGameHud.MiniShown || !MiniGameHud.CanvasOn)
                {
                    // One picture kept for a person, in a frame where me read none back.
                    if (!shot && good >= 2)
                    {
                        shot = true;
                        SelfTest.Screenshot(RhythmDrawnName, "falling-notes");
                    }
                    continue;
                }
                // Notes well inside the lanes: fully opaque there (they fade in at the top). Not in the 52 units over
                // the line: "Miss" text of the lane is written there (MiniGameHud judgement box), over the notes.
                var picks = new List<KeyValuePair<int, int>>();
                foreach (var v in game.Visible)
                {
                    var h = NoteHeight(game, v);
                    if (h > NoteField.HitY + 60f && h < NoteField.LaneH - 60f)
                    {
                        picks.Add(v);
                    }
                }
                if (picks.Count == 0)
                {
                    continue;
                }
                var box = MiniGameHud.ScreenBox("mini");
                var unit = box.width / NoteField.Width;
                var picture = ScreenCapture.CaptureScreenshotAsTexture();
                if (picture == null)
                {
                    continue;
                }
                pictures++;
                nextAt = Time.realtimeSinceStartup + 0.3f;
                try
                {
                    foreach (var v in picks)
                    {
                        var lane = game.Lane(v.Value);
                        var h = NoteHeight(game, v);
                        // An empty place of the same lane to compare with (over the judgement text too).
                        var free = -1f;
                        for (var probe = NoteField.HitY + 60f; probe < NoteField.LaneH - 50f; probe += 12f)
                        {
                            if (LaneFree(game, lane, probe, NoteField.NoteH * 1.6f))
                            {
                                free = probe;
                                break;
                            }
                        }
                        if (free < 0f)
                        {
                            continue;
                        }
                        var sx = box.xMin + NoteField.LaneCentre(lane) * unit;
                        var note = PixelAt(picture, sx, box.yMin + h * unit);
                        var empty = PixelAt(picture, sx, box.yMin + free * unit);
                        looks++;
                        lanesSeen.Add(lane);
                        if (LaneColour(lane, note) && Bright(note) > Bright(empty) + 0.12f)
                        {
                            good++;
                            var key = NoteKey(v);
                            if (!heights.TryGetValue(key, out var list))
                            {
                                list = new List<float>();
                                heights[key] = list;
                            }
                            list.Add(box.yMin + h * unit);
                        }
                        else if (bad.Count < 4)
                        {
                            bad.Add($"lane {lane + 1} at {F(h)} units: pixel ({F(note.r)}, {F(note.g)}, {F(note.b)}), empty lane ({F(empty.r)}, {F(empty.g)}, {F(empty.b)})");
                        }
                    }
                    // The hit line: brighter than the lane just above it, in each lane with no note near.
                    for (var lane = 0; lane < Chart.Lanes; lane++)
                    {
                        if (lineLanes.Contains(lane) || !LaneFree(game, lane, NoteField.HitY + 8f, 34f))
                        {
                            continue;
                        }
                        var sx = box.xMin + NoteField.LaneCentre(lane) * unit;
                        var line = PixelAt(picture, sx, box.yMin + NoteField.HitY * unit);
                        var above = PixelAt(picture, sx, box.yMin + (NoteField.HitY + 16f) * unit);
                        if (Bright(line) > Bright(above) + 0.15f)
                        {
                            lineLanes.Add(lane);
                        }
                    }
                }
                finally
                {
                    Object.Destroy(picture);
                }
            }
            var fallen = 0;
            var rising = 0;
            foreach (var list in heights.Values)
            {
                if (list.Count < 2)
                {
                    continue;
                }
                var down = true;
                for (var i = 1; i < list.Count; i++)
                {
                    down &= list[i] < list[i - 1] - 1f;
                }
                if (down)
                {
                    fallen++;
                }
                else
                {
                    rising++;
                }
            }
            c.Note($"{pictures} pictures of the screen read back, {looks} notes looked at, {good} found in their lane's colour, {fallen} notes seen lower from picture to picture; "
                   + $"screen {Screen.width}x{Screen.height}");
            foreach (var line in bad)
            {
                c.Note("not found: " + line);
            }
            c.Check(pictures >= 4 && looks >= 6, $"the rhythm game ran long enough to look at the screen ({pictures} pictures, {looks} notes)");
            c.Check(looks > 0 && good == looks, $"every note looked at is drawn in its lane's colour at the place the game has it ({good} of {looks})");
            c.Check(fallen >= 2 && rising == 0, $"the notes are drawn falling toward the line: lower on every later picture ({fallen} notes followed, {rising} not falling)");
            c.Check(lanesSeen.Count == Chart.Lanes, $"falling notes were looked at in each of the four lanes ({lanesSeen.Count} of {Chart.Lanes})");
            c.Check(lineLanes.Count == Chart.Lanes, $"the hit line is drawn across the four lanes (seen in {lineLanes.Count})");
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.hud.scale (T23: the HUD at two GUI scales) ----------

    private static Rect ScreenRect(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private static bool OnScreen(Rect r) => r.xMin >= -1f && r.yMin >= -1f && r.xMax <= Screen.width + 1f && r.yMax <= Screen.height + 1f;

    private static IEnumerator HudAtScale(Checks c, Player player, Camera camera, float scale, string label)
    {
        GuiScaler.SetScale(scale);
        yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
        if (!c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), label + ": performs: " + error))
        {
            yield break;
        }
        yield return Frames(5);
        var keys = AccessTools.Field(typeof(MiniGameHud), "Keys")?.GetValue(null) as TextMeshProUGUI[];
        var judgement = AccessTools.Field(typeof(MiniGameHud), "_judgement")?.GetValue(null) as TextMeshProUGUI;
        var meterState = AccessTools.Field(typeof(MiniGameHud), "_meterState")?.GetValue(null) as TextMeshProUGUI;
        var hint = AccessTools.Field(typeof(MiniGameHud), "_hint")?.GetValue(null) as TextMeshProUGUI;
        if (!c.Check(keys != null && keys.Length == Chart.Lanes && judgement != null && meterState != null && hint != null && MiniGameHud.MiniShown && MiniGameHud.CanvasOn,
                label + ": the rhythm game HUD is up and its texts were found"))
        {
            Performance.TestRequestStop();
            yield return Frames(3);
            yield break;
        }
        var box = MiniGameHud.ScreenBox("mini");
        var unit = box.width / NoteField.Width;
        var expected = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f) * scale;
        c.Note($"{label}: lanes on screen x {F(box.xMin)}..{F(box.xMax)}, y {F(box.yMin)}..{F(box.yMax)}; {F(unit)} pixels per HUD unit; screen {Screen.width}x{Screen.height}");
        c.Check(Mathf.Abs(unit - expected) < expected * 0.03f, $"{label}: the HUD is drawn at this scale ({F(unit)} pixels per unit, {F(expected)} expected)");
        var at = camera.WorldToScreenPoint(player.GetCenterPoint());
        c.Check(at.z > 0f && box.width > 10f && box.xMin > at.x, $"{label}: the lanes sit to the right of the character (it stays visible)");
        c.Check(OnScreen(box), $"{label}: the lanes are on the screen");
        var lineY = box.yMin + NoteField.HitY * unit;
        for (var lane = 0; lane < Chart.Lanes; lane++)
        {
            var key = keys[lane];
            var r = key != null ? ScreenRect(key.rectTransform) : new Rect(0f, 0f, 0f, 0f);
            var laneMin = box.xMin + NoteField.LaneX(lane) * unit;
            var laneMax = laneMin + NoteField.LaneW * unit;
            c.Check(key != null && key.gameObject.activeInHierarchy && key.text == LaneKeys.Label(lane) && key.text.Length > 0
                    && r.center.x > laneMin && r.center.x < laneMax && r.yMax < lineY && r.yMin >= box.yMin - 1f,
                $"{label}: key label '{(key != null ? key.text : "")}' sits under the line of lane {lane + 1}");
        }
        var state = ScreenRect(meterState.rectTransform);
        c.Check(meterState.gameObject.activeInHierarchy && !string.IsNullOrEmpty(meterState.text) && state.xMax <= box.xMin + 1f && state.xMin >= -1f
                && state.yMin >= box.yMin - 1f && state.yMax <= box.yMax + 1f,
            $"{label}: the meter's text ('{meterState.text}') is beside the lanes, on the screen");
        // Meter bar itself (left 16 units of the same drawing as the lanes) not measured here: no pixel of it is read
        // back, and comparing two layout constants can never fail. A person looks at the bar (TESTING.md T23).
        var hintBox = ScreenRect(hint.rectTransform);
        c.Check(hint.gameObject.activeInHierarchy && hintBox.yMax <= box.yMin + 1f && hintBox.yMin >= -1f && hintBox.center.x > box.xMin && hintBox.center.x < box.xMax,
            $"{label}: the stop hint ('{hint.text}') is under the lanes, on the screen");
        // A judgement (the first notes are left to pass: Miss).
        yield return WaitReal(() => MiniGameHud.JudgementShown.Length > 0 || Performance.Mode != PerformanceMode.MiniGame, Judge.LeadIn + 8f);
        if (c.Check(MiniGameHud.JudgementShown.Length > 0, label + ": a judgement is shown"))
        {
            var j = ScreenRect(judgement.rectTransform);
            c.Check(j.yMin > lineY - 1f && j.center.y - lineY < 70f * unit && j.center.x > box.xMin && j.center.x < box.xMax,
                $"{label}: '{MiniGameHud.JudgementShown}' is shown near the line, over the lanes");
        }
        Performance.TestRequestStop();
        yield return Frames(3);
        // A song playing by itself: the small line.
        c.Check(Performance.StartAuto(Preset("ravens-jig"), -1, out error), label + ": plays by itself: " + error);
        yield return new WaitForSeconds(0.6f);
        var auto = MiniGameHud.ScreenBox("auto");
        at = camera.WorldToScreenPoint(player.GetCenterPoint());
        c.Check(MiniGameHud.AutoShown && auto.width > 10f && auto.xMin > at.x && OnScreen(auto), $"{label}: the status line of a song sits to the right of the character, on the screen");
        Performance.TestRequestStop();
        yield return Frames(3);
    }

    private static IEnumerator RunHudScale()
    {
        var c = new Checks(HudScaleName);
        var player = Player.m_localPlayer;
        var camera = Utils.GetMainCamera();
        if (player == null || camera == null || Hud.instance == null)
        {
            SelfTest.Fail(HudScaleName, "no player, camera or HUD");
            yield break;
        }
        var scaleField = AccessTools.Field(typeof(GuiScaler), "m_largeGuiScale");
        if (scaleField == null || !(scaleField.GetValue(null) is float))
        {
            SelfTest.Fail(HudScaleName, "the game's GUI scale could not be read (GuiScaler.m_largeGuiScale): nothing was checked");
            yield break;
        }
        var own = (float)scaleField.GetValue(null);
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            PadInput.Test = false;
            Hud.instance.m_userHidden = false;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            // What the slider in Settings, Accessibility calls while it is moved (in memory: nothing is saved).
            var other = own > 0.95f ? 0.8f : 1.1f;
            yield return HudAtScale(c, player, camera, own, $"GUI scale {F(own * 100f)} % (this player's)");
            yield return HudAtScale(c, player, camera, other, $"GUI scale {F(other * 100f)} %");
            c.Report();
        }
        finally
        {
            GuiScaler.SetScale(own);
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.volume.heard (T16: the sound after the mixer) ----------

    // Mean energy of the sound at the game's output (after the mixer and the listener) over 'seconds'.
    private static IEnumerator OutputEnergy(float seconds, double[] energy)
    {
        var buffer = new float[1024];
        var sum = 0.0;
        var count = 0L;
        var end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            AudioListener.GetOutputData(buffer, 0);
            for (var i = 0; i < buffer.Length; i++)
            {
                sum += buffer[i] * buffer[i];
            }
            count += buffer.Length;
            yield return null;
        }
        energy[0] = count > 0 ? sum / count : 0.0;
    }

    private static string Db(double energy) => energy > 1e-12 ? F((float)(10.0 * Math.Log10(energy))) + " dB" : "silence";

    private static IEnumerator RunVolumeHeard()
    {
        var c = new Checks(VolumeHeardName);
        var player = Player.m_localPlayer;
        var mixer = AudioMan.instance != null ? AudioMan.instance.m_masterMixer : null;
        if (player == null || mixer == null)
        {
            SelfTest.Fail(VolumeHeardName, "no player or no audio mixer");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        var own = AudioMan.GetSFXVolume();
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            Plugin.TestVolume = 1f;
            // The game's music left as it is in every part, so it weighs the same in each.
            Plugin.TestGameMusicVolume = 1f;
            Performance.Repeat = true;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            c.Note($"listener volume {F(AudioListener.volume)}, this player's sound effects level {F(own)} (Master x Sound effects)");

            // Sliders full: the world alone, then with the flute.
            AudioMan.SetSFXVolume(1f);
            yield return Real(0.4f);
            var e = new double[1];
            yield return OutputEnergy(1.2f, e);
            var world = e[0];
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var error), "a song plays: " + error);
            yield return new WaitForSeconds(0.8f);
            var emitter = Performance.LocalEmitter;
            var source = emitter != null ? emitter.Source : null;
            if (!c.Check(source != null && AudioKit.SfxGroup != null && source.outputAudioMixerGroup == AudioKit.SfxGroup
                         && source.outputAudioMixerGroup.audioMixer == mixer && !source.ignoreListenerVolume && !source.mute,
                    "the instrument's sound source feeds the game's sound effects group, like the game's own sounds"))
            {
                c.Report();
                yield break;
            }
            emitter.ResetMeasure();
            yield return OutputEnergy(1.6f, e);
            var with = e[0];
            var played = emitter.Rms;

            // What the Sound effects and the Master slider call when moved (Settings, Audio): both end in this one
            // call with Master x Sound effects. 1 %.
            AudioMan.SetSFXVolume(0.01f);
            mixer.GetFloat("SfxVol", out var level);
            c.Check(Mathf.Abs(level + 20f) < 0.5f, $"the sliders' call turns the mixer's sound effects level down ({F(level)} dB at 1 %)");
            yield return Real(0.4f);
            emitter.ResetMeasure();
            yield return OutputEnergy(1.6f, e);
            var withLow = e[0];
            var playedLow = emitter.Rms;
            Performance.TestRequestStop();
            yield return new WaitForSeconds(1.2f);
            yield return OutputEnergy(1.2f, e);
            var worldLow = e[0];

            var heard = Math.Max(0.0, with - world);
            var heardLow = Math.Max(0.0, withLow - worldLow);
            c.Note($"at the game's output: world alone {Db(world)}, with the flute {Db(with)}; sliders at 1 %: with the flute {Db(withLow)}, world alone {Db(worldLow)}; "
                   + $"the flute before the mixer: rms {F(played)} then {F(playedLow)}");
            c.Check(played > 0.002f && playedLow > played * 0.3f, $"the flute itself plays on as loud (before the mixer: rms {F(played)}, then {F(playedLow)})");
            c.Check(with > 1e-9 && heard > 3.0 * world,
                $"the flute is heard at the game's output, well above the world around it (flute {Db(heard)}, world {Db(world)}); "
                + "when this fails the game is muted or the place is too loud to tell, not a fault of the mod");
            // Same notes are not played in both parts: weigh by what the flute gave before the mixer.
            var share = heard > 0.0 && played > 0f && playedLow > 0f ? heardLow / (playedLow * (double)playedLow) / (heard / (played * (double)played)) : 1.0;
            c.Check(share < 0.1, $"with Sound effects (or Master) at 1 % the instrument is much quieter at the game's output ({Db(heardLow)} against {Db(heard)}, "
                                 + $"{F((float)(share * 100.0))} % of the energy; a tenth or less expected)");
            c.Report();
        }
        finally
        {
            AudioMan.SetSFXVolume(own);
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.listen.rest (M04: listener side, a real fire, the game's own Resting and Rested) ----------

    // Needs things outside the mod: dry weather (forced here), a campfire that burns, and no creature that has
    // noticed the player. When one is missing the test says which.
    private static IEnumerator RunListenRest()
    {
        var c = new Checks(ListenRestName);
        var player = Player.m_localPlayer;
        if (player == null || ZNetScene.instance == null || EnvMan.instance == null)
        {
            SelfTest.Fail(ListenRestName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            x.Weather("Clear");
            var rules = TestRules();
            rules.BonusMinutes = 10f;
            rules.BonusRange = 20f;
            rules.HearingRange = 40f;
            ServerRules.TestRules = rules;
            var seman = player.GetSEMan();
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);
            seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
            seman.RemoveStatusEffect(SEMan.s_statusEffectWet, true);
            Listeners.Shutdown();
            Listeners.ForgetEncores();
            var prefab = ZNetScene.instance.GetPrefab("fire_pit");
            if (!c.Check(prefab != null, "campfire prefab fire_pit"))
            {
                c.Report();
                yield break;
            }
            var forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            var spot = player.transform.position + forward * 1.6f;
            spot.y = ZoneSystem.instance.GetGroundHeight(spot);
            var fireObject = Object.Instantiate(prefab, spot, Quaternion.identity);
            rig.Spawned(fireObject);
            yield return new WaitForSeconds(1.2f);
            var fire = fireObject != null ? fireObject.GetComponent<Fireplace>() : null;
            c.Check(fire != null && fire.IsBurning(), "the campfire burns");
            c.Check(!player.InShelter(), "outdoors (no shelter)");
            Seen.Clear();
            c.Check(player.StartEmote("sit", false), "sit down by the fire");
            yield return WaitFor(() => player.IsSitting(), 4f);
            yield return WaitFor(() => seman.HaveStatusEffect(SEMan.s_statusEffectResting), 6f);
            var cozy = seman.GetStatusEffect(SEMan.s_statusEffectResting) as SE_Cozy;
            if (!c.Check(cozy != null,
                    $"Resting by the fire (sitting {player.IsSitting()}, noticed by a creature {player.IsSensed()}, near fire {F(player.m_nearFireTimer)} s ago, "
                    + $"wet {seman.HaveStatusEffect(SEMan.s_statusEffectWet)}, burning {seman.HaveStatusEffect(SEMan.s_statusEffectBurning)})"))
            {
                c.Report();
                yield break;
            }
            yield return WaitFor(() => player.m_comfortLevel > 0, 4f);
            string Comfort(int level) => Localization.instance.Localize("$se_rested_comfort:" + level);
            var baseComfort = player.GetComfortLevel();
            c.Check(baseComfort == 1 && cozy.GetIconText() == Comfort(1), $"outdoors by the fire: Resting shows comfort 1 ('{cozy.GetIconText()}')");
            // The game waits about 20 s before Rested: me skip the wait (its own timer), nothing else.
            cozy.m_time = Mathf.Max(cozy.m_time, cozy.m_delay - 0.3f);
            yield return WaitFor(() => seman.HaveStatusEffect(SEMan.s_statusEffectRested), 3f);
            var rested = seman.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Rested;
            if (!c.Check(rested != null, "Rested starts while resting"))
            {
                c.Report();
                yield break;
            }
            c.Check(Mathf.Abs(rested.m_ttl - 480f) < 0.5f, $"Rested starts at 8:00 without music ({F(rested.m_ttl)} s)");

            var here = player.transform.position;
            var right = player.transform.right;
            // A third performer, 23 m away (bonus range 20): their Encore gives nothing.
            var farOne = new ZDOID(730003L, 1u);
            Listeners.Receive(BatchOf(farOne, 3, here - right * 23f, InstrumentKind.Lyre, 0f, BatchFlags.Encore | BatchFlags.Live));
            yield return Frames(3);
            c.Check(!seman.HaveStatusEffect(InstrumentContent.EffectHash) && player.GetComfortLevel() == baseComfort,
                "another player's Encore from 23 m away: no Music, comfort unchanged");
            // A performer 12 m away: their Encore reaches us.
            var mark = Seen.Messages.Count;
            var nearOne = new ZDOID(730002L, 1u);
            Listeners.Receive(BatchOf(nearOne, 2, here + right * 12f, InstrumentKind.Flute, 0f, BatchFlags.Encore | BatchFlags.Live));
            yield return Frames(3);
            c.Check(seman.HaveStatusEffect(InstrumentContent.EffectHash), "another player's Encore from 12 m away gives us Music");
            c.Check(Seen.Count(MessageHud.MessageType.TopLeft, "The music warms you: +3 comfort.", mark) == 1, "message: " + Seen.Tail());
            c.Check(player.GetComfortLevel() == baseComfort + 3 && cozy.GetIconText() == Comfort(baseComfort + 3),
                $"resting by the fire, outdoors: the game's Resting shows comfort 3 higher at once ('{cozy.GetIconText()}')");
            yield return WaitFor(() => rested.m_ttl > 659f, 2f);
            c.Check(Mathf.Abs(rested.m_ttl - rested.m_time - 660f) < 1.5f, $"and Rested lasts 3 minutes longer: 11:00 ({F(rested.m_ttl - rested.m_time)} s)");
            yield return new WaitForSeconds(1.5f);
            c.Check(Mathf.Abs(rested.m_ttl - rested.m_time - 660f) < 1.5f && rested.GetIconText() == "11:00", $"it stays there while resting ('{rested.GetIconText()}')");
            c.Check(Seen.Count(MessageHud.MessageType.Center, "$se_rested_start", mark) == 0, "no new \"You feel rested\" message");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }
}
#endif
