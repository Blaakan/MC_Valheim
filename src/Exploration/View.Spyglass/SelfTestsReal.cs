#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MC.Shared;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.ViewSpyglassMod;

// Debug build only. Me = self tests that do the real thing where the older ones used a stand-in (TESTING.md items in
// brackets):
//   spyglass.picture           the frame as drawn, read back pixel by pixel: untouched inside the circle, black from
//                              the dark edge outward in every direction (a circle, same radius sideways and up and
//                              down), fine detail kept inside and gone outside (blur), darker outside at the default
//                              settings, and the blurred ring is the same picture [T04]
//   spyglass.render-scale      render scale 50% (the game's own switch, in memory): scaled picture and round view fill
//                              the same rectangle, blur made from the scaled picture, then the same pixel checks [T17]
//   spyglass.close-real        real building pieces: a wall right before the face and a roof right over the head are
//                              never cut by the near plane on any frame at the eye; back to a real wall: own body
//                              hidden and shown by the rule on every frame [T20 T21]
//   spyglass.death             a real death: on fire, spyglass raised while burning, low health, killed by the fire;
//                              nothing of the spyglass on any frame of the death camera, dead body drawn, death camera
//                              out of head at once and until respawn, first dead frame look at body from behind eye's
//                              last look, at third-person distance it had before raise (game only turn dead player's
//                              camera while body there, never move it: left in eye, it look out of dead body's head),
//                              clean log; after the game's respawn the view is normal, the spyglass is in the
//                              tombstone, taken back from it and raised by a click [T26]
// Death test last of the in-world ones: it leave a new player object. It keep the bag aside and give it back, with
// skills, food, health, place, death pin and death point (recipe of other MC mods' death tests).
internal static partial class SelfTests
{
    private const string PictureTest = "spyglass.picture";
    private const string RenderScaleTest = "spyglass.render-scale";
    private const string CloseRealTest = "spyglass.close-real";
    private const string DeathTest = "spyglass.death";

    // ---------- the frame as drawn ----------

    // One frame read back from the screen (world, round view, HUD): brightness 0..255 per pixel, row 0 = bottom.
    private sealed class Shot
    {
        internal int W;
        internal int H;
        internal byte[] Luma;

        // Mean brightness 0..1 of the square of that half size around a point.
        internal float Mean(float cx, float cy, int half)
        {
            var x0 = Mathf.Clamp(Mathf.RoundToInt(cx) - half, 0, W - 1);
            var x1 = Mathf.Clamp(Mathf.RoundToInt(cx) + half, 0, W - 1);
            var y0 = Mathf.Clamp(Mathf.RoundToInt(cy) - half, 0, H - 1);
            var y1 = Mathf.Clamp(Mathf.RoundToInt(cy) + half, 0, H - 1);
            long sum = 0;
            for (var y = y0; y <= y1; y++)
            {
                var row = y * W;
                for (var x = x0; x <= x1; x++)
                {
                    sum += Luma[row + x];
                }
            }
            return sum / (255f * Mathf.Max(1, (x1 - x0 + 1) * (y1 - y0 + 1)));
        }

        // Fine detail of a box: mean difference 0..1 between pixels next to each other (sideways and upward).
        internal float Detail(int x0, int y0, int x1, int y1)
        {
            x0 = Mathf.Clamp(x0, 0, W - 2);
            x1 = Mathf.Clamp(x1, x0 + 1, W - 1);
            y0 = Mathf.Clamp(y0, 0, H - 2);
            y1 = Mathf.Clamp(y1, y0 + 1, H - 1);
            long sum = 0;
            long n = 0;
            for (var y = y0; y < y1; y++)
            {
                var row = y * W;
                for (var x = x0; x < x1; x++)
                {
                    int v = Luma[row + x];
                    sum += Mathf.Abs(v - Luma[row + x + 1]) + Mathf.Abs(v - Luma[row + W + x]);
                    n += 2;
                }
            }
            return n > 0 ? sum / (255f * n) : 0f;
        }
    }

