using System;
using System.Collections.Generic;
using UnityEngine;
#if !OFFLINE_PREVIEW
using MC.Shared;
#endif

namespace MC.Exploration.MusicInstrumentsMod;

// Me = the three instrument 3D models, made in code once (no asset bundle), like the spyglass: one mesh per
// instrument with one submesh per material, mesh and materials kept whole session and shared by every copy.
// Materials = copies of the vanilla knife material with small textures drawn here (their detail textures taken off).
// Model space, every instrument: unit = metre at scale 1, origin = where the right hand grip it, +Z = long axis going
// away from the hand, Length = reach along +Z from origin to far end.
// - Flute: end-blown wooden flute (recorder like), 36 cm, beak mouthpiece at +Z, six finger holes on +Y, thumb hole on
//   -Y, three dark bands, flared foot sticking 7 cm behind the grip.
// - Lyre: Germanic round lyre of silver, 55 cm tall, 3 cm thick, soundbox 22 cm wide going thin up the two arms,
//   yoke 23 cm wide on top; big open window from 45 % to 85 % of the height, dark hammered soundboard, strings on +Y,
//   origin = bottom centre.
// - Tambourine: frame drum, 26 cm hoop of fine wood, tan skin on +Y, five jingle slots with bronze pairs; origin on
//   the rim, disc centre at (0, 0, 0.13), disc in the X-Z plane.
// Markers = empty children (names below) the pose code aim hands and mouth at. Dedicated server: object, markers and
// collider only, no mesh, texture or material.
// Geometry and texture pixels = pure maths (MeshKit, TexNoise): preview tool build them outside game with
// OFFLINE_PREVIEW (Unity object parts left out).
internal static class InstrumentModels
{
    internal const string ModelName = "MC_InstrumentModel";
    internal const string MouthMarker = "MC_Mouth";
    internal const string LeftHandMarker = "MC_LeftHand";
    internal const string StrumMarker = "MC_Strum";
    internal const string BodyMarker = "MC_Body";
    internal const string ColliderName = "MC_InstrumentCollider";

    // Flint knife "attach" look length along +Z. Me measure it in game 2026-10-05 (music.item build report): 0.379.
    // Knife this long = model at real size (scale 1).
    internal const float AssumedKnifeLength = 0.379f;

    // Material slots (one palette for all three; each mesh use some of them).
    internal const int SlotWood = 0;
    internal const int SlotDarkWood = 1;
    internal const int SlotShadow = 2;
    internal const int SlotSilver = 3;
    internal const int SlotEngraved = 4;
    internal const int SlotLinen = 5;
    internal const int SlotLeather = 6;
    internal const int SlotBronze = 7;
    internal const int SlotHammered = 8;
    internal const int SlotCount = 9;

    internal static readonly string[] SlotNames =
        { "Wood", "DarkWood", "Shadow", "Silver", "SilverEngraved", "Linen", "Leather", "Bronze", "SilverHammered" };
    internal static readonly float[] SlotMetallic = { 0f, 0f, 0f, 0.85f, 0.85f, 0f, 0f, 0.75f, 0.8f };
    internal static readonly float[] SlotGloss = { 0.25f, 0.3f, 0f, 0.72f, 0.62f, 0.1f, 0.2f, 0.55f, 0.6f };

    // ---------------------------------------------------------------------------------------------------- sizes

    // Flute: foot end, start of the beak cut, beak tip (z), head radius.
    private const float FluteFoot = -0.07f;
    private const float FluteBeak = 0.262f;
    private const float FluteTip = 0.29f;
    private const float FluteHead = 0.013f;
    private const int FluteSegments = 24;

    // Lyre outline (half widths): soundbox full width up to LyreBellyZ, going thin to the arm waist at LyreWaistZ,
    // then out again to the yoke width at LyreFlareZ. Height, half thickness, corner radii, rounded edge, engraved band.
    private const float LyreHalfWidth = 0.11f;
    private const float LyreBellyZ = 0.09f;
    private const float LyreWaistHalf = 0.098f;
    private const float LyreWaistZ = 0.33f;
    private const float LyreYokeHalf = 0.116f;
    private const float LyreFlareZ = 0.505f;
    private const float LyreHeight = 0.55f;
    private const float LyreHalfThick = 0.015f;
    private const float LyreBottomRound = 0.075f;
    private const float LyreTopRound = 0.03f;
    private const float LyreEdge = 0.003f;
    private const float LyreRim = 0.012f;
    // Window (45 % to 85 % of the height), soundboard top (silver bar between it and the window) and sink depth.
    private const float LyreWinHalf = 0.07f;
    private const float LyreWinBottom = 0.2475f;
    private const float LyreWinTop = 0.4675f;
    private const float LyreWinRoundBottom = 0.02f;
    private const float LyreWinRoundTop = 0.03f;
    private const float LyrePanelTop = 0.2255f;
    private const float LyrePanelDepth = 0.0025f;
    // Strings: tailpiece knot, bridge, peg (z), bridge top and string height at the peg (y), half spread at each.
    private const int LyreStrings = 6;
    private const float LyreStringRadius = 0.0012f;
    private const float LyreTailZ = 0.032f;
    private const float LyreBridgeZ = 0.088f;
    private const float LyrePegZ = 0.503f;
    private const float LyreBridgeTop = 0.0272f;
    private const float LyrePegY = 0.0225f;
    private const float LyreTailSpread = 0.035f;
    private const float LyreBridgeSpread = 0.0475f;
    private const float LyrePegSpread = 0.055f;

    // Tambourine: hoop outer and inner radius, half depth, slot half height, pieces round, jingle slots.
    private const float TambOuter = 0.13f;
    private const float TambInner = 0.1215f;
    private const float TambHalfDepth = 0.025f;
    private const float TambSlotHalf = 0.015f;
    private const float TambSkin = 0.0232f;
    private const int TambSegments = 60;
    private const int TambSlots = 5;
    private const int TambSlotWidth = 4;
    private static readonly Vector3 TambCentre = new Vector3(0f, 0f, 0.13f);

    internal static float Length(InstrumentKind kind)
    {
        switch (kind)
        {
            case InstrumentKind.Flute:
                return FluteTip;
            case InstrumentKind.Lyre:
                return LyreHeight;
            case InstrumentKind.Tambourine:
                return TambCentre.z + TambOuter;
            default:
                return 0.3f;
        }
    }

    // Model length / knife length: with a knife of AssumedKnifeLength the model come out at real size.
    internal static float SizeVsKnife(InstrumentKind kind) => Length(kind) / AssumedKnifeLength;

    // ---------------------------------------------------------------------------------------------------- markers

    internal struct Spot
    {
        internal string Name;
        internal Vector3 Position;
        internal Vector3 Forward;
        internal Vector3 Up;

        internal Spot(string name, Vector3 position, Vector3 forward, Vector3 up)
        {
            Name = name;
            Position = position;
            Forward = forward;
            Up = up;
        }
    }

    private static readonly Spot[] NoSpots = new Spot[0];

    private static readonly Spot[] FluteSpots =
    {
        // Beak tip, looking down the tube to the foot, up = finger hole side.
        new Spot(MouthMarker, new Vector3(0f, 0.00585f, FluteTip), Vector3.back, Vector3.up),
        // On the tube top over the upper three holes (a third of the way from mouth to foot), where fingers lie.
        new Spot(LeftHandMarker, new Vector3(0f, FluteRadius(0.165f) + 0.0003f, 0.165f), Vector3.forward, Vector3.up),
    };

