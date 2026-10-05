using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MC.Exploration.MusicInstrumentsMod;

// One entry of the song window: a built-in song or a MIDI file of the songs folder.
internal sealed class SongEntry
{
    internal string Id = "";            // "preset:<id>" / "midi:<file name>": stable key
    internal string Title = "";
    internal SongSource Source;
    internal string Path;               // MIDI file (null for presets)
    internal string Info = "";          // one line for the window: origin or parts, length
    internal string Error;              // null = fine; else why it cannot play
    internal float Length;              // seconds of one pass (0 until a MIDI file is loaded)
    internal readonly List<string> Parts = new List<string>(); // MIDI parts (after Load); presets: none
    internal PresetSong Preset;
    internal MidiScore Score;
    internal DateTime FileTime;
    internal long FileSize;
    internal bool Loaded;
}

// Me = the songs: built-in ones (PresetSongs) and the MIDI files of the songs folder (Plugin.SongsFolder, default
// BepInEx/config/MC_Valheim/Songs). Files are only listed when the window opens; a file is read when picked (cached
// while its time and size stay the same). Arrange turns a song into the notes one instrument plays:
//   preset  flute = melody; lyre = melody + chord line; tambourine = the rhythm repeated over the song
//   MIDI    one part (track x channel) or the automatic pick: flute = the part that looks most like a tune (many notes,
//           mostly one at a time, high, named melody/lead/flute...); lyre = that tune + a bass line from the lowest
//           other part; tambourine = the drum parts (General MIDI channel 10), else hits on the tune's rhythm.
// Leading silence of a MIDI file is cut to LeadSilence. Pure C# (no Unity): offline tests run it.
internal static class SongLibrary
{
    internal const float LeadSilence = 0.3f;
    internal const float TailSilence = 0.3f;
    internal const int MaxFiles = 500;
    internal const string AllParts = "All parts";
    private static readonly string[] Extensions = { ".mid", ".midi", ".kar", ".rmi" };

    private static List<SongEntry> _presets;
    private static readonly Dictionary<string, SongEntry> MidiCache = new Dictionary<string, SongEntry>(StringComparer.OrdinalIgnoreCase);

    // Default folder when the setting is empty (BepInEx config folder; set by Plugin, offline tests set their own).
    internal static string DefaultFolder = "";
    internal static string ConfiguredFolder = "";

    internal static IReadOnlyList<SongEntry> Presets => _presets ??= BuildPresets();

    internal static string Folder
    {
        get
        {
            var configured = (ConfiguredFolder ?? "").Trim();
            return configured.Length > 0 ? configured : DefaultFolder;
        }
    }

    private static List<SongEntry> BuildPresets()
    {
        var list = new List<SongEntry>();
        foreach (var p in PresetSongs.All)
        {
            var entry = new SongEntry
            {
                Id = "preset:" + p.Id,
                Title = p.Title,
                Source = SongSource.Preset,
                Preset = p,
                Loaded = true,
            };
            var melody = new List<Note>();
            if (!SongText.TryParse(p.Melody, p.Bpm, p.BeatsPerBar, false, melody, out var length, out var error))
            {
                entry.Error = "melody: " + error;
            }
            entry.Length = length;
            entry.Info = p.Origin + ", " + Clock(length);
            list.Add(entry);
        }
        return list;
    }

    // MIDI files of the folder, by name (folder made when missing). Known files keep their parsed data.
    internal static List<SongEntry> ScanMidiFolder(out string error)
    {
        error = null;
        var list = new List<SongEntry>();
        var folder = Folder;
        if (string.IsNullOrEmpty(folder))
        {
            error = "No songs folder.";
            return list;
        }
        try
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            var files = new List<string>();
            foreach (var file in Directory.GetFiles(folder))
            {
                var ext = System.IO.Path.GetExtension(file);
                foreach (var allowed in Extensions)
                {
                    if (string.Equals(ext, allowed, StringComparison.OrdinalIgnoreCase))
                    {
                        files.Add(file);
                        break;
                    }
                }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            if (files.Count > MaxFiles)
            {
                error = $"Only the first {MaxFiles} songs of the folder are listed.";
                files.RemoveRange(MaxFiles, files.Count - MaxFiles);
            }
            foreach (var file in files)
            {
                var name = System.IO.Path.GetFileName(file);
                if (!MidiCache.TryGetValue(file, out var entry))
                {
                    entry = new SongEntry
                    {
                        Id = "midi:" + name,
                        Title = System.IO.Path.GetFileNameWithoutExtension(file),
                        Source = SongSource.Midi,
                        Path = file,
                        Info = "MIDI file",
                    };
                    MidiCache[file] = entry;
                }
                list.Add(entry);
            }
        }
        catch (Exception e)
        {
            error = "Could not read the songs folder: " + e.Message;
        }
        return list;
    }

