using System;
using System.Collections.Generic;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = turn song notes into what one instrument can play.
//   Flute: one note at a time (highest wins: the tune sits on top), moved by whole octaves into its range.
//   Lyre: up to MaxLyreChord notes struck together (top and bottom kept first), moved into its range.
//   Tambourine: hits, not tones. From a drum part: General MIDI drum -> kind of hit. From tones: one hit per onset
//   (bass note = thump, loud = hit, else jingle, long held note alone = shake), never more than ~7 hits a second.
// Pure C#, no Unity.
internal static class Arranger
{
    internal const int FluteLow = 62;   // D4
    internal const int FluteHigh = 93;  // A6
    internal const int FluteCentre = 76;
    internal const int LyreLow = 45;    // A2
    internal const int LyreHigh = 84;   // C6
    internal const int LyreCentre = 62;
    internal const int MaxLyreChord = 4;
    internal const float ChordWindow = 0.035f;   // notes starting this close = struck together
    internal const float MinFluteNote = 0.06f;
    internal const float MinTambourineGap = 0.14f;
    internal const float MinDrumGap = 0.07f;

    // notes: any order. percussion: notes are General MIDI drums (MIDI channel 10) or TambourineHit (preset line).
    // presetHits: percussion notes are already TambourineHit.
    internal static Note[] Arrange(IList<Note> notes, InstrumentKind kind, bool percussion, bool presetHits)
    {
        var list = new List<Note>(notes);
        list.Sort();
        switch (kind)
        {
            case InstrumentKind.Flute:
                return percussion ? Array.Empty<Note>() : Flute(list);
            case InstrumentKind.Lyre:
                return percussion ? Array.Empty<Note>() : Lyre(list);
            case InstrumentKind.Tambourine:
                if (percussion)
                {
                    return presetHits ? Thin(list, MinDrumGap) : FromDrums(list);
                }
                return FromTones(list);
            default:
                return Array.Empty<Note>();
        }
    }

    private static Note[] Flute(List<Note> sorted)
    {
        // Skyline: per onset group keep the highest; a held note is cut when the next one starts.
        var mono = new List<Note>(sorted.Count);
        var i = 0;
        while (i < sorted.Count)
        {
            var best = sorted[i];
            var j = i + 1;
            while (j < sorted.Count && sorted[j].Time - sorted[i].Time < ChordWindow)
            {
                if (sorted[j].Pitch > best.Pitch)
                {
                    best = sorted[j];
                }
                j++;
            }
            best.Time = sorted[i].Time;
            mono.Add(best);
            i = j;
        }
        for (var k = 0; k + 1 < mono.Count; k++)
        {
            var n = mono[k];
            var gap = mono[k + 1].Time - n.Time;
            if (n.Length > gap)
            {
                // Short breath between notes, like a player tonguing the next one.
                n.Length = Math.Max(MinFluteNote, gap - Math.Min(0.02f, gap * 0.1f));
                mono[k] = n;
            }
        }
        return Fold(mono, FluteLow, FluteHigh, FluteCentre).ToArray();
    }

    private static Note[] Lyre(List<Note> sorted)
    {
        var folded = Fold(sorted, LyreLow, LyreHigh, LyreCentre);
        folded.Sort();
        var result = new List<Note>(folded.Count);
        var group = new List<Note>(8);
        var i = 0;
        while (i < folded.Count)
        {
            group.Clear();
            var j = i;
            while (j < folded.Count && folded[j].Time - folded[i].Time < ChordWindow)
            {
                group.Add(folded[j]);
                j++;
            }
            // Same pitch twice (octave folding, doubled parts): once.
            group.Sort((a, b) => a.Pitch.CompareTo(b.Pitch));
            for (var k = group.Count - 1; k > 0; k--)
            {
                if (group[k].Pitch == group[k - 1].Pitch)
                {
                    var keep = group[k].Velocity >= group[k - 1].Velocity ? group[k] : group[k - 1];
                    group[k - 1] = keep;
                    group.RemoveAt(k);
                }
            }
            if (group.Count > MaxLyreChord)
            {
                // Bottom and top first, then the highest of the middle.
                var kept = new List<Note> { group[0], group[group.Count - 1] };
                for (var k = group.Count - 2; k > 0 && kept.Count < MaxLyreChord; k--)
                {
                    kept.Add(group[k]);
                }
                group.Clear();
                group.AddRange(kept);
            }
            foreach (var n in group)
            {
                var copy = n;
                copy.Time = folded[i].Time;
                result.Add(copy);
            }
            i = j;
        }
        result.Sort();
        return result.ToArray();
    }

    // Whole song moved by the octave that fits most notes into [low, high] (tie: mean nearest the centre); notes still
    // outside are moved alone by octaves.
    private static List<Note> Fold(List<Note> notes, int low, int high, int centre)
    {
        if (notes.Count == 0)
        {
            return notes;
        }
        var bestShift = 0;
        var bestInside = -1;
        var bestDistance = double.MaxValue;
        for (var shift = -48; shift <= 48; shift += 12)
        {
            var inside = 0;
            var sum = 0.0;
            foreach (var n in notes)
            {
                var p = n.Pitch + shift;
                if (p >= low && p <= high)
                {
                    inside++;
                }
                sum += p;
            }
            var distance = Math.Abs(sum / notes.Count - centre);
            if (inside > bestInside || (inside == bestInside && distance < bestDistance))
            {
                bestInside = inside;
                bestDistance = distance;
                bestShift = shift;
            }
        }
        var result = new List<Note>(notes.Count);
        foreach (var n in notes)
        {
            var p = n.Pitch + bestShift;
            while (p < low)
            {
                p += 12;
            }
            while (p > high)
            {
                p -= 12;
            }
            var copy = n;
            copy.Pitch = (byte)p;
            result.Add(copy);
        }
        return result;
    }