    private static readonly Spot[] LyreSpots =
    {
        // Top of the +X arm just below the yoke (middle of the arm, mid thickness). Strings forward and top up: model
        // +X = player's left.
        new Spot(LeftHandMarker, new Vector3((LyreWinHalf + LyreSideX(LyreWinTop - 0.013f)) * 0.5f, 0f, LyreWinTop - 0.013f),
            Vector3.forward, Vector3.up),
        // Middle of the strings (half way from tailpiece to pegs, just over the window bottom), at string height.
        new Spot(StrumMarker, new Vector3(0f, LyreStringY((LyreTailZ + LyrePegZ) * 0.5f), (LyreTailZ + LyrePegZ) * 0.5f),
            Vector3.forward, Vector3.up),
        // Back face centre of the soundbox (all below the window; rest on the chest).
        new Spot(BodyMarker, new Vector3(0f, -LyreHalfThick, LyreWinBottom * 0.5f), Vector3.forward, Vector3.up),
    };

    private static readonly Spot[] TambourineSpots =
    {
        new Spot(LeftHandMarker, new Vector3(0f, TambSkin, TambCentre.z), Vector3.forward, Vector3.up),
        new Spot(BodyMarker, TambCentre, Vector3.forward, Vector3.up),
    };

    internal static Spot[] Markers(InstrumentKind kind)
    {
        switch (kind)
        {
            case InstrumentKind.Flute:
                return FluteSpots;
            case InstrumentKind.Lyre:
                return LyreSpots;
            case InstrumentKind.Tambourine:
                return TambourineSpots;
            default:
                return NoSpots;
        }
    }

    // ---------------------------------------------------------------------------------------------------- geometry

    internal static MeshKit BuildGeometry(InstrumentKind kind)
    {
        var kit = new MeshKit(SlotCount);
        switch (kind)
        {
            case InstrumentKind.Flute:
                BuildFlute(kit);
                break;
            case InstrumentKind.Lyre:
                BuildLyre(kit);
                break;
            case InstrumentKind.Tambourine:
                BuildTambourine(kit);
                break;
        }
        return kit;
    }

    // Body radius at z: thin taper from the foot band (10.6 mm) to the head band (12.6 mm).
    private static float FluteRadius(float z) => Mathf.Lerp(0.0106f, 0.0126f, Mathf.Clamp01((z + 0.043f) / 0.269f));

    // Outer radius where holes and window sit.
    private static float FluteOuter(float z) => z >= 0.234f ? FluteHead : FluteRadius(z);

    // Lathe of the tube (foot, bands, body, head), lofted beak, end cap, then dark holes laid on the surface.
    private static void BuildFlute(MeshKit k)
    {
        var rail = MeshKit.Circle(Vector3.zero, Vector3.right, Vector3.up, 0f, Mathf.PI * 2f, FluteSegments, 1f);
        var axis = Vector3.forward;
        // v = height along the flute (0 foot .. 1 tip): the wood grain run along it.
        var uv = new MeshKit.UvMap { ByHeight = true, VPerMetre = 1f / (FluteTip - FluteFoot), V0 = -FluteFoot / (FluteTip - FluteFoot) };

        // Open foot end: dark bore, flat end ring with a rounded lip, flare narrowing into the body.
        k.Sweep(rail, axis, new MeshKit.Run(SlotShadow, 1f).To(0f, -0.046f).To(0.0062f, -0.046f), uv);
        k.Sweep(rail, axis, new MeshKit.Run(SlotShadow, 1f).To(0.0062f, -0.046f).To(0.0062f, FluteFoot), uv);
        var foot = new MeshKit.Run(SlotWood, 1f).To(0.0062f, FluteFoot).To(0.0122f, FluteFoot)
            .Arc(0.0122f, FluteFoot + 0.0015f, 0.0015f, -90f, 0f, 3);
        for (var i = 1; i <= 6; i++)
        {
            var h = FluteFoot + 0.0015f + (-0.049f - FluteFoot - 0.0015f) * i / 6f;
            var f = (-0.049f - h) / 0.0195f;
            foot.To(0.0108f + 0.0029f * f * f, h);
        }
        k.Sweep(rail, axis, foot, uv);

        FluteBand(k, rail, uv, -0.049f, -0.043f, 0.0108f, 0.0117f, FluteRadius(-0.043f));
        k.Sweep(rail, axis, new MeshKit.Run(SlotWood, 1f).To(FluteRadius(-0.043f), -0.043f)
            .To(FluteRadius(0.033f), 0.033f).To(FluteRadius(0.108f), 0.108f), uv);
        FluteBand(k, rail, uv, 0.108f, 0.115f, FluteRadius(0.108f), FluteRadius(0.108f) + 0.0009f, FluteRadius(0.115f));
        k.Sweep(rail, axis, new MeshKit.Run(SlotWood, 1f).To(FluteRadius(0.115f), 0.115f)
            .To(FluteRadius(0.17f), 0.17f).To(FluteRadius(0.226f), 0.226f), uv);
        FluteBand(k, rail, uv, 0.226f, 0.234f, FluteRadius(0.226f), 0.0139f, FluteHead);
        k.Sweep(rail, axis, new MeshKit.Run(SlotWood, 1f).To(FluteHead, 0.234f).To(FluteHead, FluteBeak), uv);

        FluteBeakLoft(k, uv);

        // Six finger holes on +Y (upper three for the left hand), thumb hole on -Y, window (labium) under the beak.
        float[] holes = { 0.185f, 0.162f, 0.139f, 0.095f, 0.072f, 0.049f };
        for (var i = 0; i < holes.Length; i++)
        {
            FluteHole(k, holes[i], 1f, i == holes.Length - 1 ? 0.0023f : 0.0027f);
        }
        FluteHole(k, 0.2f, -1f, 0.0026f);
        k.Fan(SlotShadow, new Vector2(0f, 0.2495f), RectRing(-0.0045f, 0.0045f, 0.2465f, 0.2525f, 6),
            p => FluteSurface(p.x, p.y, 1f), p => FluteSurfaceNormal(p.x, p.y, 1f), p => new Vector2(0.5f, 0.5f));
    }

    // Raised dark ring from z0 to z1: step up from rIn, band at rBand, step down to rOut. Hard edges.
    private static void FluteBand(MeshKit k, MeshKit.RailPoint[] rail, MeshKit.UvMap uv, float z0, float z1, float rIn,
        float rBand, float rOut)
    {
        k.Sweep(rail, Vector3.forward, new MeshKit.Run(SlotDarkWood, 1f).To(rIn, z0).To(rBand, z0), uv);
        k.Sweep(rail, Vector3.forward, new MeshKit.Run(SlotDarkWood, 1f).To(rBand, z0).To(rBand, z1), uv);
        k.Sweep(rail, Vector3.forward, new MeshKit.Run(SlotDarkWood, 1f).To(rBand, z1).To(rOut, z1), uv);
    }

    // Beak cross-section at t (0 = start of the cut, round; 1 = tip, flat oval lifted toward +Y): ellipse half
    // widths rx, ry round centre height cy. Underside cut rises fast near the tip, top dips a little.
    private static void BeakShape(float t, out float rx, out float cy, out float ry)
    {
        t = Mathf.Clamp01(t);
        var top = FluteHead - 0.0028f * t * t;
        var bottom = -FluteHead + (FluteHead + 0.0015f) * (float)Math.Pow(t, 1.7);
        rx = FluteHead - 0.0018f * t * t;
        cy = (top + bottom) * 0.5f;
        ry = (top - bottom) * 0.5f;
    }

    private static Vector3 BeakPoint(float t, float a)
    {
        BeakShape(t, out var rx, out var cy, out var ry);
        return new Vector3(Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry, FluteBeak + (FluteTip - FluteBeak) * Mathf.Clamp01(t));
    }

