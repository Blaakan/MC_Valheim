using UnityEngine;
#if !OFFLINE_PREVIEW
using System;
using MC.Shared;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
#endif
#if DEBUG && !OFFLINE_PREVIEW
using System.Collections.Generic;
#endif

namespace MC.Exploration.MusicInstrumentsMod;

// Me = the item icons (flute, lyre, tambourine), the Music status effect icon and the package icon (package only in
// debug export and offline preview), drawn in code once (no asset file, like Spyglass's SpyglassIcon). Each picture =
// one sampler: colour at a point of the 128-pixel design space (IconKit), sampled 4x4 per pixel = soft edges. Shading
// lit from upper left, dark outline so icons read on dark inventory slots, straight alpha. Sprites made on first ask
// and kept whole session (items and status effect always point at them): never destroyed. No graphics device
// (dedicated server) = no texture, null back.
// Samplers are pure math (no texture, no Unity object): offline preview tool build same file with OFFLINE_PREVIEW.
internal static class InstrumentIcons
{
    internal const int Size = 128;

    // Outline width (design pixel).
    private const float O = 1.6f;

    private static readonly Color Clear = IconKit.Clear;
    private static readonly Color Outline = new Color(0.10f, 0.07f, 0.05f, 1f);
    private static readonly Vector2 Mid = new Vector2(64f, 64f);

#if DEBUG || OFFLINE_PREVIEW
    // ---------- export table (debug game export and offline preview use same list; not in release) ----------

    internal static readonly string[] ExportNames =
        { "icon-256.png", "flute-128.png", "lyre-128.png", "tambourine-128.png", "music-effect-128.png" };

    internal static readonly int[] ExportSizes = { 256, Size, Size, Size, Size };

    internal static readonly IconKit.Sampler[] ExportSamplers = { Package, Flute, Lyre, Tambourine, MusicNotes };
#endif

    // Delegates made once (no new delegate per call).
    private static readonly IconKit.Sampler FluteSampler = Flute;
    private static readonly IconKit.Sampler LyreSampler = Lyre;
    private static readonly IconKit.Sampler TambourineSampler = Tambourine;
    private static readonly IconKit.Sampler NotesSampler = MusicNotes;

    // Picture of an instrument (null = not ours).
    internal static IconKit.Sampler SamplerOf(InstrumentKind kind)
    {
        switch (kind)
        {
            case InstrumentKind.Flute:
                return FluteSampler;
            case InstrumentKind.Lyre:
                return LyreSampler;
            case InstrumentKind.Tambourine:
                return TambourineSampler;
            default:
                return null;
        }
    }

#if !OFFLINE_PREVIEW
    // Index = (int)InstrumentKind (1..3).
    private static readonly Sprite[] Sprites = new Sprite[4];
    private static readonly bool[] Failed = new bool[4];
    private static Sprite _effect;
    private static bool _effectFailed;

    private static bool HasGraphics => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

    // Item icon of this instrument. Null = no graphics device, not ours, or drawing failed (one error line).
    // Main thread (texture). First call per kind draw it (~0.1 s), then same sprite whole session.
    internal static Sprite Get(InstrumentKind kind)
    {
        var i = (int)kind;
        if (i <= 0 || i >= Sprites.Length || !HasGraphics)
        {
            return null;
        }
        if (Sprites[i] != null)
        {
            return Sprites[i];
        }
        if (Failed[i])
        {
            return null;
        }
        try
        {
            Sprites[i] = MakeSprite(SamplerOf(kind), "MC_Icon_" + kind);
            return Sprites[i];
        }
        catch (Exception e)
        {
            Failed[i] = true;
            PatchGuard.Report("InstrumentIcons.Get", e);
            return null;
        }
    }

    // Music status effect icon (two beamed notes, gold glow). Null = no graphics device or drawing failed.
    internal static Sprite EffectIcon()
    {
        if (!HasGraphics)
        {
            return null;
        }
        if (_effect != null)
        {
            return _effect;
        }
        if (_effectFailed)
        {
            return null;
        }
        try
        {
            _effect = MakeSprite(NotesSampler, "MC_Icon_Music");
            return _effect;
        }
        catch (Exception e)
        {
            _effectFailed = true;
            PatchGuard.Report("InstrumentIcons.EffectIcon", e);
            return null;
        }
    }

