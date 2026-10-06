using System;
using System.Collections.Generic;

namespace MC.Exploration.MusicInstrumentsMod;

// Which instrument. Byte go on wire and in player ZDO: never renumber, only add.
internal enum InstrumentKind : byte
{
    None = 0,
    Flute = 1,
    Lyre = 2,
    Tambourine = 3,
}

// Tambourine "pitch" = kind of hit, not a tone. Same byte go in Note.Pitch and on wire: never renumber.
internal enum TambourineHit : byte
{
    Thump = 0,  // low hit in the middle of the skin, jingles shiver (B)
    Hit = 1,    // palm hit near the rim, jingles ring: the accent (H)
    Jingle = 2, // jingles only, one short shake (J)
    Shake = 3,  // long jingle roll for the note length (S)
}

// One note: start and length in seconds from song start, MIDI pitch (60 = middle C; tambourine = TambourineHit),
// loudness 1..127. Plain struct: songs are arrays of it.
internal struct Note : IComparable<Note>
{
    internal float Time;
    internal float Length;
    internal byte Pitch;
    internal byte Velocity;

    internal Note(float time, float length, byte pitch, byte velocity)
    {
        Time = time;
        Length = length;
        Pitch = pitch;
        Velocity = velocity;
    }

    internal float End => Time + Length;

    // Time first, then low pitch first (chords stay in same order everywhere).
    public int CompareTo(Note other)
    {
        var c = Time.CompareTo(other.Time);
        return c != 0 ? c : Pitch.CompareTo(other.Pitch);
    }

    public override string ToString() => $"{Time:0.###}s +{Length:0.###} p{Pitch} v{Velocity}";
}

internal enum SongSource : byte
{
    Preset = 0,
    Midi = 1,     // MIDI file of the player's own songs folder
    Server = 2,   // MIDI file the server shares (SongShare): read from its folder on the server's game, else downloaded
}

// Small helpers shared by music code (no Unity here: offline tests compile these files).
internal static class MusicMath
{
    // MIDI pitch -> Hz (A4 = 69 = 440 Hz).
    internal static double PitchToHz(double pitch) => 440.0 * Math.Pow(2.0, (pitch - 69.0) / 12.0);

    private static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    internal static string PitchName(int pitch)
    {
        if (pitch < 0 || pitch > 127)
        {
            return "?";
        }
        return Names[pitch % 12] + (pitch / 12 - 1);
    }

    internal static byte ClampByte(int v, int min, int max) => (byte)(v < min ? min : v > max ? max : v);

    // FNV-1a 32-bit of the bytes: song id and check of a downloaded server song (same on every game).
    internal static int Fnv1a(byte[] data)
    {
        unchecked
        {
            var hash = 2166136261u;
            if (data != null)
            {
                foreach (var b in data)
                {
                    hash = (hash ^ b) * 16777619u;
                }
            }
            return (int)hash;
        }
    }

    internal static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

    // Copy of notes [from, to) as new list (helpers build arrays from lists).
    internal static List<Note> Slice(Note[] notes, int from, int to)
    {
        var list = new List<Note>(Math.Max(0, to - from));
        for (var i = from; i < to; i++)
        {
            list.Add(notes[i]);
        }
        return list;
    }
}