    // box[0] = the frame being drawn now (null = could not be read).
    private static IEnumerator Grab(Shot[] box)
    {
        box[0] = null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = null;
        try
        {
            tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex != null && tex.width > 16 && tex.height > 16)
            {
                var px = tex.GetPixels32();
                var luma = new byte[px.Length];
                for (var i = 0; i < px.Length; i++)
                {
                    luma[i] = (byte)((px[i].r * 77 + px[i].g * 150 + px[i].b * 29) >> 8);
                }
                box[0] = new Shot { W = tex.width, H = tex.height, Luma = luma };
            }
        }
        finally
        {
            if (tex != null)
            {
                Object.Destroy(tex);
            }
        }
    }

    // Round view settings forced in memory, then the frame once they took hold.
    private static IEnumerator GrabWith(float clear, bool blur, float dark, Shot[] box)
    {
        ScopeOverlay.TestClearViewSize = clear;
        ScopeOverlay.TestEdgeBlur = blur;
        ScopeOverlay.TestEdgeDarkness = dark;
        yield return Frames(5);
        yield return Grab(box);
    }

    private static float Ratio(Shot shot, Shot plain, float x, float y, int half)
    {
        var a = plain.Mean(x, y, half);
        return a > 0.03f ? shot.Mean(x, y, half) / a : -1f;
    }

    // Spyglass must be up at x4. Me read frames with the round view settings forced one by one and compare them with
    // the plain zoomed picture (no blur, no dark edge) of the same look: at the ground for sharp / blurred / dark /
    // round, at the horizon for "the blurred ring is the same picture, in the same place". label = which run.
    private static IEnumerator PictureChecks(Checks c, SpyRig rig, string label, float yaw)
    {
        var box = new Shot[1];
        const float Size = ScopeOverlay.DefaultClearSize;
        rig.Look(yaw, 18f);
        yield return new WaitForSeconds(0.6f);
        yield return GrabWith(Size, false, 0f, box);
        var plain = box[0];
        yield return GrabWith(0.4f, false, 1f, box);
        var smallDark = box[0];
        yield return GrabWith(Size, false, 1f, box);
        var bigDark = box[0];
        yield return GrabWith(Size, true, 0f, box);
        var blurred = box[0];
        yield return GrabWith(Size, true, ScopeOverlay.DefaultEdgeDarkness, box);
        var normal = box[0];
        rig.Look(yaw, 0f);
        yield return new WaitForSeconds(0.6f);
        yield return GrabWith(Size, false, 0f, box);
        var farPlain = box[0];
        yield return GrabWith(Size, true, 0f, box);
        var farBlurred = box[0];
        ScopeOverlay.TestEdgeDarkness = ScopeOverlay.DefaultEdgeDarkness;
        yield return Frames(2);

        if (!c.Check(plain != null && smallDark != null && bigDark != null && blurred != null && normal != null && farPlain != null && farBlurred != null,
                label + "the drawn frame could be read back from the screen"))
        {
            yield break;
        }
        int w = plain.W;
        int h = plain.H;
        var cx = w * 0.5f;
        var cy = h * 0.5f;
        var patch = Mathf.Max(3, h / 150);
        var mid = plain.Mean(cx, cy, h / 10);
        if (!c.Check(Mathf.Abs(w - Screen.width) <= 2 && Mathf.Abs(h - Screen.height) <= 2 && mid > 0.05f,
                $"{label}the read frame is the whole screen ({w} x {h}) and the plain picture is bright enough to judge (brightness {F(mid)} at the centre)"))
        {
            yield break;
        }

        // Round and dark: ClearViewSize 0.4 with EdgeDarkness 1 has the whole ring on screen in every direction.
        var r = h * 0.5f * 0.4f;
        var kept = 0;
        var black = 0;
        var worst = "";
        for (var i = 0; i < 8; i++)
        {
            var ang = i * 45f * Mathf.Deg2Rad;
            var dx = Mathf.Cos(ang);
            var dy = Mathf.Sin(ang);
            var inner = Ratio(smallDark, plain, cx + dx * r * 0.6f, cy + dy * r * 0.6f, patch);
            var edge = Ratio(smallDark, plain, cx + dx * r * 0.95f, cy + dy * r * 0.95f, patch);
            var outer = smallDark.Mean(cx + dx * r * 1.78f, cy + dy * r * 1.78f, patch);
            var isKept = inner > 0.7f && inner < 1.35f && edge > 0.7f && edge < 1.35f;
            var isBlack = outer <= 0.04f;
            kept += isKept ? 1 : 0;
            black += isBlack ? 1 : 0;
            if (worst.Length == 0 && (!isKept || !isBlack))
            {
                worst = $"at {i * 45} degrees: inside x{F(inner)}, at the rim x{F(edge)}, outside brightness {F(outer)}";
            }
        }
        c.Check(kept >= 7, $"{label}ClearViewSize 0.4, EdgeDarkness 1: inside the circle the picture is as without the dark edge, up to its rim, in {kept} of 8 directions"
                           + (worst.Length == 0 ? "" : " (" + worst + ")"));
        c.Check(black >= 7, $"{label}and black from 1.78 circle radii outward in {black} of 8 directions: the dark edge is a circle around the middle of the screen, "
                            + $"as wide sideways as up and down ({F(r)} px radius on {w} x {h})" + (worst.Length == 0 ? "" : " (" + worst + ")"));

        // Default size (70% of the screen height): inside untouched all around, black toward the left and right edge.
        r = h * 0.5f * Size;
        kept = 0;
        for (var i = 0; i < 8; i++)
        {
            var ang = i * 45f * Mathf.Deg2Rad;
            var inner = Ratio(bigDark, plain, cx + Mathf.Cos(ang) * r * 0.9f, cy + Mathf.Sin(ang) * r * 0.9f, patch);
            kept += inner > 0.7f && inner < 1.35f ? 1 : 0;
        }
        // As far out as the screen goes (wide screens: fully dark there; narrow ones: as dark as the ring is there).
        var reach = Mathf.Min(r * 1.78f, w * 0.5f - patch - 2f);
        var k = Mathf.Clamp01((reach - r * 1.02f) / (r * 0.68f));
        var alpha = k * k * (3f - 2f * k);
        // Dark edge mixed in display values gives 1 - alpha, mixed in linear light gives its 1 / 2.2 power.
        var most = Mathf.Pow(1f - alpha, 1f / 2.2f) + 0.12f;
        var left = Ratio(bigDark, plain, cx - reach, cy, patch);
        var right = Ratio(bigDark, plain, cx + reach, cy, patch);
        c.Check(kept >= 7 && alpha > 0.3f && left >= 0f && right >= 0f && left <= most && right <= most,
            $"{label}default size: untouched inside the circle of {F(Size * 100f)}% of the screen height ({kept} of 8 directions), dark toward the left and right edge "
            + $"(x{F(left)} and x{F(right)} of the plain picture {F(reach)} px out, at most x{F(most)} expected)");
        // Half way through the dark edge the picture is partly darkened: a circle of another size would be fully
        // clear or fully dark there.
        const float HalfWay = 1.45f;
        var hk = (HalfWay - 1.02f) / 0.68f;
        var halfAlpha = hk * hk * (3f - 2f * hk);
        var low = 1f - halfAlpha - 0.15f;
        var high = Mathf.Pow(1f - halfAlpha, 1f / 2.2f) + 0.12f;
        var halfLeft = Ratio(bigDark, plain, cx - r * HalfWay, cy, patch);
        var halfRight = Ratio(bigDark, plain, cx + r * HalfWay, cy, patch);
        c.Check(r * HalfWay < w * 0.5f - patch - 2f && halfLeft > low && halfLeft < high && halfRight > low && halfRight < high,
            $"{label}and partly dark {F(HalfWay)} circle radii out, where the dark edge is half way (x{F(halfLeft)} and x{F(halfRight)}, between x{F(low)} and x{F(high)} expected): "
            + "the circle has the size the setting says");

        // Sharp inside, blurred outside: fine detail of the same boxes with EdgeBlur on and off.
        var box0 = Mathf.RoundToInt(h * 0.18f);
        var near = r * 1.3f * 1.06f;
        var far = Mathf.Min(w * 0.5f - 4f, h * 0.8f);
        if (c.Check(far - near >= h * 0.05f, $"{label}the screen is wide enough to show the fully blurred ring ({w} x {h})"))
        {
            var inPlain = plain.Detail((int)cx - box0, (int)cy - box0, (int)cx + box0, (int)cy + box0);
            var inBlur = blurred.Detail((int)cx - box0, (int)cy - box0, (int)cx + box0, (int)cy + box0);
            var outPlain = (plain.Detail((int)(cx - far), (int)cy - box0, (int)(cx - near), (int)cy + box0)
                            + plain.Detail((int)(cx + near), (int)cy - box0, (int)(cx + far), (int)cy + box0)) * 0.5f;
            var outBlur = (blurred.Detail((int)(cx - far), (int)cy - box0, (int)(cx - near), (int)cy + box0)
                           + blurred.Detail((int)(cx + near), (int)cy - box0, (int)(cx + far), (int)cy + box0)) * 0.5f;
            if (c.Check(inPlain >= 0.004f && outPlain >= 0.004f,
                    $"{label}the ground in view has fine detail to tell sharp from blurred (inside {F(inPlain * 100f)}%, outside {F(outPlain * 100f)}% mean step between pixels)"))
            {
                c.Check(inBlur > inPlain * 0.7f && inBlur < inPlain * 1.4f,
                    $"{label}sharp inside the circle: as much fine detail with EdgeBlur on as off (x{F(inBlur / inPlain)})");
                c.Check(outBlur < outPlain * 0.6f, $"{label}blurred outside it: the fine detail falls to x{F(outBlur / outPlain)} of the plain picture");
            }
            // Default settings: outside darker, not black; middle untouched.
            var darker = (normal.Mean(cx - (near + far) * 0.5f, cy, box0 / 2) + normal.Mean(cx + (near + far) * 0.5f, cy, box0 / 2))
                         / Mathf.Max(0.001f, plain.Mean(cx - (near + far) * 0.5f, cy, box0 / 2) + plain.Mean(cx + (near + far) * 0.5f, cy, box0 / 2));
            var centre = Ratio(normal, plain, cx, cy, box0 / 2);
            c.Check(darker > 0.01f && darker < 0.75f && centre > 0.7f && centre < 1.35f,
                $"{label}default settings: outside the circle the picture is darker, not black (x{F(darker)}), the middle is not darkened (x{F(centre)})");

            // The blurred ring is the same picture in the same place: coarse cells left and right of the circle,
            // horizon in view (sky above land), blur on against blur off.
            var cell = Mathf.Max(8, h / 27);
            var a = new List<float>();
            var b = new List<float>();
            for (var y = h * 0.28f; y <= h * 0.72f; y += cell)
            {
                for (var x = near + cell * 0.5f; x <= far - cell * 0.5f; x += cell)
                {
                    a.Add(farPlain.Mean(cx - x, y, cell / 2));
                    b.Add(farBlurred.Mean(cx - x, y, cell / 2));
                    a.Add(farPlain.Mean(cx + x, y, cell / 2));
                    b.Add(farBlurred.Mean(cx + x, y, cell / 2));
                }
            }
            float ma = 0f, mb = 0f;
            foreach (var v in a)
            {
                ma += v;
            }
            foreach (var v in b)
            {
                mb += v;
            }
            ma /= Mathf.Max(1, a.Count);
            mb /= Mathf.Max(1, b.Count);
            float saa = 0f, sbb = 0f, sab = 0f, off = 0f;
            for (var i = 0; i < a.Count; i++)
            {
                saa += (a[i] - ma) * (a[i] - ma);
                sbb += (b[i] - mb) * (b[i] - mb);
                sab += (a[i] - ma) * (b[i] - mb);
                off += Mathf.Abs(a[i] - b[i]);
            }
            var spread = a.Count > 0 ? Mathf.Sqrt(saa / a.Count) : 0f;
            var match = saa > 0f && sbb > 0f ? sab / Mathf.Sqrt(saa * sbb) : 0f;
            off /= Mathf.Max(1, a.Count);
            if (c.Check(a.Count >= 12 && spread >= 0.03f,
                    $"{label}looking at the horizon the picture has big shapes to compare ({a.Count} cells beside the circle, brightness spread {F(spread)})"))
            {
                c.Check(match > 0.9f && off < 0.12f,
                    $"{label}the blurred ring is the same picture in the same place as the plain one (cells match {F(match)}, mean difference {F(off)})");
            }
        }
    }

    // ---------- spyglass.picture ----------

    private static IEnumerator RunPicture()
    {
        var c = new Checks(PictureTest);
        if (!Ready(PictureTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, PictureTest);
        try
        {
            rig.Noon();
            rig.ClearWeather();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new SpyglassRules();
            ScopeOverlay.TestClearViewSize = ScopeOverlay.DefaultClearSize;
            ScopeOverlay.TestEdgeBlur = true;
            ScopeOverlay.TestEdgeDarkness = ScopeOverlay.DefaultEdgeDarkness;
            Scope.TestSetMagnification(4f);
            var yaw = OpenYaw(player, out var free);
            rig.Look(yaw, 0f);
            // Light of noon and clear weather settled: frames a moment apart must be the same picture.
            var fog = new float[1];
            yield return FogSettled(fog, 15f);
            c.Note($"looking {F(yaw)} degrees, {F(free)} m of open view; render scale {(RenderScale.Auto ? "automatic" : RenderScale.Target >= Screen.height ? "100%" : RenderScale.Target + " lines")}");
            yield return RaiseUp(c, player, "picture");
            yield return Frames(15);
            if (Scope.State == ScopeState.Raised)
            {
                yield return PictureChecks(c, rig, "", yaw);
            }
            SelfTest.Screenshot(PictureTest, "default");
            yield return null;
            yield return null;
            yield return LowerDown();
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.render-scale ----------

    private static IEnumerator RunRenderScale()
    {
        var c = new Checks(RenderScaleTest);
        if (!Ready(RenderScaleTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, RenderScaleTest);
        var auto = RenderScale.Auto;
        var target = RenderScale.Target;
        try
        {
            rig.Noon();
            rig.ClearWeather();
            var spy = Hold(rig, c);
            var scaler = Object.FindAnyObjectByType<FrameBufferScaler>(FindObjectsInactive.Include);
            var buffer = RenderScale.FindBuffer();
            var raw = scaler != null ? scaler.GetComponent<RawImage>() : null;
            if (spy == null || !c.Check(scaler != null && buffer != null && raw != null && _tap != null,
                    "this game has the render scale setting (its scaled-picture objects exist)"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new SpyglassRules();
            ScopeOverlay.TestClearViewSize = ScopeOverlay.DefaultClearSize;
            ScopeOverlay.TestEdgeBlur = true;
            ScopeOverlay.TestEdgeDarkness = ScopeOverlay.DefaultEdgeDarkness;
            Scope.TestSetMagnification(4f);
            var problems = _tap.Problems().Count;
            var errors = _tap.ErrorMark;
            var yaw = OpenYaw(player, out _);
            rig.Look(yaw, 0f);
            var fog = new float[1];
            yield return FogSettled(fog, 15f);

            // What Settings > Graphics > Render scale writes when applied (GraphicsSettingsManager): only these two
            // values in memory, nothing saved. Put back in finally.
            var lines = (uint)Mathf.Max(64, Screen.height / 2);
            c.Note($"render scale before the test: {(auto ? "automatic" : target >= Screen.height ? "100%" : target + " lines")} of {Screen.height}; set to {lines} lines (50%) for the test");
            RenderScale.Auto = false;
            RenderScale.Target = lines;
            yield return WaitReal(() => RenderScale.Texture(buffer) != null && raw.texture != null, 3f);
            yield return Frames(5);
            var picture = RenderScale.Texture(buffer);
            if (!c.Check(picture != null && ReferenceEquals(raw.texture, picture) && picture.height == (int)lines,
                    $"the game now draws the world at {lines} lines and stretches that picture over the screen ({(picture != null ? picture.width + " x " + picture.height : "no scaled picture")})"))
            {
                c.Report();
                yield break;
            }
            c.Check(ReferenceEquals(buffer.GetComponent<Camera>(), cam.m_camera), "it is the game camera that draws into the scaled picture");

            yield return RaiseUp(c, player, "render scale");
            yield return Frames(15);
            yield return new WaitForEndOfFrame();
            if (Scope.State == ScopeState.Raised)
            {
                // Same rectangle: round view (ours) and the scaled picture (the game's).
                var rect = ScopeOverlay.TestRect;
                var corners = new Vector3[4];
                raw.rectTransform.GetWorldCorners(corners);
                var canvas = raw.canvas;
                var uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var corner in corners)
                {
                    var p = RectTransformUtility.WorldToScreenPoint(uiCam, corner);
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
                c.Check(Mathf.Abs(rect.width - Screen.width) <= 2f && Mathf.Abs(rect.height - Screen.height) <= 2f,
                    $"the round view is still laid out over the whole screen ({F(rect.width)} x {F(rect.height)} of {Screen.width} x {Screen.height})");
                c.Check(Mathf.Abs(min.x) <= 2f && Mathf.Abs(min.y) <= 2f && Mathf.Abs(max.x - Screen.width) <= 2f && Mathf.Abs(max.y - Screen.height) <= 2f
                        && Near(raw.uvRect.x, 0f) && Near(raw.uvRect.y, 0f) && Near(raw.uvRect.width, 1f) && Near(raw.uvRect.height, 1f),
                    $"the scaled picture fills the same rectangle with its whole texture (({F(min.x)}, {F(min.y)}) to ({F(max.x)}, {F(max.y)}))");
                var frameCanvas = RootCanvasOf(scaler.gameObject);
                var hudCanvas = RootCanvasOf(Hud.instance.m_rootObject);
                c.Note("overlay: " + ScopeOverlay.Placement + (frameCanvas == hudCanvas ? "; the scaled picture sits in the HUD's own canvas: the pixel checks below say whether the round view shows over it" : ""));
                if (frameCanvas != null && frameCanvas != hudCanvas)
                {
                    c.Check(ScopeOverlay.SortingOrder > frameCanvas.sortingOrder && hudCanvas != null && ScopeOverlay.SortingOrder < hudCanvas.sortingOrder,
                        $"the round view is drawn over the scaled picture and under the HUD (order {ScopeOverlay.SortingOrder}, picture {frameCanvas.sortingOrder})");
                }
                // The blur: a copy of the scaled picture.
                RenderTexture blur = null;
                foreach (var t in Resources.FindObjectsOfTypeAll<RenderTexture>())
                {
                    if (t != null && t.name == "MC_Spyglass_Blur")
                    {
                        blur = t;
                    }
                }
                c.Check(ScopeOverlay.TestBlurOn && blur != null && blur.width == Mathf.Max(16, picture.width / 8) && blur.height == Mathf.Max(16, picture.height / 8),
                    $"the blur is made from the scaled picture ({(blur != null ? blur.width + " x " + blur.height : "no blur picture")} from {picture.width} x {picture.height})");
                yield return PictureChecks(c, rig, "render scale 50%: ", yaw);
            }
            SelfTest.Screenshot(RenderScaleTest, "half");
            yield return null;
            yield return null;
            yield return LowerDown();
            yield return Frames(3);
            c.Check(NotBack(player).Length == 0 && Near(cam.m_camera.fieldOfView, cam.m_fov, 0.01f), "lowered at render scale 50%: everything given back");
            var logged = ErrorsNamingMe(errors);
            c.Check(ProblemsSince(problems).Count == 0 && logged.Count == 0,
                "no warning or error from the spyglass at render scale 50%" + (logged.Count == 0 ? "" : ": " + First(logged)));
            c.Report();
        }
        finally
        {
            RenderScale.Auto = auto;
            RenderScale.Target = target;
            rig.Restore();
        }
    }

    // ---------- spyglass.close-real ----------

    // First of these building pieces the game knows, stood up at a place. Its wear check is switched off: a piece
    // that hangs in the air would fall apart during the test.
    private static GameObject SpawnPiece(string[] names, Vector3 at, Func<string, Quaternion> rotation, out string used)
    {
        used = "";
        foreach (var name in names)
        {
            var prefab = ZNetScene.instance.GetPrefab(name);
            if (prefab == null || prefab.GetComponent<Piece>() == null)
            {
                continue;
            }
            used = name;
            var go = Object.Instantiate(prefab, at, rotation(name));
            var wear = go.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.enabled = false;
            }
            return go;
        }
        return null;
    }

    private static void RemovePiece(GameObject go)
    {
        if (go != null && ZNetScene.instance != null)
        {
            ZNetScene.instance.Destroy(go);
        }
    }

    // Distance from a point along a direction to that piece (-1 = not hit within reach).
    private static float PieceAhead(GameObject piece, Vector3 from, Vector3 dir, float reach)
    {
        var mask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid");
        foreach (var hit in Physics.RaycastAll(from, dir, reach, mask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.collider.transform.IsChildOf(piece.transform))
            {
                return hit.distance;
            }
        }
        return -1f;
    }

    // Does the camera look through that piece? True when the piece's surface lies between the camera and one of the
    // four corners or the middle of its near plane (what is nearer than that plane is not drawn).
    private static bool CutsInto(Camera camera, GameObject piece)
    {
        var from = camera.transform.position;
        var near = camera.nearClipPlane;
        for (var i = 0; i < 5; i++)
        {
            var u = i == 4 ? 0.5f : i % 2;
            var v = i == 4 ? 0.5f : i / 2;
            var p = camera.ViewportToWorldPoint(new Vector3(u, v, near));
            var d = p - from;
            if (d.sqrMagnitude > 0.000001f && PieceAhead(piece, from, d.normalized, d.magnitude) >= 0f)
            {
                return true;
            }
        }
        return false;
    }

    // Spyglass up for a second and down again in front of that piece, every drawn frame looked at. result: [0] frames
    // at the eye, [1] of them cutting into the piece, [2] frames on the way cutting into it, [3] 1 = done.
    private static IEnumerator UpAndDownAt(Player player, GameCamera cam, GameObject piece, int[] result)
    {
        result[0] = result[1] = result[2] = result[3] = 0;
        yield return WaitReal(() => Scope.CanRaise(player), 4f);
        Scope.TestRequestRaise();
        var t = Time.unscaledTime;
        var lowered = false;
        while (Time.unscaledTime - t < 6f)
        {
            yield return new WaitForEndOfFrame();
            if (Scope.Active && piece != null)
            {
                var atEye = (cam.transform.position - player.m_eye.position).magnitude < 0.05f;
                var cuts = CutsInto(cam.m_camera, piece);
                if (atEye)
                {
                    result[0]++;
                    result[1] += cuts ? 1 : 0;
                }
                else
                {
                    result[2] += cuts ? 1 : 0;
                }
            }
            if (!lowered && Scope.State == ScopeState.Raised && Time.unscaledTime - t > 1.2f)
            {
                Scope.TestRequestLower();
                lowered = true;
            }
            else if (lowered && Scope.State == ScopeState.Idle)
            {
                result[3] = 1;
                break;
            }
        }
    }

    private static IEnumerator RunCloseReal()
    {
        var c = new Checks(CloseRealTest);
        if (!Ready(CloseRealTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, CloseRealTest);
        GameObject wall = null;
        GameObject roof = null;
        GameObject behind = null;
        try
        {
            rig.KeepPlace();
            rig.Noon();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            Scope.TestHoldToLook = false;
            Scope.TestSetMagnification(4f);
            var yaw = player.transform.eulerAngles.y;
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var feet = player.transform.position;
            var radius = player.m_collider != null ? player.m_collider.radius : 0.4f;
            var result = new int[4];
            // A floor piece stood on its edge is a wall too (the only piece name other MC tests have checked in game).
            Quaternion Upright(string name) => name.StartsWith("wood_floor", StringComparison.Ordinal)
                ? Quaternion.LookRotation(fwd) * Quaternion.Euler(90f, 0f, 0f)
                : Quaternion.LookRotation(fwd);
            Quaternion Flat(string name) => Quaternion.Euler(0f, yaw, 0f);

            // Face first at a real wall: its near side a hand's width from the body.
            rig.Look(yaw, 0f);
            yield return new WaitForSeconds(0.5f);
            wall = SpawnPiece(new[] { "woodwall", "stone_wall_4x2", "wood_floor" }, feet + fwd * 1.5f + Vector3.up * 1f, Upright, out var wallName);
            yield return FixedSteps(3);
            var ahead = wall != null ? PieceAhead(wall, player.m_eye.position, fwd, 4f) : -1f;
            if (c.Check(wall != null && ahead > 0f, $"a real wall piece in front of the player ({wallName}, {F(ahead)} m ahead)"))
            {
                // Nearer to the eye than the near plane the game uses in the open (0.5 m): only the spyglass's own
                // near plane keeps it drawn.
                wall.transform.position -= fwd * (ahead - (radius + 0.05f));
                Physics.SyncTransforms();
                yield return FixedSteps(3);
                ahead = PieceAhead(wall, player.m_eye.position, fwd, 4f);
                c.Check(ahead > 0.15f && ahead < radius + 0.35f,
                    $"moved right before the face ({F(ahead)} m from the eye, body radius {F(radius)}; the game's near plane in the open is {F(cam.m_nearClipPlaneMax)} m)");
                yield return UpAndDownAt(player, cam, wall, result);
                c.Check(result[3] == 1 && result[0] > 10, $"raised and lowered face first at the wall ({result[0]} frames at the eye)");
                c.Check(result[1] == 0, $"at the eye the wall is never cut away: on {result[1]} of {result[0]} frames a corner of the near plane lay behind its surface");
                c.Note($"on the way to the eye and back: {result[2]} frame(s) with the wall nearer than the near plane");
            }
            RemovePiece(wall);
            wall = null;

            // A real roof (or floor) piece right over the head, looking up at it.
            var eyeUp = player.m_eye.position;
            roof = SpawnPiece(new[] { "wood_roof", "wood_floor" }, eyeUp + Vector3.up * 1.2f, Flat, out var roofName);
            yield return FixedSteps(3);
            var above = roof != null ? PieceAhead(roof, eyeUp, Vector3.up, 4f) : -1f;
            if (above < 0f && roof != null && roofName != "wood_floor")
            {
                // Sloped piece with its surface elsewhere: the flat one is over the head for sure.
                RemovePiece(roof);
                roof = SpawnPiece(new[] { "wood_floor" }, eyeUp + Vector3.up * 1.2f, Flat, out roofName);
                yield return FixedSteps(3);
                above = roof != null ? PieceAhead(roof, eyeUp, Vector3.up, 4f) : -1f;
            }
            if (c.Check(roof != null && above > 0f, $"a real roof piece over the player ({roofName}, {F(above)} m over the eye)"))
            {
                roof.transform.position -= Vector3.up * (above - 0.35f);
                Physics.SyncTransforms();
                rig.Look(yaw, -70f);
                yield return FixedSteps(3);
                yield return new WaitForSeconds(0.5f);
                above = PieceAhead(roof, player.m_eye.position, Vector3.up, 4f);
                c.Check(above > 0.1f && above < 0.8f, $"lowered to just over the head ({F(above)} m over the eye)");
                yield return UpAndDownAt(player, cam, roof, result);
                c.Check(result[3] == 1 && result[0] > 10, $"raised and lowered looking up at the roof ({result[0]} frames at the eye)");
                c.Check(result[1] == 0, $"at the eye the roof is never cut away: on {result[1]} of {result[0]} frames a corner of the near plane lay behind its surface");
                c.Note($"on the way to the eye and back: {result[2]} frame(s) with the roof nearer than the near plane");
            }
            RemovePiece(roof);
            roof = null;

            // Back about 1 m from a real wall: own body by the rule on every frame.
            rig.Look(yaw, 0f);
            behind = SpawnPiece(new[] { "woodwall", "stone_wall_4x2", "wood_floor" }, feet - fwd * 2f + Vector3.up * 1f, Upright, out var backName);
            yield return FixedSteps(3);
            var back = behind != null ? PieceAhead(behind, player.m_eye.position, -fwd, 5f) : -1f;
            if (c.Check(behind != null && back > 0f, $"a real wall piece behind the player ({backName}, {F(back)} m behind)"))
            {
                behind.transform.position += fwd * (back - 1f);
                Physics.SyncTransforms();
                yield return new WaitForSeconds(0.8f);
                back = PieceAhead(behind, player.m_eye.position, -fwd, 5f);
                var pulled = (cam.transform.position - player.m_eye.position).magnitude;
                var watch = new DrawWatch();
                var done = new bool[1];
                yield return UpAndDownWatched(player, cam, watch, done);
                c.Check(done[0] && back > 0.6f && back < 1.4f, $"raised and lowered with the back {F(back)} m from the wall (camera {F(pulled)} m from the eye before)");
                c.Check(!watch.BodySeen && !watch.BodyGone,
                    $"own body hidden on every frame the camera is in the head or within 2 m of the feet, shown on the others ({watch.Frames} frames; seen {watch.BodySeen}, wrongly hidden {watch.BodyGone})");
                yield return FixedSteps(3);
                c.Check(BodyFar(player) == !player.m_lodVisible, "afterwards the body follows the game's own rule again");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
            RemovePiece(wall);
            RemovePiece(roof);
            RemovePiece(behind);
        }
    }

    // ---------- real death: spyglass.death ----------

    // Me = what a real death takes from the test player, kept aside and given back to the player the game puts in.
    private sealed class DeathKit
    {
        internal readonly Player Old;
        internal readonly GameCamera Cam;
        internal bool Emptied;
        internal bool Given;
        internal int CorpseRun;

        private readonly bool _god;
        private readonly bool _ghost;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly Dictionary<Skills.SkillType, Vector2> _skills = new Dictionary<Skills.SkillType, Vector2>();
        private readonly List<Player.Food> _foods;
        private readonly float _health;
        private readonly float _sinceDeath;
        private readonly PlayerProfile.WorldPlayerData _world;
        private readonly bool _hadDeathPoint;
        private readonly Vector3 _deathPoint;
        private readonly ZPackage _bag = new ZPackage();
        private readonly bool _sawTutorial;
        private readonly float _zoomPick;
        private readonly bool _todOn;
        private readonly float _tod;
        private readonly string _debugEnv;
        private readonly HashSet<ZNetView> _gravesBefore = new HashSet<ZNetView>();

        internal DeathKit(Player old, GameCamera cam)
        {
            Old = old;
            Cam = cam;
            _god = old.InGodMode();
            _ghost = old.InGhostMode();
            _position = old.transform.position;
            _rotation = old.transform.rotation;
            foreach (var s in old.GetSkills().m_skillData)
            {
                _skills[s.Key] = new Vector2(s.Value.m_level, s.Value.m_accumulator);
            }
            _foods = new List<Player.Food>(old.m_foods);
            _health = old.GetHealth();
            _sinceDeath = old.m_timeSinceDeath;
            _world = Game.instance.GetPlayerProfile().GetWorldData(ZNet.instance.GetWorldUID());
            _hadDeathPoint = _world.m_haveDeathPoint;
            _deathPoint = _world.m_deathPoint;
            old.GetInventory().Save(_bag);
            _sawTutorial = old.m_shownTutorials.Contains("death");
            _zoomPick = Scope.TestRawMagnification;
            var env = EnvMan.instance;
            _todOn = env.m_debugTimeOfDay;
            _tod = env.m_debugTime;
            _debugEnv = env.m_debugEnv;
            foreach (var view in NewGraves())
            {
                _gravesBefore.Add(view);
            }
        }

        // Tombstones made since the test began.
        internal List<ZNetView> NewGraves()
        {
            var list = new List<ZNetView>();
            var scene = ZNetScene.instance;
            if (scene == null)
            {
                return list;
            }
            foreach (var view in scene.m_instances.Values)
            {
                if (view != null && !_gravesBefore.Contains(view) && view.GetComponent<TombStone>() != null)
                {
                    list.Add(view);
                }
            }
            return list;
        }

        // Bag aside: only a spyglass in it, in the right hand (the tombstone then holds only that). Death tutorial
        // marked as seen for the death (no raven).
        internal ItemDrop.ItemData Empty()
        {
            Old.m_shownTutorials.Add("death");
            Old.UnequipAllItems();
            Old.GetInventory().RemoveAll();
            Emptied = true;
            var spy = Old.GetInventory().AddItem(SpyglassContent.ItemName, 1, 1, 0, 0L, "", false);
            if (spy != null)
            {
                Old.EquipItem(spy, false);
            }
            return spy != null && Old.GetRightItem() == spy ? spy : null;
        }

        // Noon, clear weather (like console "tod 0.5", "env Clear"); put back by Finish.
        internal void NoonClear()
        {
            var env = EnvMan.instance;
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0.5f;
            env.m_debugEnv = "Clear";
        }

        internal void GiveBack(Player player)
        {
            Given = true;
            if (!_sawTutorial)
            {
                player.m_shownTutorials.Remove("death");
            }
            player.UnequipAllItems();
            player.GetInventory().RemoveAll();
            _bag.SetPos(0);
            player.GetInventory().Load(_bag);
            player.EquipInventoryItems();
            var data = player.GetSkills().m_skillData;
            foreach (var type in new List<Skills.SkillType>(data.Keys))
            {
                if (!_skills.ContainsKey(type))
                {
                    data.Remove(type);
                }
            }
            foreach (var saved in _skills)
            {
                var skill = player.GetSkills().GetSkill(saved.Key);
                skill.m_level = saved.Value.x;
                skill.m_accumulator = saved.Value.y;
            }
            if (!ReferenceEquals(player, Old))
            {
                player.m_foods.Clear();
                player.m_foods.AddRange(_foods);
                player.m_timeSinceDeath = _sinceDeath;
                if (_sinceDeath > player.m_hardDeathCooldown)
                {
                    player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectSoftDeath, true);
                }
                MovePlayer(player, _position + Vector3.up * 0.1f);
                player.transform.rotation = _rotation;
                if (player.m_body != null)
                {
                    player.m_body.rotation = _rotation;
                }
                player.m_maxAirAltitude = player.transform.position.y;
                if (Minimap.instance != null)
                {
                    Minimap.instance.RemovePin(_position, 3f);
                }
                _world.m_haveDeathPoint = _hadDeathPoint;
                _world.m_deathPoint = _deathPoint;
            }
            player.SetHealth(Mathf.Min(_health, player.GetMaxHealth()));
        }

        // finally of the test (no yield): everything back on whoever is the local player now.
        internal void Finish(string test)
        {
            try
            {
                ClearOverrides();
                Scope.Abort();
                Scope.TestRawMagnification = _zoomPick;
                var env = EnvMan.instance;
                if (env != null)
                {
                    env.m_debugTimeOfDay = _todOn;
                    env.m_debugTime = _tod;
                    env.m_debugEnv = _debugEnv;
                }
                if (InventoryGui.instance != null && InventoryGui.IsVisible())
                {
                    InventoryGui.instance.Hide();
                }
                var player = Player.m_localPlayer;
                if (player != null)
                {
                    if (Emptied && !Given)
                    {
                        GiveBack(player);
                    }
                    if (!_sawTutorial)
                    {
                        player.m_shownTutorials.Remove("death");
                    }
                    if (CorpseRun != 0)
                    {
                        player.GetSEMan().RemoveStatusEffect(CorpseRun, true);
                    }
                    player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectBurning, true);
                    player.SetGodMode(_god);
                    player.SetGhostMode(_ghost);
                }
                // Tombstones of the test hold nothing but its spyglass.
                foreach (var view in NewGraves())
                {
                    if (view.IsValid())
                    {
                        view.ClaimOwnership();
                    }
                    ZNetScene.instance.Destroy(view.gameObject);
                }
            }
            catch (Exception e)
            {
                SelfTest.Note(test, $"clean-up after the death failed: {e.Message}");
            }
        }
    }

    // Me = what was seen of one real death.
    private sealed class DeathFacts
    {
        internal readonly List<string> Problems = new List<string>();
        internal float FogBefore;
        internal float FogUp;
        internal bool FogCleared;
        internal float ThirdPerson;
        internal float LowHealth;
        internal bool ByBurning;
        internal bool StayedUp = true;
        internal string AtDeath = "";
        internal float AtDeathFov;
        internal float AtDeathFog;
        internal float CameraFromEye = -1f;
        internal float CameraFromEyeMost = -1f;
        internal float CameraFromEyeLeast = -1f;
        internal float AtDeathLookOff = -1f;   // first dead frame: degrees from camera forward to body (-1 = no body)
        internal float AtDeathShake;           // vanilla camera shake that frame, degrees per axis at most
        internal float AtDeathAhead;           // camera from last living eye along its look (below 0 = behind)
        internal string Showed = "";
        internal float FovOff;
        internal bool RagdollMade;
        internal bool BodySeen;
        internal int DeadFrames;
        internal float RespawnAfter;
        internal Player Now;
        internal int ProblemMark;
        internal int ErrorMark;
    }

    // A real death: on fire like a normal player, spyglass raised while burning, health low, killed by the next tick
    // of the fire; then the game's own respawn (10 s). Problems filled = it could not be played that far.
    private static IEnumerator PlayDeath(DeathKit kit, DeathFacts f, string test)
    {
        var old = kit.Old;
        var cam = kit.Cam;
        var gone = SendAwayEnemies(old);
        if (gone.Length > 0)
        {
            SelfTest.Note(test, "hostile creature(s) at the test player removed before the test: " + gone);
        }
        var spy = kit.Empty();
        if (spy == null)
        {
            f.Problems.Add("the spyglass could not be given and held");
            yield break;
        }
        kit.NoonClear();
        ServerRules.TestRules = new SpyglassRules();
        Scope.TestHoldToLook = false;
        Scope.TestSetMagnification(4f);
        old.m_lookYaw = Quaternion.Euler(0f, OpenYaw(old, out _), 0f);
        old.m_lookPitch = -1f;
        old.UpdateEyeRotation();
        old.m_lookDir = old.m_eye.forward;
        var fog = new float[1];
        yield return FogSettled(fog, 15f);
        f.FogBefore = fog[0];
        yield return new WaitForEndOfFrame();
        f.ThirdPerson = (cam.transform.position - old.m_eye.position).magnitude;
        f.ProblemMark = _tap.Problems().Count;
        f.ErrorMark = _tap.ErrorMark;

        // On fire. God mode and ghost mode both keep a player at 1 health (Character.ApplyDamage): off, like a normal
        // player. Full health first: this fire alone must not kill before the spyglass is up.
        old.SetHealth(old.GetMaxHealth());
        old.SetGodMode(false);
        old.SetGhostMode(false);
        // Wet player burn out 6 times faster (SE_Burning.UpdateStatusEffect add 5 dt more): me dry it first (weather
        // Clear keep it dry). Parked runs of 2026-10-13/14: fire did not kill in first death of run, did in next one.
        old.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectWet, true);
        var fire = new HitData { m_point = old.GetCenterPoint(), m_dir = Vector3.up, m_hitType = HitData.HitType.EnemyHit };
        fire.m_damage.m_fire = ThroughArmor(old, 10f);
        old.Damage(fire);
        yield return WaitReal(() => old == null || old.IsDead() || old.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectBurning), 2f);
        if (old == null || old.IsDead() || !old.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectBurning))
        {
            f.Problems.Add("the player did not catch fire (or died at once)");
            yield break;
        }

        // Raised while burning.
        yield return WaitReal(() => Scope.CanRaise(old), 4f);
        Scope.TestRequestRaise();
        yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
        yield return Frames(5);
        yield return new WaitForEndOfFrame();
        if (old == null || old.IsDead() || Scope.State != ScopeState.Raised || !ScopeOverlay.Shown)
        {
            f.Problems.Add($"the spyglass did not go up while burning ({Scope.State})");
            yield break;
        }
        f.FogCleared = ScopeCamera.FogApplied;
        f.FogUp = RenderSettings.fogDensity;
        var burning = old.GetSEMan().GetStatusEffect(SEMan.s_statusEffectBurning) as SE_Burning;
        if (burning == null || burning.m_fireDamageLeft < 1f)
        {
            // Burnt out meanwhile: lit again (fire alone never lowers the spyglass).
            var again = new HitData { m_point = old.GetCenterPoint(), m_dir = Vector3.up, m_hitType = HitData.HitType.EnemyHit };
            again.m_damage.m_fire = ThroughArmor(old, 10f);
            old.Damage(again);
            yield return Frames(3);
            burning = old.GetSEMan().GetStatusEffect(SEMan.s_statusEffectBurning) as SE_Burning;
        }
        if (burning == null || Scope.State != ScopeState.Raised)
        {
            f.Problems.Add($"not burning with the spyglass up any more ({Scope.State})");
            yield break;
        }

        // Low health: the next tick of the fire kills.
        old.SetHealth(Mathf.Clamp(burning.m_fireDamagePerHit * 0.5f, 0.12f, 1f));
        f.LowHealth = old.GetHealth();
        var eye = old.m_eye.position;
        var look = old.m_eye.forward;
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 12f)
        {
            yield return new WaitForEndOfFrame();
            if (old == null || old.IsDead())
            {
                break;
            }
            // Health held low until fire kill: food regen (Player.UpdateFood) may lift it over the ticks left.
            if (old.GetHealth() > f.LowHealth)
            {
                old.SetHealth(f.LowHealth);
            }
            f.StayedUp &= Scope.State == ScopeState.Raised;
            eye = old.m_eye.position;
            look = old.m_eye.forward;
        }
        if (old == null || !old.IsDead())
        {
            f.Problems.Add($"the burning did not kill the player within 12 s (health {F(f.LowHealth)}, {F(burning.m_fireDamagePerHit)} per tick)");
            yield break;
        }
        // First frame drawn with the player dead.
        f.ByBurning = old.m_lastHit != null && old.m_lastHit.m_hitType == HitData.HitType.Burning;
        f.AtDeath = OnScreen();
        f.AtDeathFov = cam.m_camera.fieldOfView;
        f.AtDeathFog = RenderSettings.fogDensity;
        f.CameraFromEye = (cam.transform.position - eye).magnitude;
        f.CameraFromEyeMost = f.CameraFromEye;
        f.CameraFromEyeLeast = f.CameraFromEye;
        // This frame the turn is DeathView's own (vanilla LookAt was from eye spot; later frames vanilla turn it
        // itself): body straight ahead, but for the shake vanilla put on top (GameCamera.UpdateCameraShake, angle per
        // axis at most intensity^3 x m_shakeMovement). No physics step since LateUpdate: body where camera looked at it.
        f.AtDeathAhead = Vector3.Dot(cam.transform.position - eye, look);
        f.AtDeathShake = cam.GetAttenuatedShakeIntensity() * Mathf.Abs(cam.m_shakeMovement);
        var deadBody = old.GetRagdoll();
        if (deadBody != null)
        {
            var toBody = deadBody.GetAverageBodyPosition() - cam.transform.position;
            f.AtDeathLookOff = Vector3.Angle(cam.transform.forward, toBody);
        }

        // Dead until the game puts a new player in: every frame of the death camera.
        var died = Time.realtimeSinceStartup;
        Player now = null;
        while (Time.realtimeSinceStartup - died < 70f)
        {
            yield return new WaitForEndOfFrame();
            now = Player.m_localPlayer;
            if (now != null && !ReferenceEquals(now, old) && !now.IsDead())
            {
                break;
            }
            now = null;
            f.DeadFrames++;
            if (f.Showed.Length == 0)
            {
                f.Showed = OnScreen();
            }
            f.FovOff = Mathf.Max(f.FovOff, Mathf.Abs(cam.m_camera.fieldOfView - cam.m_fov));
            // Camera drawn on every dead frame, also after game took old player away before respawn.
            var fromEye = (cam.transform.position - eye).magnitude;
            f.CameraFromEyeMost = Mathf.Max(f.CameraFromEyeMost, fromEye);
            f.CameraFromEyeLeast = Mathf.Min(f.CameraFromEyeLeast, fromEye);
            if (old != null)
            {
                var ragdoll = old.GetRagdoll();
                if (ragdoll != null && !f.BodySeen)
                {
                    f.RagdollMade = true;
                    foreach (var r in ragdoll.GetComponentsInChildren<Renderer>())
                    {
                        f.BodySeen |= r != null && r.enabled && r.isVisible;
                    }
                }
            }
        }
        if (now == null)
        {
            f.Problems.Add("the game put no new player in within 70 s of the death");
            yield break;
        }
        f.Now = now;
        f.RespawnAfter = Time.realtimeSinceStartup - died;
        // New player is a normal one (no god mode): nothing of the world may kill it while the test looks at it.
        // Finish puts both as they were.
        now.SetGodMode(true);
        now.SetGhostMode(true);
        yield return WaitReal(() => !now.IsTeleporting() && ZNetScene.instance.IsAreaReady(now.transform.position), 20f);
        yield return new WaitForSeconds(1f);
    }

    private static IEnumerator RunDeath()
    {
        const string T = DeathTest;
        var c = new Checks(T);
        if (!Ready(T, out var old, out var cam))
        {
            yield break;
        }
        if (Game.instance == null || ZNet.instance == null || EnvMan.instance == null || _tap == null)
        {
            SelfTest.Fail(T, "no world or the log is not listened to");
            yield break;
        }
        var kit = new DeathKit(old, cam);
        var f = new DeathFacts();
        try
        {
            yield return PlayDeath(kit, f, T);
            if (!c.Check(f.Problems.Count == 0,
                    f.Problems.Count == 0
                        ? $"a real death played: on fire, spyglass raised while burning, health {F(f.LowHealth)}, killed by the fire, new player {F(f.RespawnAfter)} s later"
                        : "the death could not be played: " + string.Join("; ", f.Problems.ToArray())))
            {
                c.Report();
                yield break;
            }
            var now = f.Now;
            c.Check(f.ByBurning && f.StayedUp, $"died from the burning with the spyglass up until the frame of the death (last hit burning {f.ByBurning}, up all the way {f.StayedUp})");
            c.Check(f.AtDeath.Length == 0 && Near(f.AtDeathFov, cam.m_fov, 0.01f),
                "on the first frame drawn dead: " + (f.AtDeath.Length == 0 ? "no round view, blur, dark ring, cleared fog or hidden body" : f.AtDeath)
                + $", zoom {F(f.AtDeathFov)} of {F(cam.m_fov)}");
            // "No cleared fog" at death only means something when the fog really was thinner while looking (Distant
            // Horizons running, clear weather). Not so = failed check, never a quiet pass of that part of the item.
            if (c.Check(f.FogCleared && f.FogUp < f.FogBefore * 0.9f,
                    f.FogCleared
                        ? $"while looking, before the death, the fog was thinner than before the raise (density {D(f.FogUp)}, {D(f.FogBefore)} before; under 90% of it expected in clear weather)"
                        : "the fog was not cleared while looking (Distant Horizons is not loaded and active in this run): the \"no cleared fog\" part of TESTING.md T26 was not checked"))
            {
                c.Check(Mathf.Abs(f.AtDeathFog - f.FogBefore) <= f.FogBefore * 0.05f + 0.0000001f,
                    $"the fog cleared while looking is back on that frame (density {D(f.AtDeathFog)}, {D(f.FogBefore)} before the raise)");
            }
            c.Check(f.DeadFrames > 30 && f.Showed.Length == 0 && f.FovOff <= 0.01f,
                f.Showed.Length == 0
                    ? $"nothing of the spyglass on any of the {f.DeadFrames} frames of the death camera, zoom never off by more than {F(f.FovOff)}"
                    : $"on the death camera: {f.Showed}");
            c.Check(f.RagdollMade && f.BodySeen, $"the dead body is there and drawn on the death camera (body made {f.RagdollMade}, drawn {f.BodySeen})");
            // Game leave dead player's camera where it was while body there, only turn it to body: without spyglass
            // that is third-person spot. At eye = look out of dead body's head (bug fixed in 0.1.3). Same distance as
            // before raise within 0.5 m or 30% (player stood still in between, test never turn it).
            var thirdPersonSlack = Mathf.Max(0.5f, f.ThirdPerson * 0.3f);
            c.Check(f.CameraFromEye > 0.5f && f.CameraFromEyeLeast > 0.5f && Mathf.Abs(f.CameraFromEye - f.ThirdPerson) <= thirdPersonSlack,
                $"the normal death view is back at once: death camera {F(f.CameraFromEye)} m from where the dead player's eye was on the first frame drawn dead "
                + $"(between {F(f.CameraFromEyeLeast)} and {F(f.CameraFromEyeMost)} m until the respawn; above 0.5 m expected, out of the head), "
                + $"third person before the raise {F(f.ThirdPerson)} m (within {F(thirdPersonSlack)} m expected)");
            // First dead frame turned by DeathView itself: looks at the body, from behind the eye's last look (wrong
            // turn or wrong side at right distance = fail here). Shake (none from fire: no stagger damage) allowed for.
            var lookSlack = 3f + 1.5f * f.AtDeathShake;
            c.Check(f.AtDeathLookOff >= 0f && f.AtDeathLookOff <= lookSlack && f.AtDeathAhead < 0f,
                $"on the first frame drawn dead the camera looks at the dead body from behind: {F(f.AtDeathLookOff)} degrees off the way to the body "
                + $"(at most {F(lookSlack)} expected with that frame's camera shake; -1 = no body yet), "
                + $"{F(-f.AtDeathAhead)} m behind the eye along its last look (above 0 expected)");

            // After the respawn: normal view, spyglass down and in the tombstone.
            yield return Frames(5);
            yield return new WaitForEndOfFrame();
            var gap = (cam.transform.position - now.m_eye.position).magnitude;
            var left = NotBack(now);
            c.Check(left.Length == 0 && OnScreen().Length == 0 && Near(cam.m_camera.fieldOfView, cam.m_fov, 0.01f) && gap > 0.5f && Hud.instance.m_crosshair.enabled,
                $"after the respawn the view is normal: zoom {F(cam.m_camera.fieldOfView)}, camera {F(gap)} m from the eye, crosshair on" + (left.Length == 0 ? "" : " (" + left + ")"));
            TombStone tomb = null;
            foreach (var view in kit.NewGraves())
            {
                var box = view.GetComponent<Container>();
                if (box != null && box.GetInventory() != null && CountSpyglasses(box.GetInventory()) > 0)
                {
                    tomb = view.GetComponent<TombStone>();
                }
            }
            c.Check(Scope.State == ScopeState.Idle && now.GetRightItem() == null && CountSpyglasses(now.GetInventory()) == 0 && tomb != null,
                $"the spyglass is down: not on the new player, in the tombstone ({Scope.State}, tombstone with it {tomb != null})");
            if (tomb != null)
            {
                // Taken back like the Use key on the own tombstone.
                kit.CorpseRun = tomb.m_lootStatusEffect != null ? tomb.m_lootStatusEffect.NameHash() : 0;
                var side = now.transform.position - tomb.transform.position;
                side.y = 0f;
                side = side.sqrMagnitude > 0.01f ? side.normalized : Vector3.forward;
                var stand = tomb.transform.position + side * 1f;
                stand.y = ZoneSystem.instance.GetGroundHeight(stand) + 0.1f;
                MovePlayer(now, stand);
                yield return FixedSteps(5);
                tomb.Interact(now, false, false);
                yield return WaitReal(() => CountSpyglasses(now.GetInventory()) > 0, 6f);
                ItemDrop.ItemData back = null;
                foreach (var item in now.GetInventory().GetAllItems())
                {
                    if (SpyglassContent.IsSpyglass(item))
                    {
                        back = item;
                    }
                }
                if (c.Check(back != null, "the spyglass is taken back from the tombstone"))
                {
                    if (now.GetRightItem() != back)
                    {
                        now.EquipItem(back, false);
                    }
                    yield return WaitReal(() => now.GetRightItem() == back && Scope.CanRaise(now), 6f);
                    yield return Click(now);
                    yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
                    yield return Frames(5);
                    yield return new WaitForEndOfFrame();
                    c.Check(Scope.State == ScopeState.Raised && ScopeOverlay.Shown && Flag(now)
                            && Near(cam.m_camera.fieldOfView, ScopeCamera.ZoomedFov(cam.m_fov, 4f), 0.05f),
                        $"picked up again, a click raises it: round view and zoom x4 ({Scope.State}, fov {F(cam.m_camera.fieldOfView)})");
                    yield return LowerDown();
                    yield return Frames(3);
                    c.Check(NotBack(now).Length == 0, "and it comes down again");
                }
            }
            var problems = ProblemsSince(f.ProblemMark);
            var named = ErrorsNamingMe(f.ErrorMark);
            c.Check(problems.Count == 0 && named.Count == 0,
                problems.Count == 0 && named.Count == 0
                    ? $"the log has no warning or error from the spyglass and no error naming its code, from the fire to after the respawn ({_tap.ErrorsSince(f.ErrorMark).Count} error line(s) of others)"
                    : $"{problems.Count} warning or error line(s) from the spyglass and {named.Count} error(s) naming its code: {First(problems.Count > 0 ? problems : named)}");
            kit.GiveBack(now);
            c.Report();
        }
        finally
        {
            kit.Finish(T);
        }
    }

    // Render scale of the game (UpscaledFrameBuffer statics, its picture field) through reflection, never a direct
    // reference: the dedicated server's assembly_valheim has the class without these fields, and the Debug JitCheck
    // failed there on a direct use (multiplayer run 2026-10-14). On the server all of it reads as absent.
    private static class RenderScale
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type BufferType = typeof(Player).Assembly.GetType("UpscaledFrameBuffer");
        private static readonly FieldInfo AutoField = BufferType != null ? BufferType.GetField("m_autoTargetResolution", Any | BindingFlags.Static) : null;
        private static readonly FieldInfo TargetField = BufferType != null ? BufferType.GetField("m_targetResolutionVertical", Any | BindingFlags.Static) : null;
        private static readonly FieldInfo TextureField = BufferType != null ? BufferType.GetField("m_renderTexture", Any | BindingFlags.Instance) : null;

        internal static bool Auto
        {
            get => AutoField != null && (bool)AutoField.GetValue(null);
            set => AutoField?.SetValue(null, value);
        }

        internal static uint Target
        {
            get => TargetField != null ? (uint)TargetField.GetValue(null) : uint.MaxValue;
            set => TargetField?.SetValue(null, value);
        }

        internal static Component FindBuffer() =>
            BufferType != null && TextureField != null ? Object.FindAnyObjectByType(BufferType, FindObjectsInactive.Include) as Component : null;

        internal static RenderTexture Texture(Component buffer) =>
            buffer != null && TextureField != null ? TextureField.GetValue(buffer) as RenderTexture : null;
    }
}
#endif