    // General MIDI drum kit -> tambourine hit; one hit per onset (strongest kind wins), MinDrumGap apart.
    private static Note[] FromDrums(List<Note> sorted)
    {
        var hits = new List<Note>(sorted.Count);
        foreach (var n in sorted)
        {
            var hit = DrumToHit(n.Pitch, n.Velocity);
            var length = LongDrum(n.Pitch) ? 0.8f : n.Length;
            hits.Add(new Note(n.Time, HitLength(hit, length), (byte)hit, n.Velocity));
        }
        return Thin(hits, MinDrumGap);
    }

    // General MIDI drum note (+ loudness) -> tambourine hit. Kick, low toms = thump; snare, clap, high toms, loud
    // tambourine = hit; crashes, open hi-hat, shakers = shake (crash long); the rest (hi-hats, ride, tambourine,
    // clicks) = jingle.
    internal static TambourineHit DrumToHit(int drum, int velocity = 80)
    {
        switch (drum)
        {
            case 35:
            case 36:
            case 41:
            case 43:
            case 45:
                return TambourineHit.Thump;
            case 38:
            case 39:
            case 40:
            case 47:
            case 48:
            case 50:
                return TambourineHit.Hit;
            case 54:
                return velocity >= 100 ? TambourineHit.Hit : TambourineHit.Jingle;
            case 46:
            case 49:
            case 52:
            case 55:
            case 57:
            case 69:
            case 70:
            case 82:
                return TambourineHit.Shake;
            default:
                return TambourineHit.Jingle;
        }
    }

    // Crash cymbals ring: their shake lasts longer.
    private static bool LongDrum(int drum) => drum == 49 || drum == 52 || drum == 55 || drum == 57;

    // Tones -> hits: one per onset group.
    private static Note[] FromTones(List<Note> sorted)
    {
        var hits = new List<Note>();
        var i = 0;
        while (i < sorted.Count)
        {
            var j = i;
            var low = 127;
            var loud = 0;
            var longest = 0f;
            while (j < sorted.Count && sorted[j].Time - sorted[i].Time < ChordWindow)
            {
                low = Math.Min(low, sorted[j].Pitch);
                loud = Math.Max(loud, sorted[j].Velocity);
                longest = Math.Max(longest, sorted[j].Length);
                j++;
            }
            var next = j < sorted.Count ? sorted[j].Time : sorted[i].Time + longest;
            TambourineHit hit;
            if (longest >= 1.2f && next - sorted[i].Time >= 1.0f)
            {
                hit = TambourineHit.Shake;
            }
            else if (low < 48)
            {
                hit = TambourineHit.Thump;
            }
            else if (loud >= 92)
            {
                hit = TambourineHit.Hit;
            }
            else
            {
                hit = TambourineHit.Jingle;
            }
            hits.Add(new Note(sorted[i].Time, HitLength(hit, Math.Min(longest, next - sorted[i].Time)), (byte)hit,
                (byte)Math.Max(1, loud)));
            i = j;
        }
        return Thin(hits, MinTambourineGap);
    }

    private static float HitLength(TambourineHit hit, float noteLength)
    {
        switch (hit)
        {
            case TambourineHit.Shake:
                return MusicMath.Clamp(noteLength, 0.25f, 2.5f);
            case TambourineHit.Thump:
                return 0.4f;
            case TambourineHit.Hit:
                return 0.3f;
            default:
                return 0.18f;
        }
    }

    // One hit per onset group (strongest wins: thump, hit, shake, jingle) and at least gap seconds apart.
    private static Note[] Thin(List<Note> sorted, float gap)
    {
        var result = new List<Note>(sorted.Count);
        var i = 0;
        var last = float.NegativeInfinity;
        while (i < sorted.Count)
        {
            var best = sorted[i];
            var j = i + 1;
            while (j < sorted.Count && sorted[j].Time - sorted[i].Time < ChordWindow)
            {
                if (Rank(sorted[j].Pitch) < Rank(best.Pitch) || (sorted[j].Pitch == best.Pitch && sorted[j].Velocity > best.Velocity))
                {
                    best = sorted[j];
                }
                j++;
            }
            if (sorted[i].Time - last >= gap)
            {
                best.Time = sorted[i].Time;
                result.Add(best);
                last = sorted[i].Time;
            }
            i = j;
        }
        return result.ToArray();
    }

    private static int Rank(byte hit)
    {
        switch ((TambourineHit)hit)
        {
            case TambourineHit.Thump: return 0;
            case TambourineHit.Hit: return 1;
            case TambourineHit.Shake: return 2;
            default: return 3;
        }
    }
}