    // Read and parse a MIDI entry (again only when the file changed). Fills Parts, Length, Info, Error.
    internal static bool Load(SongEntry entry)
    {
        if (entry == null)
        {
            return false;
        }
        if (entry.Source == SongSource.Preset)
        {
            return entry.Error == null;
        }
        try
        {
            var info = new FileInfo(entry.Path);
            if (!info.Exists)
            {
                entry.Error = "The file is gone.";
                entry.Loaded = false;
                return false;
            }
            if (entry.Loaded && info.LastWriteTimeUtc == entry.FileTime && info.Length == entry.FileSize)
            {
                return entry.Error == null;
            }
            entry.FileTime = info.LastWriteTimeUtc;
            entry.FileSize = info.Length;
            entry.Loaded = true;
            entry.Parts.Clear();
            entry.Score = null;
            if (info.Length > MidiReader.MaxFileBytes)
            {
                entry.Error = "The file is larger than " + MidiReader.MaxFileBytes / (1024 * 1024) + " MB.";
                return false;
            }
            var score = MidiReader.Read(File.ReadAllBytes(entry.Path), out var error);
            if (score == null)
            {
                entry.Error = "The file " + error + ".";
                return false;
            }
            entry.Error = null;
            entry.Score = score;
            foreach (var part in score.Parts)
            {
                entry.Parts.Add(part.Label + " - " + part.Notes.Count.ToString(CultureInfo.InvariantCulture) + " notes");
            }
            entry.Length = Trimmed(score);
            entry.Info = "MIDI, " + score.Parts.Count + (score.Parts.Count == 1 ? " part, " : " parts, ") + Clock(entry.Length)
                         + (score.Truncated ? " (damaged end skipped)" : "");
            return true;
        }
        catch (Exception e)
        {
            entry.Error = "The file could not be read: " + e.Message;
            return false;
        }
    }

    // Automatic part for this instrument: index into entry.Parts, -1 = several parts (lyre tune + bass, tambourine
    // drums or rhythm). Presets: -1.
    internal static int AutoPart(SongEntry entry, InstrumentKind kind)
    {
        if (entry?.Score == null)
        {
            return -1;
        }
        if (kind == InstrumentKind.Flute)
        {
            return BestMelody(entry.Score);
        }
        return -1;
    }

    // Notes this instrument plays (arranged, sorted, from 0) and one pass length. part: -1 = automatic.
    internal static bool Arrange(SongEntry entry, InstrumentKind kind, int part, out Note[] notes, out float length,
        out string error)
    {
        notes = Array.Empty<Note>();
        length = 0f;
        error = null;
        if (entry == null)
        {
            error = "No song.";
            return false;
        }
        if (entry.Source == SongSource.Preset)
        {
            return ArrangePreset(entry, kind, out notes, out length, out error);
        }
        if (!Load(entry) || entry.Score == null)
        {
            error = entry.Error ?? "The file could not be read.";
            return false;
        }
        var score = entry.Score;
        var source = new List<Note>();
        var percussion = false;
        if (part >= 0 && part < score.Parts.Count)
        {
            var chosen = score.Parts[part];
            if (chosen.Drums && kind != InstrumentKind.Tambourine)
            {
                error = "That part is drums: pick another part for the " + InstrumentName(kind) + ".";
                return false;
            }
            source.AddRange(chosen.Notes);
            percussion = chosen.Drums;
        }
        else
        {
            switch (kind)
            {
                case InstrumentKind.Flute:
                {
                    var best = BestMelody(score);
                    if (best < 0)
                    {
                        error = "This file has no tune, only drums.";
                        return false;
                    }
                    source.AddRange(score.Parts[best].Notes);
                    break;
                }
                case InstrumentKind.Lyre:
                {
                    var best = BestMelody(score);
                    if (best < 0)
                    {
                        error = "This file has no tune, only drums.";
                        return false;
                    }
                    source.AddRange(score.Parts[best].Notes);
                    var bass = BassPart(score, best);
                    if (bass >= 0)
                    {
                        source.AddRange(BassLine(score.Parts[bass].Notes));
                    }
                    break;
                }
                default:
                {
                    foreach (var p in score.Parts)
                    {
                        if (p.Drums)
                        {
                            source.AddRange(p.Notes);
                            percussion = true;
                        }
                    }
                    if (!percussion)
                    {
                        var best = BestMelody(score);
                        if (best >= 0)
                        {
                            source.AddRange(score.Parts[best].Notes);
                        }
                    }
                    break;
                }
            }
        }
        if (source.Count == 0)
        {
            error = "No notes to play.";
            return false;
        }
        var arranged = Arranger.Arrange(source, kind, percussion, presetHits: false);
        if (arranged.Length == 0)
        {
            error = "Nothing in this part suits the " + InstrumentName(kind) + ".";
            return false;
        }
        // Leading silence cut.
        var shift = arranged[0].Time - LeadSilence;
        var end = 0f;
        for (var i = 0; i < arranged.Length; i++)
        {
            if (shift > 0f)
            {
                arranged[i].Time -= shift;
            }
            end = Math.Max(end, arranged[i].End);
        }
        notes = arranged;
        length = end + TailSilence;
        return true;
    }

