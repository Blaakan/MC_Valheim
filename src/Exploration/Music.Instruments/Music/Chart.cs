using System;
using System.Collections.Generic;

namespace MC.Exploration.MusicInstrumentsMod;

// One note of the mini-game: time (seconds from song start), lane 0..Lanes-1, and the song notes that sound when it
// is hit: arranged notes [First, First + Count). Notes the chart left out (too dense to play) ride along with the
// chart note before them, so a clean run plays the whole song.
internal struct ChartNote
{
    internal float Time;
    internal byte Lane;
    internal int First;
    internal int Count;
}

// Me = mini-game chart of an arranged song. Lanes follow the tune: higher note = lane more to the right, inside a
// window of nearby chart notes (so a high passage still uses all lanes); the same pitch again keeps its lane.
// Tambourine: lane = kind of hit (thump, tap, shake, roll). Pure C#, no Unity.
internal sealed class Chart
{
    internal const int Lanes = 4;
    internal const float DefaultMinGap = 0.22f;   // chart notes at least this far apart (seconds)
    private const int LaneWindow = 6;             // chart notes before and after used to place a lane

    internal ChartNote[] Notes = Array.Empty<ChartNote>();
    internal Note[] Song = Array.Empty<Note>();   // arranged notes the chart points into
    internal float Length;                        // song length (seconds)

    internal static Chart Build(Note[] arranged, InstrumentKind kind, float songLength, float minGap = DefaultMinGap)
    {
        var chart = new Chart { Song = arranged ?? Array.Empty<Note>(), Length = songLength };
        var notes = chart.Song;
        if (notes.Length == 0)
        {
            return chart;
        }

        // Onset groups [start, end) of the sorted arranged notes; keep a group when it is minGap after the last kept.
        var kept = new List<int>();     // group start index
        var top = new List<int>();      // highest pitch (tambourine: strongest hit) of the kept group
        var lastTime = float.NegativeInfinity;
        var i = 0;
        while (i < notes.Length)
        {
            var j = i + 1;
            int high = notes[i].Pitch;
            while (j < notes.Length && notes[j].Time - notes[i].Time < Arranger.ChordWindow)
            {
                high = kind == InstrumentKind.Tambourine ? Math.Min(high, notes[j].Pitch) : Math.Max(high, notes[j].Pitch);
                j++;
            }
            if (notes[i].Time - lastTime >= minGap)
            {
                kept.Add(i);
                top.Add(high);
                lastTime = notes[i].Time;
            }
            i = j;
        }

        var result = new ChartNote[kept.Count];
        for (var k = 0; k < kept.Count; k++)
        {
            var first = kept[k];
            var end = k + 1 < kept.Count ? kept[k + 1] : notes.Length;
            result[k] = new ChartNote
            {
                Time = notes[first].Time,
                First = first,
                Count = end - first,
                Lane = kind == InstrumentKind.Tambourine ? (byte)Math.Min(Lanes - 1, top[k]) : (byte)0,
            };
        }
        if (kind != InstrumentKind.Tambourine)
        {
            PlaceLanes(result, top);
        }
        chart.Notes = result;
        return chart;
    }

    private static void PlaceLanes(ChartNote[] chart, List<int> pitch)
    {
        for (var k = 0; k < chart.Length; k++)
        {
            if (k > 0 && pitch[k] == pitch[k - 1])
            {
                chart[k].Lane = chart[k - 1].Lane;
                continue;
            }
            int lo = int.MaxValue, hi = int.MinValue;
            var from = Math.Max(0, k - LaneWindow);
            var to = Math.Min(chart.Length - 1, k + LaneWindow);
            for (var w = from; w <= to; w++)
            {
                lo = Math.Min(lo, pitch[w]);
                hi = Math.Max(hi, pitch[w]);
            }
            int lane;
            if (hi <= lo)
            {
                lane = k > 0 ? chart[k - 1].Lane : 1;
            }
            else
            {
                lane = (int)Math.Round((pitch[k] - lo) / (double)(hi - lo) * (Lanes - 1));
            }
            // Moving up the scale never moves left (and down never right) from the last lane.
            if (k > 0)
            {
                var prev = chart[k - 1].Lane;
                if (pitch[k] > pitch[k - 1] && lane < prev)
                {
                    lane = Math.Min(Lanes - 1, prev + 1);
                }
                else if (pitch[k] < pitch[k - 1] && lane > prev)
                {
                    lane = Math.Max(0, prev - 1);
                }
                else if (lane == prev)
                {
                    // Different pitch on the same lane reads as "same note": step one lane the right way.
                    lane = pitch[k] > pitch[k - 1] ? Math.Min(Lanes - 1, prev + 1) : Math.Max(0, prev - 1);
                }
            }
            chart[k].Lane = (byte)Math.Max(0, Math.Min(Lanes - 1, lane));
        }
    }
}

