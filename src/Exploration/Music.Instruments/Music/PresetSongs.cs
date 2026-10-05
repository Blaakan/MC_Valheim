namespace MC.Exploration.MusicInstrumentsMod;

// One built-in song: melody (flute, lyre), chord line (lyre accompaniment), tambourine rhythm (one or two bars,
// repeated over the whole song). Notation: SongText. Ids are permanent (the song window remembers the last pick).
internal sealed class PresetSong
{
    internal string Id;
    internal string Title;
    internal string Origin;          // shown in the window: "Traditional, Norway" / "Written for this mod"
    internal float Bpm;              // quarter notes per minute
    internal float BeatsPerBar;      // in quarter notes (6/8 = 3)
    internal float PickupBeats;      // beats before the first full bar (rhythm starts after them)
    internal float RhythmLoudness = 1f;
    internal string Melody;
    internal string Chords;
    internal string Rhythm;
}

// Me = the built-in songs. Four traditional tunes in the public domain (anonymous, centuries old or printed before
// 1925; notes taken from published ABC transcriptions: research 2026-10-05, docs/design) and four tunes written for
// this mod. Every bar was checked to add up to its time signature. Never add anything still under copyright (no game
// soundtracks, no modern arrangements).
internal static class PresetSongs
{
    private const string March = "B:1 J:0.5 J:0.5 H:1 J:1 | B:1 J:0.5 J:0.5 H:0.5 H:0.5 S:1";
    private const string Reel = "B:0.5 J:0.5 J:0.5 J:0.5 H:0.5 J:0.5 J:0.5 J:0.5 | B:0.5 J:0.5 H:0.5 J:0.5 S:1 H:1";
    private const string Waltz = "B:1 J:1 J:1 | H:1 J:1 S:1";
    private const string Jig = "B:0.5 J:0.5 J:0.5 H:0.5 J:0.5 J:0.5 | B:0.5 J:0.5 J:0.5 H:1 J:0.5";
    private const string Lull = "S:3 | B:1 S:2";

