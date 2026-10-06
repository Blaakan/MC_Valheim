using System;
using MC.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = every moving part of the mini-game in ONE mesh: success meter left of the lanes, four lanes, key boxes (flash
// on press), hit line (glow on a hit), falling notes, missed notes (grey, fading under the line). Plain coloured quads
// (default UI material, white texture), rebuilt each frame by SetVerticesDirty, on its own nested canvas so the
// rebuild never touches the texts. Reads the MiniGame straight in OnPopulateMesh: no per-frame lists.
// Units = rect units (1920x1080 reference, rect bottom-left = meter bottom-left; lanes start at LanesX).
internal sealed class NoteField : MaskableGraphic
{
    internal const float LaneW = 70f;
    internal const float LaneGap = 6f;
    internal const float LaneH = 420f;
    internal const float HitY = 64f;       // hit line centre above the lane bottom
    internal const float KeyBottom = 10f;  // key box under the hit line
    internal const float KeyTop = 50f;
    internal const float NoteH = 18f;
    internal const float MeterGap = 14f;
    internal const float MeterW = 16f;
    internal const float LanesX = MeterW + MeterGap; // lane 0 left edge (meter sit left of the lanes)
    internal const float FieldW = Chart.Lanes * LaneW + (Chart.Lanes - 1) * LaneGap; // lanes only
    internal const float Width = LanesX + FieldW;
    internal const float LanesCentre = LanesX + FieldW * 0.5f;
    private const float TopFade = 40f;
    private const float PressFlash = 0.15f;
    private const float HitGlow = 0.25f;

    // Warm palette, one per lane: amber, teal, crimson, moss green.
    internal static readonly Color32[] Palette =
    {
        new Color32(236, 168, 56, 255),
        new Color32(64, 178, 168, 255),
        new Color32(206, 62, 58, 255),
        new Color32(134, 170, 66, 255),
    };

    private static readonly Color32 LaneBack = new Color32(14, 10, 7, 150);
    private static readonly Color32 HitLine = new Color32(245, 222, 160, 220);
    private static readonly Color32 MissGrey = new Color32(130, 126, 120, 255);
    private static readonly Color32 MeterFrame = new Color32(0, 0, 0, 170);
    private static readonly Color32 MeterBack = new Color32(40, 32, 24, 200);
    internal static readonly Color32 MeterFlow = new Color32(242, 193, 78, 255);    // in flow: filling
    internal static readonly Color32 MeterBehind = new Color32(186, 72, 52, 255);   // out of flow: meter wait
    internal static readonly Color32 MeterWait = new Color32(128, 143, 166, 255);   // long rest: waiting
    internal static readonly Color32 MeterIdle = new Color32(120, 112, 100, 255);   // too few notes yet

    internal MiniGame Game;

    internal static float LaneX(int lane) => LanesX + lane * (LaneW + LaneGap);

    internal static float LaneCentre(int lane) => LaneX(lane) + LaneW * 0.5f;

    // Same order as the HUD's meter text: warming up, resting, in flow, draining.
    internal static Color32 MeterColor(MiniGame game)
    {
        if (game.Accuracy < 0f)
        {
            return MeterIdle;
        }
        if (game.Meter.Waiting)
        {
            return MeterWait;
        }
        return game.Meter.InFlow ? MeterFlow : MeterBehind;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var game = Game;
        if (game == null)
        {
            return;
        }
        try
        {
            Draw(vh, game);
        }
        catch (Exception e)
        {
            vh.Clear();
            PatchGuard.Report("MiniGameHud note field", e);
        }
    }

    private void Draw(VertexHelper vh, MiniGame game)
    {
        var r = rectTransform.rect;
        var ox = r.xMin;
        var oy = r.yMin;
        var clock = game.Clock;
        var travel = LaneH - HitY;
        var look = Mathf.Max(0.1f, game.LookAhead);

        // Lanes, press flash, key boxes.
        for (var lane = 0; lane < Chart.Lanes; lane++)
        {
            var x0 = ox + LaneX(lane);
            var x1 = x0 + LaneW;
            Quad(vh, x0, oy, x1, oy + LaneH, LaneBack);
            Quad(vh, x0, oy, x1, oy + LaneH, WithAlpha(Palette[lane], 0.07f));
            var press = Fade(clock - game.LanePressedAt[lane], PressFlash);
            if (press > 0f)
            {
                Quad(vh, x0, oy, x1, oy + LaneH, WithAlpha(new Color32(255, 255, 255, 255), 0.1f * press));
            }
            Quad(vh, x0 + 4f, oy + KeyBottom, x1 - 4f, oy + KeyTop, WithAlpha(Palette[lane], 0.28f + 0.5f * press));
        }

        // Hit line, then hit glows on it.
        Quad(vh, ox + LanesX, oy + HitY - 1.5f, ox + LanesX + FieldW, oy + HitY + 1.5f, HitLine);
        for (var lane = 0; lane < Chart.Lanes; lane++)
        {
            var glow = Fade(clock - game.LaneHitAt[lane], HitGlow);
            if (glow > 0f)
            {
                var x0 = ox + LaneX(lane);
                var grow = 6f * (1f - glow);
                Quad(vh, x0 - grow, oy + HitY - 12f - grow, x0 + LaneW + grow, oy + HitY + 12f + grow,
                    WithAlpha(Palette[lane], 0.6f * glow));
            }
        }

        // Missed notes: grey, fading, sliding on under the line.
        var misses = game.RecentMisses;
        for (var i = 0; i < misses.Count; i++)
        {
            var key = misses[i].Key;
            var age = clock - misses[i].Value;
            var a = 1f - age / MiniGame.Behind;
            if (a <= 0f)
            {
                continue;
            }
            var y = HitY + (game.NoteTime(key.Key, key.Value) - clock) / look * travel;
            DrawNote(vh, ox, oy, game.Lane(key.Value), y, MissGrey, 0.7f * a);
        }

        // Notes still to play.
        var visible = game.Visible;
        for (var i = 0; i < visible.Count; i++)
        {
            var key = visible[i];
            var lane = game.Lane(key.Value);
            var y = HitY + (game.NoteTime(key.Key, key.Value) - clock) / look * travel;
            DrawNote(vh, ox, oy, lane, y, Palette[lane & 3], 1f);
        }

        // Success meter left of the lanes: fills from the bottom.
        var mx = ox;
        Quad(vh, mx - 2f, oy - 2f, mx + MeterW + 2f, oy + LaneH + 2f, MeterFrame);
        Quad(vh, mx, oy, mx + MeterW, oy + LaneH, MeterBack);
        var fill = Mathf.Clamp01(game.Meter.Fraction) * LaneH;
        if (fill > 0.5f)
        {
            Quad(vh, mx, oy, mx + MeterW, oy + fill, MeterColor(game));
            Quad(vh, mx, oy + fill - 2f, mx + MeterW, oy + fill, WithAlpha(new Color32(255, 246, 220, 255), 0.8f));
        }
    }

    // One note bar centred on y (lane units), clipped to the lane, faded in near the top.
    private static void DrawNote(VertexHelper vh, float ox, float oy, int lane, float y, Color32 c, float alpha)
    {
        if (lane < 0 || lane >= Chart.Lanes)
        {
            return;
        }
        var y0 = Mathf.Max(0f, y - NoteH * 0.5f);
        var y1 = Mathf.Min(LaneH, y + NoteH * 0.5f);
        if (y1 <= y0)
        {
            return;
        }
        var a = alpha * Mathf.Clamp01((LaneH - y) / TopFade);
        if (a <= 0.01f)
        {
            return;
        }
        var x0 = ox + LaneX(lane) + 5f;
        var x1 = ox + LaneX(lane) + LaneW - 5f;
        Quad(vh, x0, oy + y0, x1, oy + y1, WithAlpha(c, a));
        // Me draw light top edge: bar look raised.
        var top = Mathf.Min(y1, y + NoteH * 0.5f);
        var edge = Mathf.Max(y0, top - 3f);
        if (top > edge)
        {
            Quad(vh, x0, oy + edge, x1, oy + top, WithAlpha(Lighten(c), a));
        }
    }

    // 1 just after the moment, 0 after length seconds (or before the moment).
    private static float Fade(float age, float length) => age < 0f || age >= length ? 0f : 1f - age / length;

    private static Color32 WithAlpha(Color32 c, float a)
    {
        c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * a), 0, 255);
        return c;
    }

    private static Color32 Lighten(Color32 c) =>
        new Color32((byte)((c.r + 255) / 2), (byte)((c.g + 255) / 2), (byte)((c.b + 255) / 2), c.a);

    private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c)
    {
        var n = vh.currentVertCount;
        vh.AddVert(new Vector3(x0, y0, 0f), c, Vector2.zero);
        vh.AddVert(new Vector3(x0, y1, 0f), c, Vector2.zero);
        vh.AddVert(new Vector3(x1, y1, 0f), c, Vector2.zero);
        vh.AddVert(new Vector3(x1, y0, 0f), c, Vector2.zero);
        vh.AddTriangle(n, n + 1, n + 2);
        vh.AddTriangle(n + 2, n + 3, n);
    }
}
