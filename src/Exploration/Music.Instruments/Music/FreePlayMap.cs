namespace MC.Exploration.MusicInstrumentsMod;

// Me = free play keys to notes, laid out on the keyboard like a piano (user rule): 29 keys = every semitone from C to
// the E two octaves up. Key index = semitone above the lowest C. Lower octave: white keys on the bottom letter row,
// black keys on the row above where a piano has them; upper octave: white keys on the top letter row, black keys on the
// number row (FreePlayKeys has the key of each index). Holding Space plays everything an octave up. Flute lowest C = C4,
// lyre lowest C = C3. Tambourine: the first four white keys = Thump, Hit, Jingle, Shake (TambourineHit values are its
// "pitches"); its other keys play nothing. Pure C#.
internal static class FreePlayMap
{
    internal const int KeyCount = 29;
    internal const int WhiteCount = 17;
    internal const int OctaveUp = 12;

    private static readonly bool[] BlackInOctave = { false, true, false, true, false, false, true, false, true, false, true, false };
    private static readonly int[] TambourineKeys = { 0, 2, 4, 5 }; // first four white keys: C D E F

    internal static bool IsBlack(int key) => key >= 0 && BlackInOctave[key % 12];

    // White keys left of this key (a black key: the white key it sits after): its place on the drawn piano.
    internal static int WhiteIndex(int key)
    {
        var n = 0;
        for (var k = 0; k < key; k++)
        {
            if (!IsBlack(k))
            {
                n++;
            }
        }
        return IsBlack(key) ? n - 1 : n;
    }

    // MIDI pitch (tambourine: TambourineHit) of a key, -1 = this key plays nothing on this instrument.
    internal static int Pitch(InstrumentKind kind, int key, bool octaveUp)
    {
        if (key < 0 || key >= KeyCount)
        {
            return -1;
        }
        if (kind == InstrumentKind.Tambourine)
        {
            for (var i = 0; i < TambourineKeys.Length; i++)
            {
                if (TambourineKeys[i] == key)
                {
                    return i;
                }
            }
            return -1;
        }
        if (kind != InstrumentKind.Flute && kind != InstrumentKind.Lyre)
        {
            return -1;
        }
        var pitch = (kind == InstrumentKind.Lyre ? 48 : 60) + key + (octaveUp ? OctaveUp : 0);
        return pitch <= 127 ? pitch : -1;
    }

    // Note name for the key ("C4", "F#5"; tambourine: the hit).
    internal static string Name(InstrumentKind kind, int pitch)
    {
        if (pitch < 0)
        {
            return "";
        }
        if (kind == InstrumentKind.Tambourine)
        {
            switch ((TambourineHit)pitch)
            {
                case TambourineHit.Thump:
                    return "Thump";
                case TambourineHit.Hit:
                    return "Hit";
                case TambourineHit.Jingle:
                    return "Jingle";
                default:
                    return "Shake";
            }
        }
        return MusicMath.PitchName(pitch);
    }
}