    private static void FluteBeakLoft(MeshKit k, MeshKit.UvMap uv)
    {
        const int rings = 8;
        const float da = 0.01f;
        const float dt = 0.01f;
        var cols = FluteSegments + 1;
        var start = k.Verts.Count;
        for (var r = 0; r <= rings; r++)
        {
            var t = r / (float)rings;
            BeakShape(t, out _, out var cy, out _);
            for (var s = 0; s <= FluteSegments; s++)
            {
                var a = (s % FluteSegments) * Mathf.PI * 2f / FluteSegments;
                var p = BeakPoint(t, a);
                var around = BeakPoint(t, a + da) - BeakPoint(t, a - da);
                var along = BeakPoint(Mathf.Min(1f, t + dt), a) - BeakPoint(Mathf.Max(0f, t - dt), a);
                var n = MeshKit.Unit(Vector3.Cross(MeshKit.Unit(around), MeshKit.Unit(along)));
                if (Vector3.Dot(n, new Vector3(p.x, p.y - cy, 0f)) < 0f)
                {
                    n = -n;
                }
                k.Add(p, n, new Vector2(s / (float)FluteSegments, uv.V0 + p.z * uv.VPerMetre));
            }
        }
        for (var r = 0; r < rings; r++)
        {
            for (var s = 0; s < FluteSegments; s++)
            {
                var a = start + r * cols + s;
                k.Quad(SlotWood, a, a + 1, a + cols + 1, a + cols);
            }
        }

        // Flat tip with the dark windway slit near its top edge.
        BeakShape(1f, out var tx, out var tcy, out var ty);
        var ring = new List<Vector2>();
        for (var s = 0; s < FluteSegments; s++)
        {
            var a = s * Mathf.PI * 2f / FluteSegments;
            ring.Add(new Vector2(Mathf.Cos(a) * tx, tcy + Mathf.Sin(a) * ty));
        }
        k.Fan(SlotWood, new Vector2(0f, tcy), ring, p => new Vector3(p.x, p.y, FluteTip), p => Vector3.forward,
            p => new Vector2(0.5f + p.x * 20f, 0.95f + p.y * 2f));
        k.Fan(SlotShadow, new Vector2(0f, 0.0077f), RectRing(-0.0058f, 0.0058f, 0.007f, 0.0084f, 1),
            p => new Vector3(p.x, p.y, FluteTip + 0.0002f), p => Vector3.forward, p => new Vector2(0.5f, 0.5f));
    }

    // Point on the flute surface (lifted a hair) above (x, z), on the +Y (side 1) or -Y (side -1) half.
    private static Vector3 FluteSurface(float x, float z, float side)
    {
        var r = FluteOuter(z) + 0.00025f;
        var y = Mathf.Sqrt(Mathf.Max(0f, r * r - x * x));
        return new Vector3(x, y * side, z);
    }

    private static Vector3 FluteSurfaceNormal(float x, float z, float side)
    {
        var p = FluteSurface(x, z, side);
        return MeshKit.Unit(new Vector3(p.x, p.y, 0f));
    }

    private static void FluteHole(MeshKit k, float z, float side, float radius)
    {
        var ring = new List<Vector2>();
        for (var i = 0; i < 10; i++)
        {
            var a = i * Mathf.PI * 2f / 10f;
            ring.Add(new Vector2(Mathf.Cos(a) * radius, z + Mathf.Sin(a) * radius));
        }
        k.Fan(SlotShadow, new Vector2(0f, z), ring, p => FluteSurface(p.x, p.y, side),
            p => FluteSurfaceNormal(p.x, p.y, side), p => new Vector2(0.5f, 0.5f));
    }

    // Rectangle outline going round, long edges cut in steps pieces (so a patch bent on a tube stays on it).
    private static List<Vector2> RectRing(float x0, float x1, float y0, float y1, int steps)
    {
        var ring = new List<Vector2>();
        for (var i = 0; i <= steps; i++)
        {
            ring.Add(new Vector2(x0 + (x1 - x0) * i / steps, y0));
        }
        for (var i = 0; i <= steps; i++)
        {
            ring.Add(new Vector2(x1 + (x0 - x1) * i / steps, y1));
        }
        return ring;
    }

    // Closed outline in the X-Z plane (x, z) with its outward normals.
    private sealed class Outline
    {
        internal readonly List<Vector2> P = new List<Vector2>();
        internal readonly List<Vector2> N = new List<Vector2>();
        internal int TopMid;
        internal int LeftStop = -1;
        internal int RightStop = -1;

        internal int Count => P.Count;

        // Point moved d along its normal (d < 0 = inward).
        internal Vector2 Out(int i, float d) => P[i] + N[i] * d;

        internal void Add(Vector2 p, Vector2 n)
        {
            if (P.Count > 0 && (P[P.Count - 1] - p).sqrMagnitude < 1e-14f)
            {
                return;
            }
            P.Add(p);
            N.Add(n);
        }

        internal void Arc(float cx, float cz, float r, float a0, float a1, int steps)
        {
            for (var i = 0; i <= steps; i++)
            {
                var a = (a0 + (a1 - a0) * i / steps) * Mathf.Deg2Rad;
                var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Add(new Vector2(cx, cz) + n * r, n);
            }
        }
    }