    private static bool ArrangePreset(SongEntry entry, InstrumentKind kind, out Note[] notes, out float length, out string error)
    {
        notes = Array.Empty<Note>();
        length = 0f;
        var p = entry.Preset;
        var melody = new List<Note>();
        if (!SongText.TryParse(p.Melody, p.Bpm, p.BeatsPerBar, false, melody, out length, out error))
        {
            return false;
        }
        switch (kind)
        {
            case InstrumentKind.Flute:
                notes = Arranger.Arrange(melody, kind, false, false);
                return true;
            case InstrumentKind.Lyre:
            {
                var all = new List<Note>(melody);
                if (!string.IsNullOrEmpty(p.Chords)
                    && !SongText.TryParseChords(p.Chords, p.Bpm, all, out _, out error))
                {
                    return false;
                }
                notes = Arranger.Arrange(all, kind, false, false);
                return true;
            }
            case InstrumentKind.Tambourine:
            {
                var bar = new List<Note>();
                if (!SongText.TryParse(p.Rhythm, p.Bpm, p.BeatsPerBar, true, bar, out var barLength, out error)
                    || barLength <= 0f)
                {
                    error ??= "no rhythm";
                    return false;
                }
                var hits = new List<Note>();
                var start = p.PickupBeats * 60f / p.Bpm;
                for (var offset = start; offset < length - 0.01f; offset += barLength)
                {
                    foreach (var n in bar)
                    {
                        var t = offset + n.Time;
                        if (t >= length - 0.01f)
                        {
                            break;
                        }
                        var v = (int)Math.Round(n.Velocity * p.RhythmLoudness);
                        hits.Add(new Note(t, Math.Min(n.Length, length - t), n.Pitch, MusicMath.ClampByte(v, 1, 127)));
                    }
                }
                notes = Arranger.Arrange(hits, kind, true, true);
                return true;
            }
        }
        error = "Unknown instrument.";
        return false;
    }

    // Part that looks most like a tune (research: Rizo et al. / Uitdenbogerd and Zobel features): many notes, much of
    // the song covered, mostly one note at a time, pitch near the treble, small steps, named like a tune, flute-like
    // sound. -1 = only drums.
    internal static int BestMelody(MidiScore score)
    {
        var best = -1;
        var bestScore = double.MinValue;
        for (var i = 0; i < score.Parts.Count; i++)
        {
            var p = score.Parts[i];
            if (p.Drums || p.Notes.Count == 0)
            {
                continue;
            }
            var s = MelodyScore(p, score.Length);
            if (s > bestScore)
            {
                bestScore = s;
                best = i;
            }
        }
        return best;
    }

