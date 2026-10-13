#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Debug build only. The invaded areas on the map as a player meets them (T22), three small tests, each in an invaded
// area of its own (registered from SelfTestsReal.cs):
//   dn.map.fog     the purple is in the map's colours, the game's fog of war decides what shows: explored place = no
//                  fog and purple, a place not explored yet = purple under the fog; large map and minimap draw the same
//                  two textures; ShowAreas off and on never lifts fog; after going there the place is explored and purple
//   dn.map.open    real key: the key bound to Map opens the large map over the player's explored, purple place, the same
//                  key closes it
//   dn.map.screen  what is drawn: screen pixels of the open large map and of the minimap, read with the areas shown and
//                  with ShowAreas off: explored invaded places turn purple, places not explored do not change
// Still for a person: whether it looks right.
internal static partial class SelfTests
{
    private const string MapFogName = "dn.map.fog";
    private const string MapOpenName = "dn.map.open";
    private const string MapScreenName = "dn.map.screen";

    private static void RegisterMap()
    {
        SelfTest.Register(MapFogName, RunMapFog);
        SelfTest.Register(MapOpenName, RunMapOpen);
        SelfTest.Register(MapScreenName, RunMapScreen);
    }

    private static void UnregisterMap()
    {
        SelfTest.Unregister(MapFogName);
        SelfTest.Unregister(MapOpenName);
        SelfTest.Unregister(MapScreenName);
    }

    // ---------- tools ----------

    // Keyboard key or mouse button a game button is bound to right now (the player's own binding), found with the
    // game's own KeyCode -> control path table. False = bound to nothing the test can press.
    private static bool BoundKey(string button, out KeyCode key, out string path)
    {
        key = KeyCode.None;
        path = "";
        var def = ZInput.instance != null ? ZInput.instance.GetButtonDef(button) : null;
        if (def == null)
        {
            return false;
        }
        path = def.GetActionPath() ?? "";
        if (path.Length == 0)
        {
            return false;
        }
        foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
        {
            if (k == KeyCode.None || k >= KeyCode.JoystickButton0 || k == KeyCode.Mouse5 || k == KeyCode.Mouse6)
            {
                continue;
            }
            if (string.Equals(ZInput.KeyCodeToPath(k, false) ?? "", path, StringComparison.OrdinalIgnoreCase))
            {
                key = k;
                return true;
            }
        }
        return false;
    }

    // The game's fog of war over a place: 1 = fog (not explored), 0 = lifted, -1 = off the map.
    private static float FogRed(Minimap map, Vector3 at)
    {
        map.WorldToPixel(at, out var px, out var py);
        if (map.m_fogTexture == null || px < 0 || py < 0 || px >= map.m_textureSize || py >= map.m_textureSize)
        {
            return -1f;
        }
        return map.m_fogTexture.GetPixel(px, py).r;
    }

    private static Vector3 Around(Vector3 center, float degrees, float distance) =>
        center + new Vector3(Mathf.Sin(degrees * Mathf.Deg2Rad), 0f, Mathf.Cos(degrees * Mathf.Deg2Rad)) * distance;

    // Tinted on the map, explored or not as asked, and the same 'margin' m around it (not at the rim of the area, nor at
    // the rim of what is explored).
    private static bool MapSpot(Minimap map, Vector3 p, bool explored, float margin)
    {
        if (MapOverlay.TestPixel(p) != 2 || map.IsExplored(p) != explored)
        {
            return false;
        }
        for (var a = 0; a < 360; a += 90)
        {
            var q = Around(p, a, margin);
            if (MapOverlay.TestPixel(q) != 2 || map.IsExplored(q) != explored)
            {
                return false;
            }
        }
        return true;
    }

