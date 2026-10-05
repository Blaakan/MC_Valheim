using System;
using System.Collections.Generic;
using System.Globalization;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = tiny text notation of the built-in songs. Tokens split by spaces, "|" bar lines skipped:
//   C4:1      pitch (C D E F G A B, then # or b, then octave; C4 = middle C = MIDI 60) and length in beats
//   C3+G3:2   chord: notes joined by +
//   R:0.5     rest
//   B H J S   tambourine hits in a percussion line: B thump, H hit, J jingle, S shake (lasts its length), e.g. H:0.5
//   ! after the name = accent (louder), e.g. D5!:1 or H!:0.5
// Chord lines (lyre accompaniment, TryParseChords): chord symbols with lengths, e.g. Dm:4 G:2 B7:4 Am7:3 R:1. Chord =
// root in octave 3 + third + fifth (+ seventh), strummed low to high a few ms apart, soft.
// Beat = quarter note. First beat of each bar a bit louder by itself (beatsPerBar).
// Pure C#, no Unity. Bad token = error text with its position, nothing half parsed.
internal static class SongText
{
    internal const byte Soft = 72;
    internal const byte Normal = 84;
    internal const byte Downbeat = 96;
    internal const byte Accent = 112;
    private static readonly char[] Separators = { ' ', '\t', '\r', '\n' };

    internal static bool TryParse(string text, float bpm, float beatsPerBar, bool percussion, List<Note> into,
        out float lengthSeconds, out string error)
    {
        error = null;
        lengthSeconds = 0f;
        if (bpm <= 0f || float.IsNaN(bpm))
        {
            error = "tempo must be above 0";
            return false;
        }
        var secondsPerBeat = 60.0 / bpm;
        var beat = 0.0;
        var parsed = new List<Note>();
        var tokens = (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token == "|")
            {
                continue;
            }
            var colon = token.LastIndexOf(':');
            if (colon <= 0 || colon == token.Length - 1)
            {
                error = $"token {i + 1} \"{token}\" has no length (write it as Name:beats)";
                return false;
            }
            if (!double.TryParse(token.Substring(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var beats)
                || beats <= 0 || beats > 64)
            {
                error = $"token {i + 1} \"{token}\" has a bad length";
                return false;
            }
            var head = token.Substring(0, colon);
            var accent = head.EndsWith("!", StringComparison.Ordinal);
            if (accent)
            {
                head = head.Substring(0, head.Length - 1);
            }
            if (head != "R")
            {
                var onBarStart = beatsPerBar > 0 && Math.Abs(beat / beatsPerBar - Math.Round(beat / beatsPerBar)) < 1e-6;
                var velocity = accent ? Accent : onBarStart ? Downbeat : Normal;
                var time = (float)(beat * secondsPerBeat);
                var length = (float)(beats * secondsPerBeat);
                foreach (var name in head.Split('+'))
                {
                    if (!TryPitch(name, percussion, out var pitch))
                    {
                        error = $"token {i + 1} \"{token}\" has an unknown {(percussion ? "hit" : "note")} \"{name}\"";
                        return false;
                    }
                    parsed.Add(new Note(time, length, pitch, velocity));
                }
            }
            beat += beats;
        }
        into.AddRange(parsed);
        lengthSeconds = (float)(beat * secondsPerBeat);
        return true;
    }