    // Rounded rectangle centred on x = 0 from z0 to z1, bottom corners rB, top corners rT. Me start at bottom middle
    // and go +X first (counter-clockwise with x right, z up); TopMid = index of the top middle point.
    private static Outline RoundRect(float halfW, float z0, float z1, float rB, float rT, int stepsB, int stepsT)
    {
        var o = new Outline();
        o.Add(new Vector2(0f, z0), new Vector2(0f, -1f));
        o.Arc(halfW - rB, z0 + rB, rB, -90f, 0f, stepsB);
        o.Arc(halfW - rT, z1 - rT, rT, 0f, 90f, stepsT);
        o.TopMid = o.Count;
        o.Add(new Vector2(0f, z1), new Vector2(0f, 1f));
        o.Arc(-(halfW - rT), z1 - rT, rT, 90f, 180f, stepsT);
        o.Arc(-(halfW - rB), z0 + rB, rB, 180f, 270f, stepsB);
        return o;
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // Lyre outer half width at height z (straight part of the sides, between the bottom and top corner arcs): full
    // soundbox, soft slim to the arm waist, soft flare out to the yoke. Flat (no slope) at both ends, so the corner
    // arcs join smooth.
    private static float LyreSideX(float z)
    {
        if (z <= LyreWaistZ)
        {
            return LyreHalfWidth + (LyreWaistHalf - LyreHalfWidth) * Ease((z - LyreBellyZ) / (LyreWaistZ - LyreBellyZ));
        }
        return LyreWaistHalf + (LyreYokeHalf - LyreWaistHalf) * Ease((z - LyreWaistZ) / (LyreFlareZ - LyreWaistZ));
    }

    // String height (y) at z on the bridge -> peg stretch.
    private static float LyreStringY(float z)
    {
        var bridge = LyreBridgeTop + LyreStringRadius;
        return bridge + (LyrePegY - bridge) * (z - LyreBridgeZ) / (LyrePegZ - LyreBridgeZ);
    }

    // Lyre outline, counter-clockwise from bottom middle: bottom corner arc, side by LyreSideX (extra point at the
    // soundboard top = RightStop / LeftStop), top corner arc, top middle (TopMid), then the left half as mirror.
    private static Outline LyreOutline()
    {
        const float rB = LyreBottomRound;
        const float rT = LyreTopRound;
        const int sideSteps = 30;
        var o = new Outline();
        o.Add(new Vector2(0f, 0f), new Vector2(0f, -1f));
        o.Arc(LyreHalfWidth - rB, rB, rB, -90f, 0f, 8);
        var z0 = rB;
        var z1 = LyreHeight - rT;
        for (var i = 1; i < sideSteps; i++)
        {
            var z = z0 + (z1 - z0) * i / sideSteps;
            if (o.RightStop < 0 && z > LyrePanelTop - 0.004f)
            {
                o.RightStop = o.Count;
                LyreSidePoint(o, LyrePanelTop);
                if (z < LyrePanelTop + 0.004f)
                {
                    continue;
                }
            }
            LyreSidePoint(o, z);
        }
        o.Arc(LyreYokeHalf - rT, z1, rT, 0f, 90f, 6);
        o.TopMid = o.Count;
        o.Add(new Vector2(0f, LyreHeight), new Vector2(0f, 1f));
        for (var i = o.TopMid - 1; i >= 1; i--)
        {
            if (i == o.RightStop)
            {
                o.LeftStop = o.Count;
            }
            o.Add(new Vector2(-o.P[i].x, o.P[i].y), new Vector2(-o.N[i].x, o.N[i].y));
        }
        return o;
    }

    // Right side point at z; normal = side slope turned a quarter (out and a bit down where the side flare out).
    private static void LyreSidePoint(Outline o, float z)
    {
        const float h = 0.001f;
        var slope = (LyreSideX(z + h) - LyreSideX(z - h)) / (2f * h);
        o.Add(new Vector2(LyreSideX(z), z), MeshKit.Unit(new Vector2(1f, -slope)));
    }

    // Closed rail along an outline (first point again at the end), Out = outline normal * outSign.
    private static MeshKit.RailPoint[] LoopRail(Outline o, float outSign, float uPerMetre)
    {
        var rail = new MeshKit.RailPoint[o.Count + 1];
        var len = 0f;
        for (var i = 0; i <= o.Count; i++)
        {
            var j = i % o.Count;
            if (i > 0)
            {
                len += (o.P[j] - o.P[i - 1]).magnitude;
            }
            rail[i] = new MeshKit.RailPoint
            {
                Base = new Vector3(o.P[j].x, 0f, o.P[j].y),
                Out = new Vector3(o.N[j].x, 0f, o.N[j].y) * outSign,
                U = len * uPerMetre,
            };
        }
        return rail;
    }

    private static Vector2 LyrePlanar(Vector2 p) => new Vector2(p.x * 6f + 0.5f, p.y * 6f);

    // Silver frame: outline and window walls with rounded edges; front = engraved band round the edge, plain metal
    // round the window and on the bar under it, dark hammered soundboard sunk a little below; back flat. Then bronze
    // bosses, tailpiece, wood bridge, linen strings over the whole soundboard, bronze pegs through the yoke.
    private static void BuildLyre(MeshKit k)
    {
        const float t = LyreHalfThick;
        const float e = LyreEdge;
        const float r = LyreStringRadius;
        var outer = LyreOutline();
        var win = RoundRect(LyreWinHalf, LyreWinBottom, LyreWinTop, LyreWinRoundBottom, LyreWinRoundTop, 4, 5);
        var count = outer.Count;
        var inner = e + LyreRim;
        var wallUv = MeshKit.UvMap.Length(1f / (2f * t));

        // Walls (solid on the right of the walk: front edge, down the side, back edge).
        var wall = new MeshKit.Run(SlotSilver, -1f).Arc(-e, t - e, e, 90f, 0f, 3).Arc(-e, -t + e, e, 0f, -90f, 3);
        k.Sweep(LoopRail(outer, 1f, 8f), Vector3.up, wall, wallUv);
        k.Sweep(LoopRail(win, -1f, 8f), Vector3.up, wall, wallUv);

        // Engraved band: u along the band (whole number of pattern tiles round the loop), v across it.
        var lengths = new float[count + 1];
        for (var i = 1; i <= count; i++)
        {
            lengths[i] = lengths[i - 1] + (outer.Out(i % count, -(e + LyreRim * 0.5f)) - outer.Out(i - 1, -(e + LyreRim * 0.5f))).magnitude;
        }
        var tiles = Mathf.Max(1f, Mathf.Round(lengths[count] / (LyreRim * 4f)));
        var start = k.Verts.Count;
        for (var i = 0; i <= count; i++)
        {
            var a = outer.Out(i % count, -e);
            var b = outer.Out(i % count, -inner);
            var u = lengths[i] / lengths[count] * tiles;
            k.Add(new Vector3(a.x, t, a.y), Vector3.up, new Vector2(u, 0f));
            k.Add(new Vector3(b.x, t, b.y), Vector3.up, new Vector2(u, 1f));
        }
        for (var i = 0; i < count; i++)
        {
            var a = start + i * 2;
            k.Quad(SlotEngraved, a, a + 2, a + 3, a + 1);
        }

        // Front round the window and on the bar under it (two halves cut at x = 0, so no polygon has a hole).
        var panelMid = new Vector2(0f, LyrePanelTop);
        var poly = new List<Vector2>();
        for (var i = outer.TopMid; i <= outer.LeftStop; i++)
        {
            poly.Add(outer.Out(i, -inner));
        }
        poly.Add(panelMid);
        poly.Add(win.Out(0, e));
        for (var i = win.Count - 1; i >= win.TopMid; i--)
        {
            poly.Add(win.Out(i, e));
        }
        k.Polygon(SlotSilver, poly, p => new Vector3(p.x, t, p.y), Vector3.up, LyrePlanar);
        poly.Clear();
        for (var i = outer.RightStop; i <= outer.TopMid; i++)
        {
            poly.Add(outer.Out(i, -inner));
        }
        for (var i = win.TopMid; i >= 0; i--)
        {
            poly.Add(win.Out(i, e));
        }
        poly.Add(panelMid);
        k.Polygon(SlotSilver, poly, p => new Vector3(p.x, t, p.y), Vector3.up, LyrePlanar);

        // Soundboard (dark hammered silver), sunk below the band, with its little step walls.
        var board = t - LyrePanelDepth;
        poly.Clear();
        for (var i = outer.LeftStop; i < count; i++)
        {
            poly.Add(outer.Out(i, -inner));
        }
        for (var i = 0; i <= outer.RightStop; i++)
        {
            poly.Add(outer.Out(i, -inner));
        }
        poly.Add(panelMid);
        k.Polygon(SlotHammered, poly, p => new Vector3(p.x, board, p.y), Vector3.up, LyrePlanar);
        var stepRail = new List<MeshKit.RailPoint>();
        for (var i = outer.LeftStop; i <= count + outer.RightStop; i++)
        {
            var j = i % count;
            var p = outer.Out(j, -inner);
            stepRail.Add(new MeshKit.RailPoint
            {
                Base = new Vector3(p.x, 0f, p.y),
                Out = new Vector3(-outer.N[j].x, 0f, -outer.N[j].y),
                U = stepRail.Count * 0.1f,
            });
        }
        var step = new MeshKit.Run(SlotSilver, -1f).To(0f, t).To(0f, board);
        k.Sweep(stepRail.ToArray(), Vector3.up, step, MeshKit.UvMap.Length(10f));
        var lineRail = new[]
        {
            LineRailPoint(outer.Out(outer.LeftStop, -inner), 0f),
            LineRailPoint(panelMid, 0.5f),
            LineRailPoint(outer.Out(outer.RightStop, -inner), 1f),
        };
        k.Sweep(lineRail, Vector3.up, step, MeshKit.UvMap.Length(10f));

        // Back: flat, two halves cut at x = 0.
        poly.Clear();
        for (var i = outer.TopMid; i < count; i++)
        {
            poly.Add(outer.Out(i, -e));
        }
        poly.Add(outer.Out(0, -e));
        poly.Add(win.Out(0, e));
        for (var i = win.Count - 1; i >= win.TopMid; i--)
        {
            poly.Add(win.Out(i, e));
        }
        k.Polygon(SlotSilver, poly, p => new Vector3(p.x, -t, p.y), Vector3.down, LyrePlanar);
        poly.Clear();
        for (var i = 0; i <= outer.TopMid; i++)
        {
            poly.Add(outer.Out(i, -e));
        }
        for (var i = win.TopMid; i >= 0; i--)
        {
            poly.Add(win.Out(i, e));
        }
        k.Polygon(SlotSilver, poly, p => new Vector3(p.x, -t, p.y), Vector3.down, LyrePlanar);

        // Four bronze bosses (studs of old lyres): where the arms leave the body and where they meet the yoke.
        for (var side = -1; side <= 1; side += 2)
        {
            k.Dome(SlotBronze, new Vector3(side * 0.0795f, t, 0.237f), Vector3.up, 0.0062f, 0.0028f, 12, 3);
            k.Dome(SlotBronze, new Vector3(side * 0.088f, t, LyreWinTop + 0.0045f), Vector3.up, 0.0068f, 0.003f, 12, 3);
        }

        // Bronze tailpiece (trapezoid plate) at the soundboard foot, light wood bridge (wedge) on the soundboard.
        var tailTop = board + 0.006f;
        k.Hexa(SlotBronze, new[]
        {
            new Vector3(-0.036f, board, 0.021f), new Vector3(0.036f, board, 0.021f),
            new Vector3(0.047f, board, 0.043f), new Vector3(-0.047f, board, 0.043f),
            new Vector3(-0.036f, tailTop, 0.021f), new Vector3(0.036f, tailTop, 0.021f),
            new Vector3(0.047f, tailTop, 0.043f), new Vector3(-0.047f, tailTop, 0.043f),
        }, 20f);
        k.Hexa(SlotWood, new[]
        {
            new Vector3(-0.058f, board, LyreBridgeZ - 0.006f), new Vector3(0.058f, board, LyreBridgeZ - 0.006f),
            new Vector3(0.058f, board, LyreBridgeZ + 0.006f), new Vector3(-0.058f, board, LyreBridgeZ + 0.006f),
            new Vector3(-0.056f, LyreBridgeTop, LyreBridgeZ - 0.0015f), new Vector3(0.056f, LyreBridgeTop, LyreBridgeZ - 0.0015f),
            new Vector3(0.056f, LyreBridgeTop, LyreBridgeZ + 0.0015f), new Vector3(-0.056f, LyreBridgeTop, LyreBridgeZ + 0.0015f),
        }, 20f);

        // Strings: tailpiece -> over the bridge -> peg; pegs through the yoke (bronze collar and shaft in front, flat
        // key behind).
        for (var i = 0; i < LyreStrings; i++)
        {
            var f = i / (float)(LyreStrings - 1) * 2f - 1f;
            var tail = new Vector3(f * LyreTailSpread, tailTop + r, LyreTailZ);
            var bridge = new Vector3(f * LyreBridgeSpread, LyreBridgeTop + r, LyreBridgeZ);
            var pegX = f * LyrePegSpread;
            var peg = new Vector3(pegX, LyrePegY, LyrePegZ);
            k.Tube(SlotLinen, tail, bridge, r, r, 5, false, false);
            k.Tube(SlotLinen, bridge, peg, r, r, 5, false, false);
            k.Tube(SlotBronze, new Vector3(pegX, t - 0.001f, LyrePegZ), new Vector3(pegX, t + 0.004f, LyrePegZ), 0.0045f,
                0.0045f, 10, false, true);
            k.Tube(SlotBronze, new Vector3(pegX, t + 0.004f, LyrePegZ), new Vector3(pegX, t + 0.011f, LyrePegZ), 0.0026f,
                0.0026f, 8, false, true);
            k.Hexa(SlotBronze, Box(new Vector3(pegX, -t - 0.0026f, LyrePegZ), new Vector3(0.0065f, 0.0026f, 0.0078f)), 20f);
        }
    }

    private static MeshKit.RailPoint LineRailPoint(Vector2 p, float u) => new MeshKit.RailPoint
    {
        Base = new Vector3(p.x, 0f, p.y),
        Out = Vector3.back,
        U = u,
    };

    // 8 corners of an axis box (centre, half sizes) in Hexa order.
    private static Vector3[] Box(Vector3 c, Vector3 h) => new[]
    {
        c + new Vector3(-h.x, -h.y, -h.z), c + new Vector3(h.x, -h.y, -h.z),
        c + new Vector3(h.x, -h.y, h.z), c + new Vector3(-h.x, -h.y, h.z),
        c + new Vector3(-h.x, h.y, -h.z), c + new Vector3(h.x, h.y, -h.z),
        c + new Vector3(h.x, h.y, h.z), c + new Vector3(-h.x, h.y, h.z),
    };

    private static float TambAngle(int i) => -Mathf.PI * 0.5f + i * (Mathf.PI * 2f / TambSegments);

    // First hoop piece of jingle slot s (slots spread evenly, none at the grip at angle -90 degrees).
    private static int SlotStart(int s) => s * (TambSegments / TambSlots) + (TambSegments / TambSlots - TambSlotWidth) / 2;

    private static bool InSlot(int piece) => piece % (TambSegments / TambSlots) >= SlotStart(0)
                                             && piece % (TambSegments / TambSlots) < SlotStart(0) + TambSlotWidth;

    // Hoop lathe with slots cut through its wall, skin over the top wrapping the rim, tack ring, pins and jingles.
    private static void BuildTambourine(MeshKit k)
    {
        var c = TambCentre;
        var rail = MeshKit.Circle(c, Vector3.right, Vector3.forward, -Mathf.PI * 0.5f, Mathf.PI * 1.5f, TambSegments, 5f);
        var up = Vector3.up;

        // Hoop: inner wall down, rounded bottom, outer wall up (grain along the hoop: swapped uv).
        var hoop = new MeshKit.Run(SlotWood, 1f).To(TambInner, TambSkin - 0.0004f).To(TambInner, TambSlotHalf)
            .To(TambInner, -TambSlotHalf).To(TambInner, -TambHalfDepth + 0.003f)
            .Arc(TambInner + 0.003f, -TambHalfDepth + 0.003f, 0.003f, 180f, 270f, 3)
            .Arc(TambOuter - 0.003f, -TambHalfDepth + 0.003f, 0.003f, 270f, 360f, 3)
            .To(TambOuter, -TambSlotHalf);
        var outerSkip = hoop.Points.Count - 1;
        hoop.To(TambOuter, TambSlotHalf).To(TambOuter, 0.0195f);
        var hoopUv = new MeshKit.UvMap { VPerMetre = 1f / 0.06f, Swap = true };
        k.Sweep(rail, up, hoop, hoopUv, (piece, seg) => InSlot(piece) && (seg == 1 || seg == outerSkip));

        // Slot walls: ends (flat), floor and roof.
        for (var s = 0; s < TambSlots; s++)
        {
            var s0 = SlotStart(s);
            var s1 = s0 + TambSlotWidth;
            SlotEnd(k, s0, 1f);
            SlotEnd(k, s1, -1f);
            var part = MeshKit.Slice(rail, s0, s1);
            k.Sweep(part, up, new MeshKit.Run(SlotWood, -1f).To(TambInner, -TambSlotHalf).To(TambOuter, -TambSlotHalf), hoopUv);
            k.Sweep(part, up, new MeshKit.Run(SlotWood, 1f).To(TambInner, TambSlotHalf).To(TambOuter, TambSlotHalf), hoopUv);

            // Pin across the slot and a pair of jingles on it, domes facing away from each other.
            var a = TambAngle(s0 + TambSlotWidth / 2);
            var pin = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ((TambInner + TambOuter) * 0.5f);
            k.Tube(SlotBronze, pin - up * TambSlotHalf, pin + up * TambSlotHalf, 0.0011f, 0.0011f, 6, false, false);
            Jingle(k, pin + up * 0.0042f, up);
            Jingle(k, pin - up * 0.0042f, -up);
        }

        // Skin: top from the centre, over the hoop edge and down the outside; thin lip; underside inside the hoop.
        var skinUv = new MeshKit.UvMap { Planar = p => new Vector2(0.5f + p.x / 0.27f, 0.5f + (p.z - 0.13f) / 0.27f) };
        var skin = new MeshKit.Run(SlotLeather, -1f).To(0f, TambSkin).To(0.06f, TambSkin).To(0.112f, TambSkin)
            .To(0.117f, 0.0236f).To(0.1205f, 0.0243f).To(0.1235f, 0.0249f).To(0.1274f, 0.0252f)
            .Arc(0.1274f, 0.0219f, 0.0033f, 90f, 0f, 3).To(0.1307f, 0.0188f);
        k.Sweep(rail, up, skin, skinUv);
        k.Sweep(rail, up, new MeshKit.Run(SlotLeather, -1f).To(0.1307f, 0.0188f).To(TambOuter - 0.0001f, 0.0188f), skinUv);
        k.Sweep(rail, up, new MeshKit.Run(SlotLeather, 1f).To(0f, TambSkin - 0.0004f).To(TambInner + 0.0003f, TambSkin - 0.0004f), skinUv);

        // Bronze tacks holding the skin round the outside.
        for (var j = 0; j < 24; j++)
        {
            var a = -Mathf.PI * 0.5f + (j + 0.5f) * Mathf.PI * 2f / 24f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            k.Dome(SlotBronze, c + dir * 0.1307f + up * 0.0203f, dir, 0.0017f, 0.0009f, 6, 2);
        }
    }

    // Flat end of a slot at hoop piece boundary i; facing +1 = toward bigger angles (into the slot).
    private static void SlotEnd(MeshKit k, int i, float facing)
    {
        var a = TambAngle(i);
        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        var n = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * facing;
        var c = TambCentre;
        var p0 = k.Add(c + dir * TambInner + Vector3.down * TambSlotHalf, n, new Vector2(0f, 0f));
        var p1 = k.Add(c + dir * TambOuter + Vector3.down * TambSlotHalf, n, new Vector2(0.15f, 0f));
        var p2 = k.Add(c + dir * TambOuter + Vector3.up * TambSlotHalf, n, new Vector2(0.15f, 0.5f));
        var p3 = k.Add(c + dir * TambInner + Vector3.up * TambSlotHalf, n, new Vector2(0f, 0.5f));
        k.Quad(SlotWood, p0, p1, p2, p3);
    }

    // One jingle (zil): thin bronze disc 33 mm wide with a dome round its hole; dome toward up.
    private static void Jingle(MeshKit k, Vector3 centre, Vector3 up)
    {
        var rail = MeshKit.Circle(centre, Vector3.right, Vector3.forward, 0f, Mathf.PI * 2f, 12, 1f);
        var uv = MeshKit.UvMap.Length(30f);
        k.Sweep(rail, up, new MeshKit.Run(SlotBronze, -1f).To(0.0013f, 0.0027f).To(0.0045f, 0.0024f)
            .To(0.007f, 0.0008f).To(0.01f, 0.0002f).To(0.0165f, 0f), uv);
        k.Sweep(rail, up, new MeshKit.Run(SlotBronze, -1f).To(0.0165f, 0f).To(0.0165f, -0.0007f), uv);
        k.Sweep(rail, up, new MeshKit.Run(SlotBronze, -1f).To(0.0165f, -0.0007f).To(0.01f, -0.0005f)
            .To(0.007f, 0.0001f).To(0.0045f, 0.0017f).To(0.0013f, 0.002f), uv);
        k.Sweep(rail, up, new MeshKit.Run(SlotBronze, -1f).To(0.0013f, 0.002f).To(0.0013f, 0.0027f), uv);
    }

    // Tambourine collider shape: closed 12 side prism round the drum (flat side 0.1325 out from the centre, so it
    // cover hoop and tacks; corners 0.137), 0.052 high from hoop bottom to skin top.
    private const int TambHullSides = 12;
    private const float TambHullApothem = 0.1325f;
    private const float TambHullHalfHeight = 0.026f;

    internal static MeshKit BuildTambourineHull()
    {
        var kit = new MeshKit(1);
        var corner = TambHullApothem / Mathf.Cos(Mathf.PI / TambHullSides);
        var rail = MeshKit.Circle(TambCentre, Vector3.right, Vector3.forward, 0f, Mathf.PI * 2f, TambHullSides, 1f);
        kit.Sweep(rail, Vector3.up, new MeshKit.Run(0, 1f).To(0f, -TambHullHalfHeight).To(corner, -TambHullHalfHeight),
            MeshKit.UvMap.Length(1f));
        kit.Sweep(rail, Vector3.up, new MeshKit.Run(0, 1f).To(corner, -TambHullHalfHeight).To(corner, TambHullHalfHeight),
            MeshKit.UvMap.Length(1f));
        kit.Sweep(rail, Vector3.up, new MeshKit.Run(0, 1f).To(corner, TambHullHalfHeight).To(0f, TambHullHalfHeight),
            MeshKit.UvMap.Length(1f));
        return kit;
    }

    // ---------------------------------------------------------------------------------------------------- textures

    // Pixels of a slot's texture (rows bottom to top). clamp = texture must not repeat.
    internal static Color32[] Pixels(int slot, out int width, out int height, out bool clamp)
    {
        clamp = false;
        switch (slot)
        {
            case SlotWood:
                width = 64;
                height = 128;
                return WoodPixels(width, height, new Color(0.80f, 0.63f, 0.41f), new Color(0.55f, 0.38f, 0.22f), 11);
            case SlotDarkWood:
                width = 32;
                height = 64;
                return WoodPixels(width, height, new Color(0.33f, 0.20f, 0.11f), new Color(0.17f, 0.09f, 0.05f), 23);
            case SlotShadow:
                width = 8;
                height = 8;
                return Brushed(width, height, new Color(0.05f, 0.035f, 0.025f), 0.1f, 5);
            case SlotSilver:
                width = 64;
                height = 64;
                return Brushed(width, height, new Color(0.86f, 0.86f, 0.89f), 0.12f, 31);
            case SlotEngraved:
                width = 128;
                height = 32;
                return KnotworkPixels(width, height, new Color(0.88f, 0.88f, 0.91f), new Color(0.30f, 0.29f, 0.31f));
            case SlotLinen:
                width = 16;
                height = 64;
                return LinenPixels(width, height);
            case SlotLeather:
                width = 128;
                height = 128;
                clamp = true;
                return LeatherPixels(width, height);
            case SlotBronze:
                width = 32;
                height = 32;
                return Brushed(width, height, new Color(0.82f, 0.58f, 0.30f), 0.14f, 37);
            case SlotHammered:
                width = 64;
                height = 64;
                // Dark old silver (much darker than the frame), so the bright frame outline stand out round it.
                return HammeredPixels(width, height, new Color(0.36f, 0.355f, 0.38f), 12, 61);
            default:
                width = 4;
                height = 4;
                return Brushed(width, height, Color.gray, 0f, 1);
        }
    }

    private static Color32 Opaque(Color c)
    {
        c.a = 1f;
        return c;
    }

    // Wood: noise bend the rings (dark late wood lines), fine fibres on top; grain go along v.
    private static Color32[] WoodPixels(int w, int h, Color light, Color dark, int seed)
    {
        var px = new Color32[w * h];
        for (var y = 0; y < h; y++)
        {
            var v = (y + 0.5f) / h;
            for (var x = 0; x < w; x++)
            {
                var u = (x + 0.5f) / w;
                var warp = TexNoise.Fbm(u, v, 3, 2, 3, seed);
                var ring = 0.5f + 0.5f * Mathf.Sin((u * 7f + warp * 2.5f) * Mathf.PI * 2f);
                ring = ring * ring * ring * ring * ring;
                var fibre = TexNoise.Fbm(u, v, 24, 3, 2, seed + 17);
                var k = Mathf.Clamp01(ring * 0.6f + (fibre - 0.5f) * 0.5f + 0.12f);
                var c = Color.Lerp(light, dark, k) * (0.95f + 0.1f * TexNoise.Value(u * 64f, v * 8f, 64, 8, seed + 33));
                px[y * w + x] = Opaque(c);
            }
        }
        return px;
    }

    // Metal or plain colour with faint streaks along u.
    private static Color32[] Brushed(int w, int h, Color c, float streak, int seed)
    {
        var px = new Color32[w * h];
        for (var y = 0; y < h; y++)
        {
            var v = (y + 0.5f) / h;
            for (var x = 0; x < w; x++)
            {
                var u = (x + 0.5f) / w;
                var n = TexNoise.Fbm(u, v, 2, Math.Max(2, h / 2), 3, seed) - 0.5f;
                px[y * w + x] = Opaque(c * (1f + n * streak * 2f));
            }
        }
        return px;
    }

    // Engraved band: two ribbon twist over and under each other between two border lines, punched ground round them.
    // u = along the band (tile repeat), v = across (0 outer edge, 1 inner edge).
    private static Color32[] KnotworkPixels(int w, int h, Color metal, Color line)
    {
        const int twists = 2;
        const float amp = 0.25f;
        const float half = 0.085f;
        const float edge = 0.032f;
        const float gap = 0.035f;
        var px = new Color32[w * h];
        var aspect = h / (float)w; // v units per u unit for equal metres (band tile 4 wide, 1 high)
        for (var y = 0; y < h; y++)
        {
            var v = (y + 0.5f) / h;
            for (var x = 0; x < w; x++)
            {
                var u = (x + 0.5f) / w;
                var phase = u * twists * Mathf.PI * 2f;
                var s = Mathf.Sin(phase);
                var slope = amp * twists * Mathf.PI * 2f * Mathf.Cos(phase) * aspect;
                var scale = 1f / Mathf.Sqrt(1f + slope * slope);
                var d1 = Mathf.Abs(v - (0.5f + amp * s)) * scale;
                var d2 = Mathf.Abs(v - (0.5f - amp * s)) * scale;
                var crossing = (int)Math.Round(u * twists * 2f);
                var firstOnTop = crossing % 2 == 0;
                var dTop = firstOnTop ? d1 : d2;
                var dUnder = firstOnTop ? d2 : d1;
                Color c;
                if (v < 0.06f || v > 0.94f)
                {
                    c = metal;
                }
                else if (v < 0.11f || v > 0.89f)
                {
                    c = line;
                }
                else if (dTop < half)
                {
                    c = dTop > half - edge ? line : metal * (1.0f + 0.08f * (1f - dTop / half));
                }
                else if (dTop < half + gap && dUnder < half)
                {
                    c = line;
                }
                else if (dUnder < half)
                {
                    c = dUnder > half - edge ? line : metal * (1.0f + 0.08f * (1f - dUnder / half));
                }
                else
                {
                    var dot = TexNoise.Value(x, y, w, h, 77);
                    c = metal * (0.55f + 0.15f * dot);
                }
                px[y * w + x] = Opaque(c);
            }
        }
        return px;
    }

    // Hammered metal: me put shallow round dents on a shaken grid (pixel take nearest dent centre), one slope lit,
    // other slope dark. cells = dents per side; tile have no seam.
    private static Color32[] HammeredPixels(int w, int h, Color metal, int cells, int seed)
    {
        var px = new Color32[w * h];
        for (var y = 0; y < h; y++)
        {
            var fy = (y + 0.5f) / h * cells;
            var iy = (int)Math.Floor(fy);
            for (var x = 0; x < w; x++)
            {
                var fx = (x + 0.5f) / w * cells;
                var ix = (int)Math.Floor(fx);
                var best = float.MaxValue;
                var bx = 0f;
                var by = 0f;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var cx = ix + dx;
                        var cy = iy + dy;
                        var hx = ((cx % cells) + cells) % cells;
                        var hy = ((cy % cells) + cells) % cells;
                        var jx = cx + 0.2f + 0.6f * TexNoise.Hash(hx, hy, seed);
                        var jy = cy + 0.2f + 0.6f * TexNoise.Hash(hx, hy, seed + 1);
                        var d = (fx - jx) * (fx - jx) + (fy - jy) * (fy - jy);
                        if (d < best)
                        {
                            best = d;
                            bx = fx - jx;
                            by = fy - jy;
                        }
                    }
                }
                var slope = Mathf.Clamp((by * 0.8f - bx * 0.6f) * 0.16f, -0.07f, 0.07f);
                var n = TexNoise.Fbm((x + 0.5f) / w, (y + 0.5f) / h, 4, 4, 2, seed + 5) - 0.5f;
                px[y * w + x] = Opaque(metal * (1f + slope + n * 0.08f));
            }
        }
        return px;
    }

    // Linen thread: cream with a faint twist.
    private static Color32[] LinenPixels(int w, int h)
    {
        var px = new Color32[w * h];
        var cream = new Color(0.88f, 0.82f, 0.67f);
        for (var y = 0; y < h; y++)
        {
            var v = (y + 0.5f) / h;
            for (var x = 0; x < w; x++)
            {
                var u = (x + 0.5f) / w;
                var twist = 0.5f + 0.5f * Mathf.Sin((u * 2f + v * 8f) * Mathf.PI * 2f);
                var n = TexNoise.Fbm(u, v, 4, 16, 2, 51);
                px[y * w + x] = Opaque(cream * (0.86f + 0.1f * twist + 0.08f * n));
            }
        }
        return px;
    }

    // Tambourine skin seen from above (uv 0..1 = 0.27 m round drum centre): spotty tan with grain. Skin go darker where
    // it bend over the hoop and wrap down the outside; hoop inner edge press a dark line in it.
    private static Color32[] LeatherPixels(int w, int h)
    {
        var px = new Color32[w * h];
        var tan = new Color(0.74f, 0.55f, 0.35f);
        for (var y = 0; y < h; y++)
        {
            var v = (y + 0.5f) / h;
            for (var x = 0; x < w; x++)
            {
                var u = (x + 0.5f) / w;
                var dx = u - 0.5f;
                var dy = v - 0.5f;
                var r = Mathf.Sqrt(dx * dx + dy * dy) * 0.27f;
                var mottle = TexNoise.Fbm(u, v, 5, 5, 4, 41);
                var grain = TexNoise.Fbm(u, v, 40, 40, 2, 43);
                var c = tan * (0.84f + mottle * 0.3f + (grain - 0.5f) * 0.12f);
                c *= 1f - 0.22f * Mathf.Clamp01((r - 0.11f) / 0.012f);
                c *= 1f - 0.18f * Mathf.Clamp01((r - 0.1272f) / 0.003f);
                if (Mathf.Abs(r - TambInner) < 0.0012f)
                {
                    c *= 0.8f;
                }
                px[y * w + x] = Opaque(c);
            }
        }
        return px;
    }