    internal static double MelodyScore(MidiPart p, float songLength)
    {
        var n = p.Notes.Count;
        Coverage(p.Notes, out var sounding, out var poly);
        var occupation = songLength > 0f ? Math.Min(1.0, sounding / songLength) : 0.0;
        var polyRate = sounding > 0 ? poly / sounding : 0.0;
        var mean = p.MeanPitch;
        var steps = 0.0;
        for (var i = 1; i < n; i++)
        {
            steps += Math.Abs(p.Notes[i].Pitch - p.Notes[i - 1].Pitch);
        }
        var meanStep = n > 1 ? steps / (n - 1) : 0.0;
        var score = Math.Log(1 + n) * Math.Sqrt(occupation) * (1 - 0.7 * polyRate)
                    * Math.Exp(-((mean - 72) / 14.0) * ((mean - 72) / 14.0))
                    * (meanStep <= 5 ? 1.0 : 0.7);
        var name = (p.Name ?? "").ToLowerInvariant();
        foreach (var word in new[] { "melody", "lead", "vocal", "voice", "solo", "theme", "flute", "tune" })
        {
            if (name.Contains(word))
            {
                score *= 1.5;
                break;
            }
        }
        if (p.Program >= 72 && p.Program <= 79)
        {
            score *= 1.3; // flutes, recorder, pan pipe, whistle, ocarina
        }
        else if ((p.Program >= 32 && p.Program <= 39) || mean < 50)
        {
            score *= 0.3; // basses
        }
        else if (p.Program >= 88 && p.Program <= 95)
        {
            score *= 0.5; // pads
        }
        return score;
    }

    // Sounding time (any note on) and time with two or more notes on, in seconds.
    private static void Coverage(List<Note> notes, out double sounding, out double poly)
    {
        sounding = 0;
        poly = 0;
        if (notes.Count == 0)
        {
            return;
        }
        var events = new List<KeyValuePair<float, int>>(notes.Count * 2);
        foreach (var n in notes)
        {
            events.Add(new KeyValuePair<float, int>(n.Time, 1));
            events.Add(new KeyValuePair<float, int>(n.End, -1));
        }
        events.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));
        var on = 0;
        var last = events[0].Key;
        foreach (var e in events)
        {
            var dt = e.Key - last;
            if (dt > 0)
            {
                if (on >= 1)
                {
                    sounding += dt;
                }
                if (on >= 2)
                {
                    poly += dt;
                }
            }
            on += e.Value;
            last = e.Key;
        }
    }

    // Lowest other melodic part with a fair share of notes (bass under the tune), -1 = none.
    private static int BassPart(MidiScore score, int melody)
    {
        var mostNotes = 0;
        foreach (var p in score.Parts)
        {
            if (!p.Drums)
            {
                mostNotes = Math.Max(mostNotes, p.Notes.Count);
            }
        }
        var best = -1;
        var lowest = float.MaxValue;
        for (var i = 0; i < score.Parts.Count; i++)
        {
            var p = score.Parts[i];
            if (i == melody || p.Drums || p.Notes.Count < mostNotes / 10 || p.Notes.Count < 8)
            {
                continue;
            }
            var mean = p.MeanPitch;
            if (mean < lowest)
            {
                lowest = mean;
                best = i;
            }
        }
        return best;
    }

    // One low note per onset, at most one every 0.25 s, soft (the tune stays on top).
    private static List<Note> BassLine(List<Note> part)
    {
        var sorted = new List<Note>(part);
        sorted.Sort();
        var result = new List<Note>();
        var last = float.NegativeInfinity;
        var i = 0;
        while (i < sorted.Count)
        {
            var low = sorted[i];
            var j = i + 1;
            while (j < sorted.Count && sorted[j].Time - sorted[i].Time < Arranger.ChordWindow)
            {
                if (sorted[j].Pitch < low.Pitch)
                {
                    low = sorted[j];
                }
                j++;
            }
            if (sorted[i].Time - last >= 0.25f)
            {
                low.Time = sorted[i].Time;
                low.Velocity = (byte)Math.Max(1, low.Velocity * 3 / 4);
                result.Add(low);
                last = sorted[i].Time;
            }
            i = j;
        }
        return result;
    }

    // Song length after the leading silence cut.
    private static float Trimmed(MidiScore score)
    {
        var first = float.MaxValue;
        foreach (var p in score.Parts)
        {
            if (p.Notes.Count > 0)
            {
                first = Math.Min(first, p.Notes[0].Time);
            }
        }
        if (first == float.MaxValue)
        {
            return 0f;
        }
        return Math.Max(0f, score.Length - Math.Max(0f, first - LeadSilence)) + TailSilence;
    }

    internal static string InstrumentName(InstrumentKind kind) => kind switch
    {
        InstrumentKind.Flute => "flute",
        InstrumentKind.Lyre => "lyre",
        InstrumentKind.Tambourine => "tambourine",
        _ => "instrument",
    };

    internal static string Clock(float seconds)
    {
        var s = (int)Math.Round(seconds);
        return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
    }
}