    // "C4" -> 60, "F#5" -> 78, "Bb3" -> 58; percussion: B T S L -> TambourineHit.
    internal static bool TryPitch(string name, bool percussion, out byte pitch)
    {
        pitch = 0;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        if (percussion)
        {
            switch (name)
            {
                case "B":
                    pitch = (byte)TambourineHit.Thump;
                    return true;
                case "H":
                    pitch = (byte)TambourineHit.Hit;
                    return true;
                case "J":
                    pitch = (byte)TambourineHit.Jingle;
                    return true;
                case "S":
                    pitch = (byte)TambourineHit.Shake;
                    return true;
                default:
                    return false;
            }
        }
        int step;
        switch (name[0])
        {
            case 'C': step = 0; break;
            case 'D': step = 2; break;
            case 'E': step = 4; break;
            case 'F': step = 5; break;
            case 'G': step = 7; break;
            case 'A': step = 9; break;
            case 'B': step = 11; break;
            default: return false;
        }
        var p = 1;
        if (p < name.Length && (name[p] == '#' || name[p] == 'b'))
        {
            step += name[p] == '#' ? 1 : -1;
            p++;
        }
        if (p >= name.Length || !int.TryParse(name.Substring(p), NumberStyles.Integer, CultureInfo.InvariantCulture, out var octave)
            || octave < 0 || octave > 8)
        {
            return false;
        }
        var midi = (octave + 1) * 12 + step;
        if (midi < 0 || midi > 127)
        {
            return false;
        }
        pitch = (byte)midi;
        return true;
    }

    internal const float StrumSeconds = 0.012f;

    // Chord line: Name:beats tokens (Dm, G, B7, Am7, F#m, Bb...), R:beats rest, | skipped.
    internal static bool TryParseChords(string text, float bpm, List<Note> into, out float lengthSeconds, out string error)
    {
        error = null;
        lengthSeconds = 0f;
        if (bpm <= 0f || float.IsNaN(bpm))
        {
            error = "tempo must be above 0";
            return false;
        }
        var secondsPerBeat = 60.0 / bpm;
        var beat = 0.0;
        var parsed = new List<Note>();
        var tokens = (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var chord = new List<int>(4);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token == "|")
            {
                continue;
            }
            var colon = token.LastIndexOf(':');
            if (colon <= 0 || colon == token.Length - 1
                || !double.TryParse(token.Substring(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var beats)
                || beats <= 0 || beats > 64)
            {
                error = $"chord token {i + 1} \"{token}\" has no valid length (write it as Name:beats)";
                return false;
            }
            var name = token.Substring(0, colon);
            if (name != "R")
            {
                if (!TryChord(name, chord))
                {
                    error = $"chord token {i + 1} \"{token}\" is not a chord (C, Dm, G7, Am7, F#m, Bb...)";
                    return false;
                }
                var time = beat * secondsPerBeat;
                var length = (float)(beats * secondsPerBeat);
                for (var k = 0; k < chord.Count; k++)
                {
                    parsed.Add(new Note((float)(time + k * StrumSeconds), Math.Max(0.05f, length - k * StrumSeconds),
                        (byte)chord[k], Soft));
                }
            }
            beat += beats;
        }
        into.AddRange(parsed);
        lengthSeconds = (float)(beat * secondsPerBeat);
        return true;
    }

    // "Dm" -> D3 F3 A3; "B7" -> B3 D#4 F#4 A4; root always in octave 3, notes low to high.
    internal static bool TryChord(string name, List<int> notes)
    {
        notes.Clear();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        int step;
        switch (name[0])
        {
            case 'C': step = 0; break;
            case 'D': step = 2; break;
            case 'E': step = 4; break;
            case 'F': step = 5; break;
            case 'G': step = 7; break;
            case 'A': step = 9; break;
            case 'B': step = 11; break;
            default: return false;
        }
        var p = 1;
        if (p < name.Length && (name[p] == '#' || name[p] == 'b'))
        {
            step += name[p] == '#' ? 1 : -1;
            p++;
        }
        var quality = name.Substring(p);
        int third;
        var seventh = false;
        switch (quality)
        {
            case "": third = 4; break;
            case "m": third = 3; break;
            case "7": third = 4; seventh = true; break;
            case "m7": third = 3; seventh = true; break;
            default: return false;
        }
        var root = 48 + ((step % 12) + 12) % 12;
        notes.Add(root);
        notes.Add(root + third);
        notes.Add(root + 7);
        if (seventh)
        {
            notes.Add(root + 10);
        }
        return true;
    }
}