    internal static readonly PresetSong[] All =
    {
        new PresetSong
        {
            Id = "hearthfire-lullaby",
            Title = "Hearthfire Lullaby",
            Origin = "Written for this mod",
            Bpm = 76f,
            BeatsPerBar = 3f,
            RhythmLoudness = 0.6f,
            Melody =
                "A4:1 C5:1 E5:1 | D5:1.5 C5:0.5 B4:1 | C5:1 A4:0.5 F4:0.5 G4:1 | A4:3 | "
                + "A4:1 C5:1 E5:1 | D5:1.5 C5:0.5 B4:1 | C5:1 A4:0.5 F4:0.5 G4:1 | A4:3 | "
                + "E5:1.5 F5:0.5 E5:1 | D5:1 C5:1 D5:1 | E5:1 C5:1 B4:0.5 G4:0.5 | A4:3 | "
                + "E5:1.5 F5:0.5 E5:1 | D5:1 C5:1 D5:1 | E5:1 C5:1 B4:0.5 G4:0.5 | A4:3 |",
            Chords =
                "Am:3 | G:3 | F:2 G:1 | Am:3 | Am:3 | G:3 | F:2 G:1 | Am:3 | "
                + "Am:3 | Dm:3 | C:2 G:1 | Am:3 | Am:3 | Dm:3 | C:2 G:1 | Am:3 |",
            Rhythm = Lull,
        },
        new PresetSong
        {
            Id = "row-the-longship",
            Title = "Row the Longship",
            Origin = "Written for this mod",
            Bpm = 104f,
            BeatsPerBar = 4f,
            Melody =
                "D4:1.5 D4:0.5 A4:1 A4:1 | C5:1 A4:0.5 G4:0.5 A4:2 | D4:1.5 D4:0.5 F4:1 G4:0.5 A4:0.5 | B4:1 G4:0.5 E4:0.5 D4:2 | "
                + "D4:1.5 D4:0.5 A4:1 A4:1 | C5:1 A4:0.5 G4:0.5 A4:2 | D4:1.5 D4:0.5 F4:1 G4:0.5 A4:0.5 | B4:1 G4:0.5 E4:0.5 D4:2 | "
                + "A4:1 D5:1.5 C5:0.5 D5:1 | E5:1 D5:0.5 C5:0.5 A4:2 | C5:1 A4:0.5 G4:0.5 F4:1 E4:1 | F4:0.5 G4:0.5 E4:1 D4:2 | "
                + "A4:1 D5:1.5 C5:0.5 D5:1 | E5:1 D5:0.5 C5:0.5 A4:2 | C5:1 A4:0.5 G4:0.5 F4:1 E4:1 | F4:0.5 G4:0.5 E4:1 D4:2 |",
            Chords =
                "Dm:4 | Am:4 | Dm:4 | G:2 Dm:2 | Dm:4 | Am:4 | Dm:4 | G:2 Dm:2 | "
                + "Dm:4 | Am:4 | F:2 C:2 | C:2 Dm:2 | Dm:4 | Am:4 | F:2 C:2 | C:2 Dm:2 |",
            Rhythm = March,
        },
        new PresetSong
        {
            Id = "ravens-jig",
            Title = "Raven's Jig",
            Origin = "Written for this mod",
            Bpm = 150f,
            BeatsPerBar = 3f,
            Melody =
                "E4:1 B4:0.5 B4:1 A4:0.5 | B4:0.5 C#5:0.5 D5:0.5 E5:1 B4:0.5 | D5:1 A4:0.5 F#4:1 A4:0.5 | D5:0.5 E5:0.5 D5:0.5 C#5:0.5 A4:0.5 F#4:0.5 | "
                + "E4:1 B4:0.5 B4:1 A4:0.5 | B4:0.5 C#5:0.5 D5:0.5 E5:1 F#5:0.5 | E5:0.5 D5:0.5 B4:0.5 A4:0.5 F#4:0.5 D4:0.5 | E4:1 B3:0.5 E4:1.5 | "
                + "E4:1 B4:0.5 B4:1 A4:0.5 | B4:0.5 C#5:0.5 D5:0.5 E5:1 B4:0.5 | D5:1 A4:0.5 F#4:1 A4:0.5 | D5:0.5 E5:0.5 D5:0.5 C#5:0.5 A4:0.5 F#4:0.5 | "
                + "E4:1 B4:0.5 B4:1 A4:0.5 | B4:0.5 C#5:0.5 D5:0.5 E5:1 F#5:0.5 | E5:0.5 D5:0.5 B4:0.5 A4:0.5 F#4:0.5 D4:0.5 | E4:1 B3:0.5 E4:1.5 | "
                + "E5:1 F#5:0.5 E5:1 D5:0.5 | B4:0.5 A4:0.5 B4:0.5 D5:1 B4:0.5 | E5:1 F#5:0.5 G5:1 F#5:0.5 | E5:0.5 D5:0.5 B4:0.5 A4:1.5 | "
                + "E5:1 F#5:0.5 E5:1 D5:0.5 | B4:0.5 A4:0.5 B4:0.5 D5:1 E5:0.5 | D5:0.5 B4:0.5 A4:0.5 F#4:1 A4:0.5 | E4:1 B3:0.5 E4:1.5 | "
                + "E5:1 F#5:0.5 E5:1 D5:0.5 | B4:0.5 A4:0.5 B4:0.5 D5:1 B4:0.5 | E5:1 F#5:0.5 G5:1 F#5:0.5 | E5:0.5 D5:0.5 B4:0.5 A4:1.5 | "
                + "E5:1 F#5:0.5 E5:1 D5:0.5 | B4:0.5 A4:0.5 B4:0.5 D5:1 E5:0.5 | D5:0.5 B4:0.5 A4:0.5 F#4:1 A4:0.5 | E4:1 B3:0.5 E4:1.5 |",
            Chords =
                "Em:3 | Em:3 | D:3 | D:3 | Em:3 | Em:3 | D:3 | Em:3 | "
                + "Em:3 | Em:3 | D:3 | D:3 | Em:3 | Em:3 | D:3 | Em:3 | "
                + "Em:3 | G:3 | Em:3 | Em:1.5 A:1.5 | Em:3 | G:3 | D:3 | Em:3 | "
                + "Em:3 | G:3 | Em:3 | Em:1.5 A:1.5 | Em:3 | G:3 | D:3 | Em:3 |",
            Rhythm = Jig,
        },
        new PresetSong
        {
            Id = "mead-hall-reel",
            Title = "Mead Hall Reel",
            Origin = "Written for this mod",
            Bpm = 120f,
            BeatsPerBar = 4f,
            Melody =
                "G4:0.5 B4:0.5 D5:0.5 B4:0.5 G4:0.5 B4:0.5 D5:0.5 G5:0.5 | F5:0.5 D5:0.5 C5:0.5 A4:0.5 F4:1 A4:0.5 C5:0.5 | G4:0.5 B4:0.5 D5:0.5 G5:0.5 F5:0.5 D5:0.5 C5:0.5 A4:0.5 | B4:0.5 G4:0.5 A4:0.5 F4:0.5 G4:2 | "
                + "G4:0.5 B4:0.5 D5:0.5 B4:0.5 G4:0.5 B4:0.5 D5:0.5 G5:0.5 | F5:0.5 D5:0.5 C5:0.5 A4:0.5 F4:1 A4:0.5 C5:0.5 | G4:0.5 B4:0.5 D5:0.5 G5:0.5 F5:0.5 D5:0.5 C5:0.5 A4:0.5 | B4:0.5 G4:0.5 A4:0.5 F4:0.5 G4:2 | "
                + "G5:1 F5:0.5 G5:0.5 A5:0.5 G5:0.5 F5:0.5 D5:0.5 | F5:1 E5:0.5 F5:0.5 D5:0.5 C5:0.5 A4:0.5 C5:0.5 | B4:0.5 C5:0.5 D5:0.5 B4:0.5 C5:0.5 A4:0.5 F4:0.5 A4:0.5 | G4:0.5 B4:0.5 A4:0.5 F4:0.5 G4:2 | "
                + "G5:1 F5:0.5 G5:0.5 A5:0.5 G5:0.5 F5:0.5 D5:0.5 | F5:1 E5:0.5 F5:0.5 D5:0.5 C5:0.5 A4:0.5 C5:0.5 | B4:0.5 C5:0.5 D5:0.5 B4:0.5 C5:0.5 A4:0.5 F4:0.5 A4:0.5 | G4:0.5 B4:0.5 A4:0.5 F4:0.5 G4:2 |",
            Chords =
                "G:4 | F:4 | G:2 F:2 | G:1 F:1 G:2 | G:4 | F:4 | G:2 F:2 | G:1 F:1 G:2 | "
                + "G:2 Dm:2 | F:4 | G:2 F:2 | G:1 F:1 G:2 | G:2 Dm:2 | F:4 | G:2 F:2 | G:1 F:1 G:2 |",
            Rhythm = Reel,
        },
        new PresetSong
        {
            Id = "drunken-sailor",
            Title = "Drunken Sailor",
            Origin = "Traditional sea shanty",
            Bpm = 140f,
            BeatsPerBar = 4f,
            Melody =
                "A4:1 A4:0.5 A4:0.5 A4:1 A4:0.5 A4:0.5 | A4:1 D4:1 F4:1 A4:1 | G4:1 G4:0.5 G4:0.5 G4:1 G4:0.5 G4:0.5 | G4:1 C4:1 E4:1 G4:1 | "
                + "A4:1 A4:0.5 A4:0.5 A4:1 A4:0.5 A4:0.5 | A4:1 B4:1 C5:1 D5:1 | C5:1 A4:1 G4:1 E4:1 | D4:2 D4:1 R:1 | "
                + "A4:2 A4:1.5 A4:0.5 | A4:1 D4:1 F4:1 A4:1 | G4:2 G4:1.5 G4:0.5 | G4:1 C4:1 E4:1 G4:1 | "
                + "A4:2 A4:1.5 A4:0.5 | A4:1 B4:1 C5:1 D5:1 | C5:1 A4:1 G4:1 E4:1 | D4:2 D4:1 R:1 |",
            Chords =
                "Dm:4 | Dm:4 | C:4 | C:4 | Dm:4 | Dm:4 | C:4 | Dm:4 | "
                + "Dm:4 | Dm:4 | C:4 | C:4 | Dm:4 | Dm:4 | C:4 | Dm:4 |",
            Rhythm = March,
        },
        new PresetSong
        {
            Id = "greensleeves",
            Title = "Greensleeves",
            Origin = "Traditional, England",
            Bpm = 120f,
            BeatsPerBar = 3f,
            PickupBeats = 1f,
            Melody =
                "A4:1 | "
                + "C5:2 D5:1 | E5:1.5 F#5:0.5 E5:1 | D5:2 B4:1 | G4:1.5 A4:0.5 B4:1 | C5:2 A4:1 | A4:1.5 G#4:0.5 A4:1 | B4:2 G#4:1 | E4:2 A4:1 | "
                + "C5:2 D5:1 | E5:1.5 F#5:0.5 E5:1 | D5:2 B4:1 | G4:1.5 A4:0.5 B4:1 | C5:1.5 B4:0.5 A4:1 | G#4:1.5 F#4:0.5 G#4:1 | A4:3 | A4:3 | "
                + "G5:3 | G5:1.5 F#5:0.5 E5:1 | D5:2 B4:1 | G4:1.5 A4:0.5 B4:1 | C5:2 A4:1 | A4:1.5 G#4:0.5 A4:1 | B4:2 G#4:1 | E4:3 | "
                + "G5:3 | G5:1.5 F#5:0.5 E5:1 | D5:2 B4:1 | G4:1.5 A4:0.5 B4:1 | C5:1.5 B4:0.5 A4:1 | G#4:1.5 F#4:0.5 G#4:1 | A4:3 | A4:2 |",
            Chords =
                "R:1 | Am:3 | D:3 | G:3 | Em:3 | F:3 | F:3 | E:3 | E:3 | Am:3 | D:3 | G:3 | Em:3 | F:3 | E:3 | Am:3 | Am:3 | "
                + "C:3 | C:3 | G:3 | Em:3 | Am:3 | F:3 | E:3 | E:3 | C:3 | C:3 | G:3 | Em:3 | F:3 | E:3 | Am:3 | Am:2 |",
            Rhythm = Waltz,
        },
        new PresetSong
        {
            Id = "vem-kan-segla",
            Title = "Vem kan segla förutan vind",
            Origin = "Traditional, Finland-Swedish",
            Bpm = 96f,
            BeatsPerBar = 4f,
            Melody =
                "E4:1 E4:1 G4:1 F#4:0.5 E4:0.5 | B4:1 B4:1 B4:1.5 R:0.5 | C5:1 C5:1 E5:1 D5:0.5 C5:0.5 | B4:2 B4:1.5 R:0.5 | "
                + "A4:1 A4:1 C5:1 B4:0.5 A4:0.5 | G4:1 G4:1 E4:1.5 R:0.5 | F#4:1 F#4:0.5 F#4:0.5 B3:1 D#4:1 | E4:2 E4:1.5 R:0.5 | "
                + "E4:1 E4:1 G4:1 F#4:0.5 E4:0.5 | B4:1 B4:1 B4:1.5 R:0.5 | C5:1 C5:1 E5:1 D5:0.5 C5:0.5 | B4:2 B4:1.5 R:0.5 | "
                + "A4:1 A4:1 C5:1 B4:0.5 A4:0.5 | G4:1 G4:1 E4:1.5 R:0.5 | F#4:1 F#4:0.5 F#4:0.5 B3:1 D#4:1 | E4:2 E4:1.5 R:0.5 |",
            Chords =
                "Em:4 | Em:4 | Am:4 | Em:4 | Am:4 | Em:4 | B7:4 | Em:4 | "
                + "Em:4 | Em:4 | Am:4 | Em:4 | Am:4 | Em:4 | B7:4 | Em:4 |",
            Rhythm = March,
        },
        new PresetSong
        {
            Id = "kjerringa-med-staven",
            Title = "Kjerringa med staven",
            Origin = "Traditional, Norway",
            Bpm = 120f,
            BeatsPerBar = 3f,
            Melody =
                "D5:1 D5:1 B4:0.75 B4:0.25 | D5:2 B4:1 | D5:1 D5:0.75 D5:0.25 B4:0.75 B4:0.25 | D5:2 B4:1 | "
                + "G4:0.75 A4:0.25 B4:0.75 C5:0.25 D5:0.75 B4:0.25 | A4:0.75 B4:0.25 C5:0.75 D5:0.25 B4:1 | "
                + "G4:0.75 A4:0.25 B4:0.75 C5:0.25 D5:0.75 B4:0.25 | A4:0.75 B4:0.25 C5:0.75 D5:0.25 B4:1 | "
                + "G4:1 B4:1 A4:0.75 F#4:0.25 | G4:2 G4:1 |",
            Chords = "G:3 | G:3 | G:3 | G:3 | G:3 | D:3 | G:3 | D:3 | G:2 D:1 | G:3 |",
            Rhythm = Waltz,
        },
    };
}