    // The nearest invaded dry place the map does not show yet, 150 to 900 m from a point.
    private static bool FindUnexplored(Minimap map, Vector3 from, out Vector3 spot)
    {
        spot = Vector3.zero;
        var wg = WorldGenerator.instance;
        if (wg == null)
        {
            return false;
        }
        for (var d = 150f; d <= 900f; d += 30f)
        {
            for (var a = 0; a < 360; a += 15)
            {
                var p = Around(from, a, d);
                if (!WorldGenerator.IsDeepnorth(p.x, p.z) || !DryAt(wg, p) || wg.GetBiome(p.x, p.z) != Heightmap.Biome.DeepNorth
                    || !MapSpot(map, p, false, 40f))
                {
                    continue;
                }
                p.y = wg.GetHeight(p.x, p.z);
                spot = p;
                return true;
            }
        }
        return false;
    }

    private static AwakeningRules MapRules(bool show) => Rules(r =>
    {
        r.StormShare = 0;
        r.NatureBandChance = 0;
        r.MapAreas = show;
    });

    private static bool SameTextures(Minimap map, RawImage image)
    {
        var material = image != null ? image.material : null;
        return material != null && map.m_mapTexture != null && map.m_fogTexture != null
               && material.GetTexture("_MainTex") == map.m_mapTexture && material.GetTexture("_FogTex") == map.m_fogTexture;
    }

    // ---------- dn.map.fog ----------