#if !OFFLINE_PREVIEW
    // ---------------------------------------------------------------------------------------------------- unity parts

    private static readonly Mesh[] Meshes = new Mesh[4];
    private static readonly Material[][] KindMaterials = new Material[4][];
    private static readonly bool[] KindFailed = new bool[4];
    private static Material[] _palette;
    private static bool _modelsFailed;
    private static Mesh _tambourineHull;
    private static bool _hullFailed;

    private static bool HasGraphics => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

    private static bool Known(InstrumentKind kind) =>
        kind == InstrumentKind.Flute || kind == InstrumentKind.Lyre || kind == InstrumentKind.Tambourine;

    // New model object (layer of the parent) under parent, scaled, with its markers. Mesh failure = logged, the
    // instrument stay usable (only unseen). Build failed once (palette for all, or this kind's mesh) = me skip the
    // mesh after: same maths fail same way, no log spam.
    internal static GameObject Create(InstrumentKind kind, Transform parent, Material baseMaterial, float scale)
    {
        var go = new GameObject(ModelName);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * scale;
        if (HasGraphics && Known(kind) && !_modelsFailed && !KindFailed[(int)kind])
        {
            try
            {
                var mesh = MeshFor(kind, baseMaterial, out var materials);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            catch (Exception e)
            {
                PatchGuard.Report("InstrumentModels.Create", e);
            }
        }
        var spots = Markers(kind);
        for (var i = 0; i < spots.Length; i++)
        {
            var marker = new GameObject(spots[i].Name);
            marker.layer = go.layer;
            marker.transform.SetParent(go.transform, false);
            marker.transform.localPosition = spots[i].Position;
            marker.transform.localRotation = Quaternion.LookRotation(spots[i].Forward, spots[i].Up);
        }
        return go;
    }

    // Collider fitting the shape on a child, item layer (pickup, rest on the ground).
    internal static void AddCollider(InstrumentKind kind, GameObject model, int layer)
    {
        var go = new GameObject(ColliderName);
        go.layer = layer;
        go.transform.SetParent(model.transform, false);
        switch (kind)
        {
            case InstrumentKind.Flute:
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.direction = 2; // along Z
                capsule.radius = 0.0135f;
                capsule.height = FluteTip - FluteFoot;
                capsule.center = new Vector3(0f, 0f, (FluteTip + FluteFoot) * 0.5f);
                break;
            }
            case InstrumentKind.Lyre:
            {
                // Yoke = widest part. Y from the peg keys behind (-0.0205) to the strings over the bridge (+0.0296).
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(LyreYokeHalf * 2f, 0.051f, LyreHeight);
                box.center = new Vector3(0f, 0.0045f, LyreHeight * 0.5f);
                break;
            }
            case InstrumentKind.Tambourine:
            {
                // Round drum: convex 12 side prism (a box leave empty corners round a disc). Hull failed = box.
                var hull = TambourineHull();
                if (hull != null)
                {
                    var mesh = go.AddComponent<MeshCollider>();
                    mesh.convex = true;
                    mesh.sharedMesh = hull;
                }
                else
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.size = new Vector3(TambOuter * 2f, TambHullHalfHeight * 2f, TambOuter * 2f);
                    box.center = TambCentre;
                }
                break;
            }
            default:
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(0.1f, 0.1f, 0.3f);
                box.center = new Vector3(0f, 0f, 0.15f);
                break;
            }
        }
    }

    // Tambourine collider mesh, made on first ask (dedicated server too: physics need no graphics), kept whole
    // session. Null = it failed once (logged), caller use a box.
    private static Mesh TambourineHull()
    {
        if (_tambourineHull == null && !_hullFailed)
        {
            try
            {
                _tambourineHull = BuildTambourineHull().ToCollisionMesh("MC_TambourineHull");
            }
            catch (Exception e)
            {
                _hullFailed = true;
                PatchGuard.Report("InstrumentModels.TambourineHull", e);
            }
        }
        return _tambourineHull;
    }

    // Debug: what the vanilla base material offer (shader name, texture slots), to check Make in game.
    internal static string Describe(Material m)
    {
        if (m == null)
        {
            return "none";
        }
        var names = new List<string>();
        foreach (var prop in m.GetTexturePropertyNames())
        {
            names.Add(prop + (m.GetTexture(prop) != null ? "*" : ""));
        }
        return $"{m.name} shader={(m.shader != null ? m.shader.name : "null")} textures=[{string.Join(", ", names.ToArray())}]"
               + $" color={(m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "-")}"
               + $" metallic={(m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString("0.##") : "-")}"
               + $" gloss={(m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness").ToString("0.##") : "-")}";
    }

    // Mesh of a kind and the materials of its submeshes, made on first ask, kept whole session. Throw = me drop the
    // half made mesh, mark the kind failed, throw on.
    private static Mesh MeshFor(InstrumentKind kind, Material baseMaterial, out Material[] materials)
    {
        var i = (int)kind;
        if (Meshes[i] == null || KindMaterials[i] == null)
        {
            Mesh mesh = null;
            try
            {
                mesh = BuildGeometry(kind).ToMesh("MC_" + kind, out var slots);
                var palette = Palette(baseMaterial);
                var mats = new Material[slots.Length];
                for (var j = 0; j < slots.Length; j++)
                {
                    mats[j] = palette[slots[j]];
                }
                Meshes[i] = mesh;
                KindMaterials[i] = mats;
            }
            catch
            {
                KindFailed[i] = true;
                Discard(mesh);
                throw;
            }
        }
        materials = KindMaterials[i];
        return Meshes[i];
    }

    // One material per slot, shared by all kinds, made once. Throw part way = me destroy every texture and material
    // made so far and set _modelsFailed (no more tries), throw on.
    private static Material[] Palette(Material baseMaterial)
    {
        if (_palette != null)
        {
            return _palette;
        }
        var made = new List<UnityEngine.Object>();
        try
        {
            var palette = new Material[SlotCount];
            for (var i = 0; i < SlotCount; i++)
            {
                var pixels = Pixels(i, out var w, out var h, out var clamp);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
                made.Add(tex);
                tex.name = "MC_Instrument_" + SlotNames[i];
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                tex.hideFlags = HideFlags.HideAndDontSave;
                tex.SetPixels32(pixels);
                tex.Apply(true, true);
                palette[i] = Make(baseMaterial, "MC_Instrument_" + SlotNames[i], tex, SlotMetallic[i], SlotGloss[i], made);
            }
            _palette = palette;
            return palette;
        }
        catch
        {
            _modelsFailed = true;
            for (var i = 0; i < made.Count; i++)
            {
                Discard(made[i]);
            }
            throw;
        }
    }

    private static void Discard(UnityEngine.Object o)
    {
        if (o != null)
        {
            UnityEngine.Object.Destroy(o);
        }
    }

    // Copy of the vanilla material with our texture as its main texture; its own detail textures (laid out for the
    // vanilla mesh) taken off. Null base (no vanilla material found) = plain default material. New material go in
    // made first thing, so a throw later still find it.
    private static Material Make(Material baseMaterial, string name, Texture2D tex, float metallic, float smoothness,
        List<UnityEngine.Object> made)
    {
        Material m;
        if (baseMaterial != null)
        {
            m = new Material(baseMaterial);
        }
        else
        {
            var shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Legacy Shaders/Diffuse");
            }
            m = new Material(shader);
        }
        made.Add(m);
        m.name = name;
        m.hideFlags = HideFlags.HideAndDontSave;
        foreach (var prop in m.GetTexturePropertyNames())
        {
            if (prop == "_MainTex")
            {
                continue;
            }
            if (m.HasProperty(prop) && m.GetTexture(prop) != null)
            {
                m.SetTexture(prop, null);
            }
        }
        m.mainTexture = tex;
        m.mainTextureScale = Vector2.one;
        m.mainTextureOffset = Vector2.zero;
        if (m.HasProperty("_Color"))
        {
            m.SetColor("_Color", Color.white);
        }
        if (m.HasProperty("_Metallic"))
        {
            m.SetFloat("_Metallic", metallic);
        }
        if (m.HasProperty("_Glossiness"))
        {
            m.SetFloat("_Glossiness", smoothness);
        }
        m.DisableKeyword("_NORMALMAP");
        m.DisableKeyword("_METALLICGLOSSMAP");
        m.DisableKeyword("_EMISSION");
        return m;
    }
#endif
}