    // Me make one session sprite: 128 px, mipmaps (slot show it ~48 px, effect bar ~32 px: no shimmer), CPU copy
    // dropped after upload.
    private static Sprite MakeSprite(IconKit.Sampler sampler, string name)
    {
        var tex = MakeTexture(sampler, Size, name, true, false);
        Sprite sprite;
        try
        {
            sprite = Sprite.Create(tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
        }
        catch
        {
            // No sprite = nobody hold texture: me throw it away, then caller report.
            Object.Destroy(tex);
            throw;
        }
        if (sprite == null)
        {
            Object.Destroy(tex);
            throw new InvalidOperationException("Sprite.Create returned nothing");
        }
        sprite.name = name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // readable = keep pixels on CPU (PNG export). Caller own texture.
    private static Texture2D MakeTexture(IconKit.Sampler sampler, int size, string name, bool mipmaps, bool readable)
    {
        var pixels = IconKit.Render(sampler, size);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipmaps)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        try
        {
            tex.SetPixels(pixels);
            tex.Apply(mipmaps, !readable);
            return tex;
        }
        catch
        {
            Object.Destroy(tex);
            throw;
        }
    }

#if DEBUG
    // Debug export: package icon + every icon as PNG (self test write them; never in release). Main thread. The
    // 256 px package icon take a few seconds. One icon fail = one error line, me skip it and give back the rest (self
    // test must go on).
    internal static List<KeyValuePair<string, byte[]>> ExportPngs()
    {
        var list = new List<KeyValuePair<string, byte[]>>();
        if (!HasGraphics)
        {
            return list;
        }
        for (var i = 0; i < ExportNames.Length; i++)
        {
            try
            {
                var tex = MakeTexture(ExportSamplers[i], ExportSizes[i], "MC_Export_" + ExportNames[i], false, true);
                try
                {
                    var png = tex.EncodeToPNG();
                    if (png == null || png.Length == 0)
                    {
                        throw new InvalidOperationException("EncodeToPNG gave nothing");
                    }
                    list.Add(new KeyValuePair<string, byte[]>(ExportNames[i], png));
                }
                finally
                {
                    Object.Destroy(tex);
                }
            }
            catch (Exception e)
            {
                PatchGuard.Report("InstrumentIcons.ExportPngs " + ExportNames[i], e);
            }
        }
        return list;
    }
#endif
#endif

    // ======================================================================================================
    // Wooden Flute: end-blown flute lying diagonal, foot lower left, beak (mouthpiece) upper right. Pale fine wood
    // with grain, six finger holes in two groups of three, window with its cut ramp under the beak, two dark rings.
    // ======================================================================================================

    private static readonly Vector2 FluteFoot = new Vector2(16f, 15f);
    private static readonly Vector2 FluteTip = new Vector2(113f, 112f);
    private static readonly float FluteLength = (FluteTip - FluteFoot).magnitude;
    private static readonly Vector2 FluteDir = (FluteTip - FluteFoot) / FluteLength;
    // Points up left: the lit side.
    private static readonly Vector2 FluteNormal = new Vector2(-FluteDir.y, FluteDir.x);

    private const float BeakLength = 9f;
    private const float HeadHalf = 8.4f;
    private const float BeakPower = 2.4f;

    // Finger holes: distance from the foot (pixel).
    private static readonly float[] FluteHoles = { 30f, 39.5f, 49f, 61f, 70.5f, 80f };

    private static readonly Color WoodLight = new Color(0.95f, 0.79f, 0.52f, 1f);
    private static readonly Color WoodMid = new Color(0.82f, 0.58f, 0.31f, 1f);
    private static readonly Color WoodGrain = new Color(0.56f, 0.36f, 0.19f, 1f);
    private static readonly Color WoodSheen = new Color(1f, 0.93f, 0.78f, 1f);
    private static readonly Color RingWood = new Color(0.36f, 0.21f, 0.11f, 1f);
    private static readonly Color RingWoodLight = new Color(0.50f, 0.31f, 0.16f, 1f);
    private static readonly Color HoleDark = new Color(0.11f, 0.06f, 0.03f, 1f);
    private static readonly Color HoleWall = new Color(0.45f, 0.28f, 0.14f, 1f);

    // Half width along the flute (a = pixel from foot): flared foot, body thin at foot, head wider.
    private static float FluteHalfWidth(float a)
    {
        if (a < 6.5f)
        {
            return Mathf.Lerp(8.0f, 6.9f, IconKit.SmoothStep(0f, 6.5f, a));
        }
        if (a < 90f)
        {
            return Mathf.Lerp(6.9f, 7.5f, (a - 6.5f) / 83.5f);
        }
        return HeadHalf;
    }

    private static Color Flute(Vector2 p)
    {
        var rel = p - FluteFoot;
        var a = rel.x * FluteDir.x + rel.y * FluteDir.y;
        var c = rel.x * FluteNormal.x + rel.y * FluteNormal.y;
        var ac = Mathf.Abs(c);
        if (a < -O || a > FluteLength + O || ac > 9.1f + O)
        {
            return Clear;
        }
        var beakStart = FluteLength - BeakLength;
        var col = Clear;
        if (a < beakStart)
        {
            var w = FluteHalfWidth(Mathf.Max(a, 0f));
            if (a >= 0f && ac <= w)
            {
                col = FluteWood(a, c, w, 1f);
            }
            else if (ac <= w + O)
            {
                col = Outline;
            }
        }
        else
        {
            // Beak: round-square end, a bit darker as it slope down to the mouth.
            var x = (a - beakStart) / BeakLength;
            if (IconKit.SuperEllipse(x, ac / HeadHalf, BeakPower) <= 1f)
            {
                var w = HeadHalf * Mathf.Pow(Mathf.Max(0.05f, 1f - Mathf.Pow(x, BeakPower)), 1f / BeakPower);
                col = FluteWood(a, c, w, 1f - 0.18f * x);
            }
            else if (IconKit.SuperEllipse((a - beakStart) / (BeakLength + O), ac / (HeadHalf + O), BeakPower) <= 1f)
            {
                col = Outline;
            }
        }
        col = FluteRing(col, a, c, 9f, 12.5f, 7.8f);
        col = FluteRing(col, a, c, 88.5f, 92.5f, 9.0f);
        if (col.a <= 0f || ac > 4.8f)
        {
            return col;
        }

        // Finger holes: dark, far wall (lower right) catch light, darker carved lip.
        for (var i = 0; i < FluteHoles.Length; i++)
        {
            var qa = (a - FluteHoles[i]) / 2.8f;
            var qc = c / 2.5f;
            var e = qa * qa + qc * qc;
            if (e <= 1f)
            {
                return Color.Lerp(HoleDark, HoleWall, IconKit.SmoothStep(-0.1f, 0.95f, -qc) * 0.9f);
            }
            if (e <= 1.45f)
            {
                return IconKit.Mul(col, qc > 0f ? 0.72f : 0.86f);
            }
        }

        // Window (sound hole) under the beak, with its cut ramp toward the foot.
        var win1 = FluteLength - 20f;
        var win0 = FluteLength - 25.5f;
        var ramp0 = FluteLength - 32f;
        if (a >= win0 && a <= win1)
        {
            if (ac <= 3.7f)
            {
                return Color.Lerp(HoleDark, HoleWall, IconKit.SmoothStep(0f, 1f, -c / 3.7f) * 0.75f);
            }
            if (ac <= 4.5f)
            {
                return IconKit.Mul(col, 0.7f);
            }
        }
        else if (a >= ramp0 && a < win0)
        {
            var t = (a - ramp0) / (win0 - ramp0);
            var hw = Mathf.Lerp(2.5f, 3.7f, t);
            if (ac <= hw)
            {
                var cut = Color.Lerp(WoodLight, WoodMid, t * 0.55f);
                return IconKit.Opaque(IconKit.Mul(cut, 1.02f - 0.3f * t * t - 0.08f * (-c / hw)));
            }
            if (ac <= hw + 0.8f)
            {
                return IconKit.Mul(col, 0.68f);
            }
        }
        return col;
    }

    // Wood of a round tube: s across (-1 lit edge), grain along the flute, oiled sheen.
    private static Color FluteWood(float a, float c, float w, float shade)
    {
        var s = Mathf.Clamp(-c / w, -1f, 1f);
        var tone = IconKit.Noise2(a * 0.03f, c * 0.45f + 7.3f);
        var streak = IconKit.SmoothStep(0.58f, 0.8f, IconKit.Noise(a * 0.05f + 3.1f, c * 0.65f + 1.7f));
        var col = Color.Lerp(WoodLight, WoodMid, tone * 0.75f);
        col = Color.Lerp(col, WoodGrain, streak * 0.6f);
        col = IconKit.Mul(col, IconKit.Tube(s) * shade);
        var k = (s + 0.55f) / 0.2f;
        col = Color.Lerp(col, WoodSheen, Mathf.Exp(-k * k) * 0.35f);
        return IconKit.Opaque(col);
    }

    // Dark raised ring from a0 to a1 (pixel from foot), half width w, outlined.
    private static Color FluteRing(Color col, float a, float c, float a0, float a1, float w)
    {
        var ac = Mathf.Abs(c);
        if (a < a0 - O || a > a1 + O || ac > w + O)
        {
            return col;
        }
        if (a < a0 || a > a1 || ac > w)
        {
            return Outline;
        }
        var s = Mathf.Clamp(-c / w, -1f, 1f);
        var ring = Color.Lerp(RingWood, RingWoodLight, IconKit.Noise(a * 0.6f, c * 0.3f) * 0.6f);
        ring = IconKit.Mul(ring, IconKit.Tube(s));
        var k = (s + 0.5f) / 0.18f;
        ring = Color.Lerp(ring, WoodSheen, Mathf.Exp(-k * k) * 0.3f);
        return IconKit.Opaque(ring);
    }

    // ======================================================================================================
    // Silver Lyre: upright Germanic round lyre, top leaning right. Silver frame (bevelled): round soundbox below,
    // two arms with an open window and an engraved groove, yoke on top with six dark pegs, two garnet studs at the
    // shoulders. Soundboard of darker blued silver with engraved border and vines, bridge and tailpiece. Six pale
    // linen strings from tailpiece to yoke.
    // ======================================================================================================

    private static readonly Vector2 LyreCenter = new Vector2(64f, 63f);
    // Lyre space -> picture: turn by -12 degrees (top lean right).
    private static readonly IconKit.Turn LyreTurn = new IconKit.Turn(-12f);
    // Light way in lyre space (shadows on the soundboard, groove lip).
    private static readonly Vector2 LyreLightLocal = LyreTurn.Undo(IconKit.Light2D);
    private static readonly IconKit.Field LyreFrameField = LyreFrame;

    private const int StringCount = 6;
    private const float StringTailV = -43f;
    private const float StringYokeV = 45.5f;
    // Space between strings at the tailpiece and at the yoke.
    private const float StringSpreadTail = 3.4f;
    private const float StringSpreadYoke = 6.0f;
    private const float PegRadius = 2.1f;
    private const float BevelWidth = 3.2f;

    private static readonly Color SilverDark = new Color(0.20f, 0.22f, 0.27f, 1f);
    private static readonly Color SilverMid = new Color(0.62f, 0.66f, 0.73f, 1f);
    private static readonly Color SilverLight = new Color(0.97f, 0.98f, 1f, 1f);
    private static readonly Color BoardDark = new Color(0.17f, 0.19f, 0.24f, 1f);
    private static readonly Color BoardLight = new Color(0.40f, 0.44f, 0.52f, 1f);
    private static readonly Color Linen = new Color(0.97f, 0.92f, 0.78f, 1f);
    private static readonly Color LinenShade = new Color(0.74f, 0.66f, 0.50f, 1f);
    private static readonly Color PegWood = new Color(0.40f, 0.24f, 0.12f, 1f);
    private static readonly Color PegLight = new Color(0.82f, 0.60f, 0.36f, 1f);
    private static readonly Color BridgeWood = new Color(0.62f, 0.38f, 0.17f, 1f);
    private static readonly Color GarnetDark = new Color(0.35f, 0.03f, 0.05f, 1f);
    private static readonly Color GarnetLight = new Color(0.95f, 0.30f, 0.25f, 1f);

    private static float StringU(int i, float v)
    {
        var t = (v - StringTailV) / (StringYokeV - StringTailV);
        return (i - (StringCount - 1) * 0.5f) * Mathf.Lerp(StringSpreadTail, StringSpreadYoke, t);
    }

    // Shapes in lyre space (u right, v up, centre 0). Body = round soundbox + narrower arms + wider yoke, filleted.
    private static float LyreBody(Vector2 q)
    {
        var box = IconKit.RoundBox(q - new Vector2(0f, -25f), new Vector2(27f, 27f), 12f, 25f);
        var arms = IconKit.Box(q - new Vector2(0f, 23f), new Vector2(23.5f, 23f));
        var yoke = IconKit.RoundBox(q - new Vector2(0f, 46f), new Vector2(27f, 6.5f), 5f, 3f);
        return IconKit.SmoothMin(IconKit.SmoothMin(box, arms, 5f), yoke, 3f);
    }

    private static float LyreWindow(Vector2 q) =>
        IconKit.RoundBox(q - new Vector2(0f, 21.5f), new Vector2(16.5f, 17.5f), 3f, 7f);

    private static float LyreBoard(Vector2 q) =>
        IconKit.RoundBox(q - new Vector2(0f, -25.75f), new Vector2(21.5f, 21.75f), 6f, 19f);

    // Metal of the frame: body minus window minus soundboard.
    private static float LyreFrame(Vector2 q) => Mathf.Max(LyreBody(q), Mathf.Max(-LyreWindow(q), -LyreBoard(q)));

    private static Color Lyre(Vector2 p)
    {
        var q = LyreTurn.Undo(p - LyreCenter);
        if (Mathf.Abs(q.x) > 27f + O + 1f || Mathf.Abs(q.y) > 52.5f + O + 1f)
        {
            return Clear;
        }
        var body = LyreBody(q);
        if (body > O)
        {
            return Clear;
        }
        var board = LyreBoard(q);
        var frame = Mathf.Max(body, Mathf.Max(-LyreWindow(q), -board));
        var col = Clear;
        if (board <= 0f)
        {
            col = LyreBoardShade(q, board);
        }
        if (frame <= 0f)
        {
            col = LyreFrameShade(q, frame);
        }
        else if (frame <= O)
        {
            col = Outline;
        }

        // Garnet studs where the arms meet the soundbox.
        if (Mathf.Abs(q.y) < 4f)
        {
            col = Garnet(col, q, new Vector2(-20.5f, 0f));
            col = Garnet(col, q, new Vector2(20.5f, 0f));
        }

        // Tailpiece and bridge (dark wood) on the soundboard.
        if (q.y < -26f)
        {
            col = WoodPiece(col, q, IconKit.RoundBox(q - new Vector2(0f, -43.5f), new Vector2(10.5f, 2.6f), 2.4f, 2.4f), -43.5f, 2.6f);
            col = WoodPiece(col, q, IconKit.RoundBox(q - new Vector2(0f, -30f), new Vector2(12.5f, 1.9f), 1.8f, 1.8f), -30f, 1.9f);
        }

        if (Mathf.Abs(q.x) < 17.5f && q.y > StringTailV - 1f && q.y < StringYokeV + 1f)
        {
            // Strings: shadow on the metal first (not in the open window), then the strings.
            if (col.a > 0f && board <= 0.5f && NearestString(q + LyreLightLocal * 1.6f, out _) <= 0.75f)
            {
                col = IconKit.Mul(col, 0.55f);
            }
            if (NearestString(q, out var side) <= 0.7f)
            {
                // Round thread: lit left half.
                col = IconKit.Opaque(Color.Lerp(Linen, LinenShade, IconKit.SmoothStep(-0.4f, 1f, side / 0.7f)));
            }
        }

        // Pegs on the yoke.
        if (q.y > StringYokeV - PegRadius - 1.5f)
        {
            for (var i = 0; i < StringCount; i++)
            {
                var center = new Vector2(StringU(i, StringYokeV), StringYokeV);
                var d = IconKit.Circle(q, center, PegRadius);
                if (d <= 0f)
                {
                    var lit = IconKit.Dome(LyreTurn.Apply((q - center) / PegRadius), 0.9f, out var spec);
                    var peg = Color.Lerp(PegWood, PegLight, lit * 0.8f);
                    col = IconKit.Opaque(Color.Lerp(peg, Color.white, spec * 0.5f));
                }
                else if (d <= 1.1f)
                {
                    col = Outline;
                }
            }
        }
        return col;
    }

    // Me give distance to nearest string. Strings fan out even, so me check only the two around q. side = how far q
    // sit right of that string (negative = left).
    private static float NearestString(Vector2 q, out float side)
    {
        var t = Mathf.Clamp01((q.y - StringTailV) / (StringYokeV - StringTailV));
        var f = q.x / Mathf.Lerp(StringSpreadTail, StringSpreadYoke, t) + (StringCount - 1) * 0.5f;
        var i0 = Mathf.Clamp((int)Mathf.Floor(f), 0, StringCount - 1);
        var i1 = Mathf.Min(i0 + 1, StringCount - 1);
        var d0 = IconKit.Segment(q, new Vector2(StringU(i0, StringTailV), StringTailV), new Vector2(StringU(i0, StringYokeV), StringYokeV));
        var d1 = IconKit.Segment(q, new Vector2(StringU(i1, StringTailV), StringTailV), new Vector2(StringU(i1, StringYokeV), StringYokeV));
        var i = d0 <= d1 ? i0 : i1;
        side = q.x - StringU(i, q.y);
        return Mathf.Min(d0, d1);
    }

    // Polished silver: rounded edges from the frame's slope, broad light toward upper left, soft reflection bands,
    // engraved groove down each arm.
    private static Color LyreFrameShade(Vector2 q, float frame)
    {
        var depth = -frame;
        var spec = 0f;
        var diffuse = IconKit.Light.z;
        if (depth < BevelWidth)
        {
            var outward = LyreTurn.Apply(IconKit.Gradient(LyreFrameField, q, frame, 0.3f));
            diffuse = IconKit.Bevel(outward, 1f - IconKit.SmoothStep(0f, BevelWidth, depth), 1.05f, out spec);
        }
        var world = LyreTurn.Apply(q);
        var broad = (world.x * IconKit.Light2D.x + world.y * IconKit.Light2D.y) / 60f;
        var band = 0.1f * Mathf.Sin((world.x + world.y) * 0.7071f * 0.11f + 0.6f);
        var col = IconKit.Ramp(SilverDark, SilverMid, SilverLight, diffuse * 0.95f + broad * 0.3f + band);
        col = Color.Lerp(col, Color.white, spec * 0.85f);
        if (q.y > 5f && q.y < 36f && depth > 1.5f)
        {
            // Groove: line at u = +-20. Wall away from the light (far lip) catch it.
            var off = q.x - (q.x > 0f ? 20f : -20f);
            var groove = Mathf.Abs(off);
            if (groove < 0.45f)
            {
                col = IconKit.Mul(col, 0.55f);
            }
            else if (groove < 1f && off * LyreLightLocal.x < 0f)
            {
                col = Color.Lerp(col, SilverLight, 0.5f);
            }
        }
        return IconKit.Opaque(col);
    }

    // Soundboard: darker blued silver, sunk under the frame (frame shade its upper left edge), engraved border and a
    // wavy engraved vine down each side of the strings.
    private static Color LyreBoardShade(Vector2 q, float board)
    {
        var world = LyreTurn.Apply(q);
        var broad = (world.x * IconKit.Light2D.x + world.y * IconKit.Light2D.y) / 50f;
        var brush = IconKit.Noise(q.x * 1.3f, q.y * 0.07f);
        var col = Color.Lerp(BoardDark, BoardLight, 0.5f + broad * 0.4f + (brush - 0.5f) * 0.25f);
        var vine = Mathf.Abs(Mathf.Abs(q.x) - (16f + 1.6f * Mathf.Sin(q.y * 0.42f + (q.x > 0f ? 0f : 3.14f))));
        if (Mathf.Abs(-board - 2.4f) < 0.4f || (vine < 0.45f && q.y > -40f && q.y < -9f))
        {
            col = IconKit.Mul(col, 0.6f);
        }
        if (LyreBoard(q + LyreLightLocal * 2.4f) > 0f)
        {
            col = IconKit.Mul(col, 0.6f);
        }
        return IconKit.Opaque(col);
    }

    // Small round red stone set in the frame.
    private static Color Garnet(Color col, Vector2 q, Vector2 center)
    {
        const float r = 2.3f;
        var d = IconKit.Circle(q, center, r);
        if (d > 1f)
        {
            return col;
        }
        if (d > 0f)
        {
            return Outline;
        }
        var lit = IconKit.Dome(LyreTurn.Apply((q - center) / r), 1f, out var spec);
        var stone = Color.Lerp(GarnetDark, GarnetLight, lit);
        return IconKit.Opaque(Color.Lerp(stone, Color.white, spec * 0.8f));
    }

    // Small dark wood bar (bridge, tailpiece): d = its distance, centre v and half height for the light.
    private static Color WoodPiece(Color col, Vector2 q, float d, float v, float half)
    {
        if (d > 1.1f)
        {
            return col;
        }
        if (d > 0f)
        {
            return Outline;
        }
        var t = Mathf.Clamp((q.y - v) / half, -1f, 1f);
        var wood = IconKit.Mul(BridgeWood, 0.75f + 0.4f * t + 0.1f * IconKit.Noise(q.x * 0.5f, q.y));
        return IconKit.Opaque(wood);
    }

    // ======================================================================================================
    // Tambourine: frame drum seen from above at an angle, tilted. Fine wood hoop (top rim + front side), tan leather
    // skin with soft highlight and a painted ring, five pairs of bronze jingles in slots on the front side.
    // ======================================================================================================

    private static readonly Vector2 TambCenter = new Vector2(64f, 63f);
    // Drum space -> picture: turn by 12 degrees.
    private static readonly IconKit.Turn TambTurn = new IconKit.Turn(12f);
    private const float TambRx = 50f;
    private const float TambRy = 29f;
    // Half the hoop depth as seen (top rim centre at +, bottom at -).
    private const float TambHalfDepth = 7.5f;
    private const float RimWidth = 5.5f;
    // Jingle disc (front slot size; side slots squash it like the slot) and its outline, the two discs of a pair sit
    // JingleOffset above and below the slot middle. Smaller than slot: dark slot rim show round each pair.
    private const float JingleRx = 5.2f;
    private const float JingleRy = 2.8f;
    private const float JingleOffset = 1.6f;
    private const float JingleOutline = 1.2f;
    private const float SlotHalfWidth = 8.2f;
    private const float SlotHalfHeight = 5.6f;

    // Slot centres on the front of the hoop (angle round the drum, -90 = nearest the viewer) and how much each is
    // squashed sideways (seen slanted: 1 = front).
    private static readonly float[] JingleAngles = { -146f, -118f, -90f, -62f, -34f };
    private static readonly float[] JingleU = MakeJingles(0);
    private static readonly float[] JingleV = MakeJingles(1);
    private static readonly float[] JingleSquash = MakeJingles(2);

    private static readonly Color SkinLight = new Color(0.96f, 0.85f, 0.64f, 1f);
    private static readonly Color SkinMid = new Color(0.80f, 0.63f, 0.41f, 1f);
    private static readonly Color SkinDark = new Color(0.55f, 0.38f, 0.22f, 1f);
    private static readonly Color SkinPaint = new Color(0.45f, 0.17f, 0.09f, 1f);
    private static readonly Color SlotDark = new Color(0.08f, 0.05f, 0.03f, 1f);
    private static readonly Color Pin = new Color(0.55f, 0.50f, 0.45f, 1f);
    private static readonly Color BronzeDark = new Color(0.42f, 0.24f, 0.08f, 1f);
    private static readonly Color BronzeMid = new Color(0.84f, 0.58f, 0.26f, 1f);
    private static readonly Color BronzeLight = new Color(1f, 0.90f, 0.62f, 1f);

    // what 0 = u (across), 1 = v (up), 2 = sideways squash.
    private static float[] MakeJingles(int what)
    {
        var list = new float[JingleAngles.Length];
        for (var i = 0; i < JingleAngles.Length; i++)
        {
            var a = JingleAngles[i] * Mathf.Deg2Rad;
            var sin = Mathf.Abs(Mathf.Sin(a));
            list[i] = what == 0 ? TambRx * Mathf.Cos(a) : what == 1 ? -TambRy * sin : Mathf.Pow(sin, 0.7f);
        }
        return list;
    }

    // Middle of the hoop front side at u (drum space): side band follow the drum's lower curve.
    private static float BandMiddle(float u)
    {
        var s = u / TambRx;
        return -TambRy * Mathf.Sqrt(Mathf.Max(0f, 1f - s * s));
    }

    private static Color Tambourine(Vector2 p)
    {
        var q = TambTurn.Undo(p - TambCenter);
        if (Mathf.Abs(q.x) > TambRx + O + 2f || Mathf.Abs(q.y) > TambRy + TambHalfDepth + O + 2f)
        {
            return Clear;
        }
        var topQ = q - new Vector2(0f, TambHalfDepth);
        var top = IconKit.Ellipse(topQ, TambRx, TambRy);
        var bottom = IconKit.Ellipse(q + new Vector2(0f, TambHalfDepth), TambRx, TambRy);
        var side = IconKit.Box(q, new Vector2(TambRx, TambHalfDepth));
        var silhouette = Mathf.Min(top, Mathf.Min(bottom, side));
        var col = Clear;
        if (silhouette <= 0f)
        {
            if (top <= 0f)
            {
                var skin = IconKit.Ellipse(topQ, TambRx - RimWidth, TambRy - RimWidth * TambRy / TambRx);
                if (skin <= 0f)
                {
                    col = SkinShade(topQ, skin);
                }
                else if (skin <= 0.9f)
                {
                    col = IconKit.Opaque(IconKit.Mul(WoodGrain, 0.55f));
                }
                else
                {
                    col = HoopTop(topQ);
                }
            }
            else
            {
                col = top <= 1.1f ? IconKit.Opaque(IconKit.Mul(WoodGrain, 0.4f)) : HoopSide(q);
            }
        }
        else if (silhouette <= O)
        {
            col = Outline;
        }
        if (q.y < 0f)
        {
            for (var i = 0; i < JingleU.Length; i++)
            {
                col = Jingle(col, q, i);
            }
        }
        return col;
    }

    private static Color SkinShade(Vector2 c, float skin)
    {
        var h = c - new Vector2(-13f, 8f);
        var light = Mathf.Exp(-(h.x * h.x / (2f * 21f * 21f) + h.y * h.y / (2f * 12f * 12f)));
        var col = IconKit.Ramp(SkinDark, SkinMid, SkinLight, 0.42f + 0.55f * light - 0.12f * (c.x / TambRx));
        col = IconKit.Mul(col, 0.94f + 0.12f * IconKit.Noise2(c.x * 0.22f, c.y * 0.35f));
        // Skin darker where it bend over the hoop.
        col = IconKit.Mul(col, Mathf.Lerp(0.78f, 1f, IconKit.SmoothStep(0f, 5f, -skin)));
        // Painted ring.
        if (Mathf.Abs(-skin - 4.8f) < 0.9f)
        {
            col = Color.Lerp(col, SkinPaint, 0.6f);
        }
        return IconKit.Opaque(col);
    }

    // Top of the hoop: pale wood, grain round the ring, lit.
    private static Color HoopTop(Vector2 c)
    {
        var angle = Mathf.Atan2(c.y / TambRy, c.x / TambRx);
        var tone = IconKit.Noise2(angle * 9f, (c.x * c.x / (TambRx * TambRx) + c.y * c.y / (TambRy * TambRy)) * 14f);
        var col = Color.Lerp(WoodLight, WoodMid, tone * 0.7f);
        col = IconKit.Mul(col, 1.02f - 0.12f * (c.x / TambRx) + 0.04f * (c.y / TambRy));
        return IconKit.Opaque(col);
    }

    // Front side of the hoop: wood band, lit on the left, darker down and right, grain along it.
    private static Color HoopSide(Vector2 q)
    {
        var s = q.x / TambRx;
        var curve = TambHalfDepth - TambRy * Mathf.Sqrt(Mathf.Max(0f, 1f - s * s));
        var down = Mathf.Clamp01((curve - q.y) / (2f * TambHalfDepth));
        var tone = IconKit.Noise2(q.x * 0.05f + 2f, q.y * 0.8f);
        var streak = IconKit.SmoothStep(0.6f, 0.82f, IconKit.Noise(q.x * 0.04f, q.y * 1.1f + 4f));
        var col = Color.Lerp(WoodLight, WoodMid, 0.35f + tone * 0.6f);
        col = Color.Lerp(col, WoodGrain, streak * 0.45f);
        var lit = 0.98f - 0.42f * IconKit.SmoothStep(-0.7f, 1f, s) - 0.22f * down;
        return IconKit.Opaque(IconKit.Mul(col, lit));
    }

    // One jingle pair in its slot. q = point in drum space.
    private static Color Jingle(Color col, Vector2 q, int index)
    {
        var d = q - new Vector2(JingleU[index], JingleV[index]);
        if (Mathf.Abs(d.x) > SlotHalfWidth + 0.5f)
        {
            return col;
        }
        // Slot sides stand straight, top and bottom bend with the hoop (height from band middle at this u), so slot
        // stay on the side band also at drum sides.
        var ds = new Vector2(d.x, q.y - BandMiddle(q.x));
        if (Mathf.Abs(ds.y) > SlotHalfHeight + 0.5f && Mathf.Abs(d.y) > JingleOffset + JingleRy + JingleOutline)
        {
            return col;
        }
        // Slot narrower toward the drum sides (seen slanted). Pin across it, two small cymbals on the pin.
        var squash = JingleSquash[index];
        var slot = IconKit.RoundBox(ds, new Vector2(SlotHalfWidth * squash, SlotHalfHeight), 1.2f, 1.2f);
        if (slot <= 0f)
        {
            col = Mathf.Abs(d.x) < 0.6f
                ? Pin
                : IconKit.Opaque(Color.Lerp(SlotDark, IconKit.Mul(WoodGrain, 0.45f),
                    IconKit.SmoothStep(SlotHalfHeight, -SlotHalfHeight, ds.y) * 0.6f));
        }
        col = JingleDisc(col, d - new Vector2(0.6f * squash, -JingleOffset), squash, 0.8f);
        col = JingleDisc(col, d - new Vector2(-0.2f * squash, JingleOffset), squash, 1f);
        return col;
    }

    // Small bronze cymbal seen from above (lies like the skin): domed, centre hole, outlined. squash = slot squash.
    private static Color JingleDisc(Color col, Vector2 d, float squash, float shade)
    {
        var rx = JingleRx * squash;
        var e = IconKit.Ellipse(d, rx, JingleRy);
        if (e > JingleOutline)
        {
            return col;
        }
        if (e > 0f)
        {
            return Outline;
        }
        var hx = 1.3f * squash;
        if (d.x * d.x / (hx * hx) + d.y * d.y / (0.8f * 0.8f) <= 1f)
        {
            return Outline;
        }
        var lit = IconKit.Dome(TambTurn.Apply(new Vector2(d.x / rx, d.y / JingleRy)), 0.75f, out var spec);
        var bronze = IconKit.Ramp(BronzeDark, BronzeMid, BronzeLight, lit * 0.95f * shade);
        return IconKit.Opaque(Color.Lerp(bronze, Color.white, spec * 0.7f));
    }

    // ======================================================================================================
    // Music effect: two beamed eighth notes in warm gold, bevelled, dark outline, soft warm glow behind. Glyph drawn
    // a bit smaller than designed (NotesScale) so the glow fits.
    // ======================================================================================================

    private static readonly IconKit.Field NotesField = NotesShape;
    private const float NotesOutline = 2.2f;
    private const float NotesScale = 0.9f;
    private const float NotesBevel = 3.8f;
    // Glow: alpha next to outline, how fast it die (pixel), small cut so it reach 0 smooth (no step). Valheim draw
    // UI in linear space: thin alpha there look much brighter and wider than in PNG viewer, so me keep it weak.
    private const float GlowStrength = 0.45f;
    private const float GlowFalloff = 3.2f;
    private const float GlowCut = 0.008f;
    // Glow fade to 0 near texture border: outer pixel ring empty, full glow from GlowEdgeFull pixel in.
    private const float GlowEdgeZero = 1f;
    private const float GlowEdgeFull = 3f;
    private static readonly IconKit.Turn HeadTurn = new IconKit.Turn(25f);

    private static readonly Vector2 NoteHead1 = new Vector2(40f, 28f);
    private static readonly Vector2 NoteHead2 = new Vector2(88f, 40f);
    private const float NoteHeadRx = 14.5f;
    private const float NoteHeadRy = 10.5f;
    private const float StemHalf = 3.4f;
    private const float StemOffset = 10.6f;
    private const float BeamThick = 14f;
    private const float BeamTopLeft = 97f;

    // Stems and beam corners, made once from the numbers above.
    private static readonly Vector2 Stem1Center;
    private static readonly Vector2 Stem1Half;
    private static readonly Vector2 Stem2Center;
    private static readonly Vector2 Stem2Half;
    private static readonly Vector2 BeamA;
    private static readonly Vector2 BeamB;
    private static readonly Vector2 BeamC;
    private static readonly Vector2 BeamD;

    private static readonly Color GoldDark = new Color(0.55f, 0.31f, 0.06f, 1f);
    private static readonly Color Gold = new Color(0.98f, 0.74f, 0.26f, 1f);
    private static readonly Color GoldLight = new Color(1f, 0.96f, 0.74f, 1f);
    private static readonly Color GoldOutline = new Color(0.24f, 0.12f, 0.03f, 1f);
    private static readonly Color Glow = new Color(1f, 0.64f, 0.18f, 1f);

    static InstrumentIcons()
    {
        var slope = (NoteHead2.y - NoteHead1.y) / (NoteHead2.x - NoteHead1.x);
        var x1 = NoteHead1.x + StemOffset;
        var x2 = NoteHead2.x + StemOffset;
        var left = x1 - StemHalf;
        var right = x2 + StemHalf;
        var topRight = BeamTopLeft + (right - left) * slope;
        // Stem end in middle of sloped beam (beam top at stem middle minus half beam), so no stem corner poke out
        // above beam.
        var top1 = BeamTopLeft + (x1 - left) * slope - BeamThick * 0.5f;
        var top2 = BeamTopLeft + (x2 - left) * slope - BeamThick * 0.5f;
        Stem1Center = new Vector2(x1, (NoteHead1.y + top1) * 0.5f);
        Stem1Half = new Vector2(StemHalf, (top1 - NoteHead1.y) * 0.5f);
        Stem2Center = new Vector2(x2, (NoteHead2.y + top2) * 0.5f);
        Stem2Half = new Vector2(StemHalf, (top2 - NoteHead2.y) * 0.5f);
        BeamA = new Vector2(left, BeamTopLeft - BeamThick);
        BeamB = new Vector2(right, topRight - BeamThick);
        BeamC = new Vector2(right, topRight);
        BeamD = new Vector2(left, BeamTopLeft);
    }

    private static float NotesShape(Vector2 q)
    {
        var h1 = IconKit.Ellipse(HeadTurn.Undo(q - NoteHead1), NoteHeadRx, NoteHeadRy);
        var h2 = IconKit.Ellipse(HeadTurn.Undo(q - NoteHead2), NoteHeadRx, NoteHeadRy);
        var s1 = IconKit.Box(q - Stem1Center, Stem1Half);
        var s2 = IconKit.Box(q - Stem2Center, Stem2Half);
        var beam = IconKit.Quad(q, BeamA, BeamB, BeamC, BeamD);
        return Mathf.Min(Mathf.Min(h1, h2), Mathf.Min(beam, Mathf.Min(s1, s2)));
    }

    private static Color MusicNotes(Vector2 p)
    {
        // Icon centre to glyph centre (design), scaled.
        var q = (p - Mid) / NotesScale + new Vector2(64f, 66f);
        var raw = NotesShape(q);
        var d = raw * NotesScale;
        if (d > NotesOutline)
        {
            var g = Mathf.Exp(-(d - NotesOutline) / GlowFalloff) * GlowStrength - GlowCut;
            if (g <= 0f)
            {
                return Clear;
            }
            g *= IconKit.SmoothStep(GlowEdgeZero, GlowEdgeFull, IconKit.BorderDistance(p));
            return g > 0f ? IconKit.WithAlpha(Glow, g) : Clear;
        }
        if (d > 0f)
        {
            return GoldOutline;
        }
        var spec = 0f;
        var diffuse = IconKit.Light.z;
        if (-d < NotesBevel)
        {
            var outward = IconKit.Gradient(NotesField, q, raw, 0.3f);
            diffuse = IconKit.Bevel(outward, 1f - IconKit.SmoothStep(0f, NotesBevel, -d), 1.1f, out spec);
        }
        var col = IconKit.Ramp(GoldDark, Gold, GoldLight, diffuse * 0.85f + 0.12f + (p.y - 64f) / 128f * 0.25f);
        col = Color.Lerp(col, GoldLight, spec * 0.7f);
        return IconKit.Opaque(col);
    }

#if DEBUG || OFFLINE_PREVIEW
    // ======================================================================================================
    // Package icon: the three instruments together on the dark plate (lyre behind, tambourine leaning right, flute in
    // front), two small gold notes floating above. Soft shadows between them. Only for export (debug self test,
    // offline preview): release build no carry it.
    // ======================================================================================================

    // One instrument placed on the package icon: drawn at Pos, turned, scaled; Shadow = soft shadow on what is under.
    private readonly struct Placement
    {
        internal readonly IconKit.Sampler Sampler;
        internal readonly Vector2 Pos;
        internal readonly IconKit.Turn Turn;
        internal readonly float Scale;
        internal readonly bool Shadow;

        internal Placement(IconKit.Sampler sampler, Vector2 pos, float deg, float scale, bool shadow)
        {
            Sampler = sampler;
            Pos = pos;
            Turn = new IconKit.Turn(deg);
            Scale = scale;
            Shadow = shadow;
        }

        // Point of the package picture -> point of the instrument's own picture.
        internal Vector2 Map(Vector2 p) => Turn.Undo(p - Pos) / Scale + Mid;
    }

    // Back to front.
    private static readonly Placement[] PackageLayout =
    {
        new Placement(LyreSampler, new Vector2(44f, 70f), 4f, 0.78f, true),
        new Placement(TambourineSampler, new Vector2(90f, 70f), -16f, 0.58f, true),
        new Placement(FluteSampler, new Vector2(64f, 28f), -26f, 0.8f, true),
        new Placement(NotesSampler, new Vector2(106f, 108f), 4f, 0.28f, false),
        new Placement(NotesSampler, new Vector2(18f, 102f), -12f, 0.2f, false),
    };

    private static readonly Vector2 ShadowOffset = new Vector2(2.2f, -2.6f);
    private static readonly Vector2[] ShadowTaps =
    {
        new Vector2(-0.9f, -0.9f), new Vector2(0.9f, -0.9f), new Vector2(-0.9f, 0.9f), new Vector2(0.9f, 0.9f),
    };

    private static Color Package(Vector2 p)
    {
        var col = Plate(p);
        for (var i = 0; i < PackageLayout.Length; i++)
        {
            var place = PackageLayout[i];
            if (place.Shadow)
            {
                var sh = 0f;
                for (var t = 0; t < ShadowTaps.Length; t++)
                {
                    sh += Mathf.Min(1f, place.Sampler(place.Map(p - ShadowOffset + ShadowTaps[t])).a);
                }
                sh /= ShadowTaps.Length;
                if (sh > 0f)
                {
                    col = IconKit.Mul(col, 1f - 0.5f * sh);
                }
            }
            col = IconKit.Over(col, place.Sampler(place.Map(p)));
        }
        return col;
    }

    // Dark slate plate (like Spyglass's), soft lighter centre, faint warm light behind the lyre.
    private static Color Plate(Vector2 p)
    {
        var d = (p - Mid).magnitude / 64f;
        var col = Color.Lerp(new Color(0.20f, 0.24f, 0.28f, 1f), new Color(0.08f, 0.09f, 0.11f, 1f), Mathf.Clamp01(d));
        var w = p - new Vector2(60f, 76f);
        var warm = Mathf.Exp(-(w.x * w.x + w.y * w.y) / (2f * 30f * 30f)) * 0.12f;
        return IconKit.Opaque(Color.Lerp(col, Glow, warm));
    }
#endif
}