    private static IEnumerator RunMapFog()
    {
        var c = new Checks(MapFogName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var point = Vector3.zero;
        var spot = Vector3.zero;
        try
        {
            ServerRules.TestRules = MapRules(true);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            var map = Minimap.instance;
            var haveArea = LandSlot(SlotFog, out point, out var cell);
            if (!c.Check(map != null && haveArea, "the game has a map and a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                yield return Until(() => MapOverlay.Ready && MapOverlay.TestPixel(pos) == 2 && map.IsExplored(pos), 12f);
                c.Check(map.IsExplored(pos) && FogRed(map, pos) < 0.5f && MapOverlay.TestPixel(pos) == 2,
                    $"where the player stands the map is explored (fog {F(FogRed(map, pos))}) and the invaded area is purple in the map's colours "
                    + $"(pixel state {MapOverlay.TestPixel(pos)})");
                c.Check(SameTextures(map, map.m_mapImageLarge) && SameTextures(map, map.m_mapImageSmall),
                    "the large map and the minimap draw the same two textures: the colours the mod tints, under the game's own fog of war");

                var haveSpot = FindUnexplored(map, pos, out spot);
                if (c.Check(haveSpot, "an invaded place within 900 m that the map has not explored yet"))
                {
                    var fog = FogRed(map, spot);
                    c.Check(!map.IsExplored(spot) && fog > 0.5f && MapOverlay.TestPixel(spot) == 2,
                        $"at {F(spot)}, {F(Flat(spot - pos).magnitude)} m away and not explored: the colours are purple there, but the game's fog of war "
                        + $"covers the place (fog {F(fog)}): no purple shows where the map is not explored");

                    // The mod never lifts fog: the areas hidden and shown again change the colours only.
                    ServerRules.TestRules = MapRules(false);
                    yield return Frames(3);
                    var hidden = MapOverlay.TestPixel(pos);
                    ServerRules.TestRules = MapRules(true);
                    yield return Frames(3);
                    c.Check(hidden == 1 && MapOverlay.TestPixel(pos) == 2 && !map.IsExplored(spot) && Mathf.Approximately(FogRed(map, spot), fog),
                        $"ShowAreas off and on again: the colours change (pixel state {hidden}, then {MapOverlay.TestPixel(pos)}), the fog of war "
                        + "over the place that is not explored stays");

                    // Go there: the game explores around the player, the purple is under no fog any more.
                    if (c.Check(clock.Room(7f, 2), "time left to go to that place"))
                    {
                        yield return TeleportAndWait(player, spot, TravelTimeout, box);
                        if (c.Check(box.Ok, "travelled to the place that was not explored"))
                        {
                            yield return Until(() => map.IsExplored(spot), 6f);
                            c.Check(map.IsExplored(spot) && FogRed(map, spot) < 0.5f && MapOverlay.TestPixel(spot) == 2,
                                $"as the map gets explored the invaded area shows: explored now (fog {F(FogRed(map, spot))}), still purple "
                                + $"(pixel state {MapOverlay.TestPixel(spot)})");
                        }
                    }
                }
            }
        }
        finally
        {
            ClearOverrides();
            SweepArea(player);
            PurgeTagged(point, 600f);
            PurgeTagged(spot, 600f);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.map.open ----------

    private static IEnumerator RunMapOpen()
    {
        var c = new Checks(MapOpenName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var away = false;
        var point = Vector3.zero;
        var map = Minimap.instance;
        try
        {
            ServerRules.TestRules = MapRules(true);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            var haveArea = LandSlot(SlotMapOpen, out point, out var cell);
            var bound = BoundKey("Map", out var key, out var path);
            if (!c.Check(map != null && haveArea, "the game has a map and a dry invaded Deep North area exists")
                || !c.Check(SelfTest.ReleaseInput() && bound,
                    $"an input driver runs the game and the Map button is bound to a key the test can press (bound to '{path}')"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                yield return Until(() => MapOverlay.Ready && MapOverlay.TestPixel(pos) == 2 && map.IsExplored(pos), 12f);
                yield return Frames(5);
                c.Check(map.m_mode == Minimap.MapMode.Small && !InventoryGui.IsVisible() && !global::Console.IsVisible() && !Menu.IsVisible(),
                    $"before the press: the minimap shows, no window is open (map mode {map.m_mode})");
                yield return SelfTest.KeyTap(key);
                yield return Until(() => map.m_mode == Minimap.MapMode.Large, 2f);
                yield return Frames(4);
                if (c.Check(map.m_mode == Minimap.MapMode.Large && map.m_largeRoot != null && map.m_largeRoot.activeInHierarchy,
                        $"the {key} key (the Map button) opens the large map"))
                {
                    var image = map.m_mapImageLarge;
                    map.WorldToMapPoint(pos, out var mx, out var my);
                    c.Check(image != null && image.isActiveAndEnabled && SameTextures(map, image),
                        "the open map draws the colours the mod tints, under the game's fog of war");
                    c.Check(image != null && image.uvRect.Contains(new Vector2(mx, my)) && MapOverlay.TestPixel(pos) == 2 && map.IsExplored(pos),
                        $"the player's place is in view on it, explored and purple (pixel state {MapOverlay.TestPixel(pos)})");
                    SelfTest.Screenshot(MapOpenName, "large_map");
                    yield return null;
                    yield return null;
                    yield return SelfTest.KeyTap(key);
                    yield return Until(() => map.m_mode == Minimap.MapMode.Small, 2f);
                    c.Check(map.m_mode == Minimap.MapMode.Small, $"the same key closes it again (map mode {map.m_mode})");
                }
            }
        }
        finally
        {
            SelfTest.ReleaseInput();
            if (map != null && map.m_mode == Minimap.MapMode.Large)
            {
                map.SetMapMode(Minimap.MapMode.Small);
            }
            ClearOverrides();
            SweepArea(player);
            PurgeTagged(point, 600f);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.map.screen ----------

    // Where a world place is drawn on screen by a map image (pixels from the bottom left). False = outside the image
    // (or in its outer tenth: frame, mask).
    private static bool MapToScreen(Minimap map, RawImage image, Vector3 world, out Vector2 screen)
    {
        screen = Vector2.zero;
        if (image == null || !image.isActiveAndEnabled)
        {
            return false;
        }
        map.WorldToMapPoint(world, out var mx, out var my);
        var uv = image.uvRect;
        if (uv.width <= 0f || uv.height <= 0f)
        {
            return false;
        }
        var u = (mx - uv.xMin) / uv.width;
        var v = (my - uv.yMin) / uv.height;
        if (u < 0.1f || u > 0.9f || v < 0.1f || v > 0.9f)
        {
            return false;
        }
        var rt = image.rectTransform;
        var rect = rt.rect;
        var worldPoint = rt.TransformPoint(new Vector3(rect.xMin + u * rect.width, rect.yMin + v * rect.height, 0f));
        var canvas = image.canvas;
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        screen = RectTransformUtility.WorldToScreenPoint(camera, worldPoint);
        return screen.x >= 2f && screen.y >= 2f && screen.x < Screen.width - 2 && screen.y < Screen.height - 2;
    }

    // How purple the screen is at a point (3 x 3 pixels): red and blue over green. Snow, sea and fog give about 0.
    private static float Purple(Texture2D shot, Vector2 at)
    {
        var x = Mathf.Clamp(Mathf.RoundToInt(at.x), 1, shot.width - 2);
        var y = Mathf.Clamp(Mathf.RoundToInt(at.y), 1, shot.height - 2);
        var sum = 0f;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var p = shot.GetPixel(x + dx, y + dy);
                sum += (p.r + p.b) * 0.5f - p.g;
            }
        }
        return sum / 9f;
    }

    private sealed class ScreenRead
    {
        internal bool Ok;
        internal float[] Values = new float[0];
    }

    // The screen as drawn at the end of this frame, read at these points.
    private static IEnumerator ReadScreen(List<Vector2> points, ScreenRead read)
    {
        read.Ok = false;
        yield return new WaitForEndOfFrame();
        var shot = ScreenCapture.CaptureScreenshotAsTexture();
        if (shot == null)
        {
            yield break;
        }
        try
        {
            read.Values = new float[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                read.Values[i] = Purple(shot, points[i]);
            }
            read.Ok = shot.width > 8 && shot.height > 8;
        }
        finally
        {
            Object.Destroy(shot);
        }
    }

    private const float PurpleStep = 0.05f;
    private const float PurpleStill = 0.03f;

    // Points whose purple went up by at least 'step' between two reads (grew = true), or moved by less than 'step'.
    private static int Count(ScreenRead first, ScreenRead second, int from, int count, float step, bool grew)
    {
        var n = 0;
        for (var i = from; i < from + count && i < first.Values.Length && i < second.Values.Length; i++)
        {
            var d = first.Values[i] - second.Values[i];
            if (grew ? d >= step : Mathf.Abs(d) < step)
            {
                n++;
            }
        }
        return n;
    }

    private static string Deltas(ScreenRead first, ScreenRead second, int from, int count)
    {
        var parts = new List<string>();
        for (var i = from; i < from + count && i < first.Values.Length && i < second.Values.Length; i++)
        {
            parts.Add(F(first.Values[i] - second.Values[i]));
        }
        return string.Join(" ", parts.ToArray());
    }

    private static IEnumerator RunMapScreen()
    {
        var c = new Checks(MapScreenName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var away = false;
        var point = Vector3.zero;
        var map = Minimap.instance;
        var zoom = map != null ? map.m_largeZoom : 0f;
        var zoomed = false;
        try
        {
            ServerRules.TestRules = MapRules(true);
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            var haveArea = LandSlot(SlotMapScreen, out point, out var cell);
            if (!c.Check(map != null && haveArea, "the game has a map and a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                yield return Until(() => MapOverlay.Ready && MapOverlay.TestPixel(pos) == 2 && map.IsExplored(pos), 12f);
                // The game explores around the player every 2 s: once more, so the ring below is in.
                yield return new WaitForSeconds(2.5f);
                c.Check(map.m_mode == Minimap.MapMode.Small && !InventoryGui.IsVisible() && !global::Console.IsVisible() && !Menu.IsVisible(),
                    $"the minimap shows, no window is open (map mode {map.m_mode})");

                // Large map, zoomed so that about 800 m of the world fill its height (the player's marker covers the
                // middle only).
                zoomed = true;
                map.m_largeZoom = Mathf.Clamp(800f / (map.m_textureSize * map.m_pixelSize), map.m_minZoom, map.m_maxZoom);
                map.SetMapMode(Minimap.MapMode.Large);
                yield return Frames(6);
                // Places to read: explored and purple (a ring around the player), purple but not explored (further out).
                var places = new List<Vector2>();
                var shown = 0;
                for (var a = 0; a < 360 && shown < 12; a += 30)
                {
                    var p = Around(pos, a, 70f);
                    if (MapSpot(map, p, true, 15f) && MapToScreen(map, map.m_mapImageLarge, p, out var sp))
                    {
                        places.Add(sp);
                        shown++;
                    }
                }
                var covered = 0;
                for (var d = 170f; d <= 330f && covered < 12; d += 40f)
                {
                    for (var a = 0; a < 360 && covered < 12; a += 30)
                    {
                        var p = Around(pos, a, d);
                        if (MapSpot(map, p, false, 30f) && MapToScreen(map, map.m_mapImageLarge, p, out var sp))
                        {
                            places.Add(sp);
                            covered++;
                        }
                    }
                }
                if (c.Check(map.m_mode == Minimap.MapMode.Large && shown >= 4 && covered >= 3,
                        $"the large map is open with {shown} explored purple places and {covered} purple places that are not explored in view"))
                {
                    var areasOn = new ScreenRead();
                    yield return ReadScreen(places, areasOn);
                    ServerRules.TestRules = MapRules(false);
                    yield return Frames(4);
                    var areasOff = new ScreenRead();
                    yield return ReadScreen(places, areasOff);
                    ServerRules.TestRules = MapRules(true);
                    yield return Frames(4);
                    if (c.Check(areasOn.Ok && areasOff.Ok, "the screen could be read with the areas shown and with ShowAreas off"))
                    {
                        var purple = Count(areasOn, areasOff, 0, shown, PurpleStep, true);
                        var still = Count(areasOn, areasOff, shown, covered, PurpleStill, false);
                        c.Check(purple * 4 >= shown * 3,
                            $"large map on screen: {purple} of {shown} explored invaded places are purple with the areas shown and not with "
                            + $"ShowAreas off (purple gained: {Deltas(areasOn, areasOff, 0, shown)})");
                        c.Check(still * 4 >= covered * 3,
                            $"large map on screen: {still} of {covered} invaded places that are not explored look the same either way: no purple "
                            + $"where the map is not explored (purple gained: {Deltas(areasOn, areasOff, shown, covered)})");
                    }
                    SelfTest.Screenshot(MapScreenName, "large_map");
                    yield return null;
                    yield return null;
                }

                // Minimap: the same explored places, nearer the player.
                map.SetMapMode(Minimap.MapMode.Small);
                yield return Frames(6);
                var small = new List<Vector2>();
                for (var a = 0; a < 360; a += 30)
                {
                    var p = Around(pos, a, 55f);
                    if (MapSpot(map, p, true, 15f) && MapToScreen(map, map.m_mapImageSmall, p, out var sp))
                    {
                        small.Add(sp);
                    }
                }
                if (c.Check(map.m_mode == Minimap.MapMode.Small && small.Count >= 4, $"the minimap shows {small.Count} explored purple places around the player"))
                {
                    var areasOn = new ScreenRead();
                    yield return ReadScreen(small, areasOn);
                    ServerRules.TestRules = MapRules(false);
                    yield return Frames(4);
                    var areasOff = new ScreenRead();
                    yield return ReadScreen(small, areasOff);
                    ServerRules.TestRules = MapRules(true);
                    yield return Frames(4);
                    if (c.Check(areasOn.Ok && areasOff.Ok, "the screen could be read again for the minimap"))
                    {
                        var purple = Count(areasOn, areasOff, 0, small.Count, PurpleStep, true);
                        c.Check(purple * 4 >= small.Count * 3,
                            $"minimap on screen: {purple} of {small.Count} explored invaded places are purple with the areas shown and not with "
                            + $"ShowAreas off (purple gained: {Deltas(areasOn, areasOff, 0, small.Count)})");
                    }
                }
            }
        }
        finally
        {
            if (map != null)
            {
                if (zoomed)
                {
                    map.m_largeZoom = zoom;
                }
                if (map.m_mode == Minimap.MapMode.Large)
                {
                    map.SetMapMode(Minimap.MapMode.Small);
                }
            }
            ClearOverrides();
            SweepArea(player);
            PurgeTagged(point, 600f);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }
}
#endif
