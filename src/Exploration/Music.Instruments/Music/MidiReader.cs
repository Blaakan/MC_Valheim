using System;
using System.Collections.Generic;
using System.Text;

namespace MC.Exploration.MusicInstrumentsMod;

// One voice of a MIDI file: notes of one channel in one track (format 0 file: one track, many channels).
internal sealed class MidiPart
{
    internal int Track;
    internal int Channel;      // 0..15; 9 = General MIDI drums
    internal string Name = ""; // track name, else "Channel N"
    internal int Program = -1; // first program change seen on this channel, -1 = none
    internal readonly List<Note> Notes = new List<Note>();

    internal bool Drums => Channel == 9;

    // "Name (Guitar)": track name with General MIDI sound family when known.
    internal string Label
    {
        get
        {
            var family = Drums ? "Drums" : Program >= 0 ? MidiReader.Family(Program) : null;
            if (string.IsNullOrEmpty(Name))
            {
                return family ?? "Channel " + (Channel + 1);
            }
            return family != null && Name.IndexOf(family, StringComparison.OrdinalIgnoreCase) < 0
                ? Name + " (" + family + ")"
                : Name;
        }
    }

    // Mean pitch (melody hunt: tunes sit high).
    internal float MeanPitch
    {
        get
        {
            if (Notes.Count == 0)
            {
                return 0f;
            }
            var sum = 0L;
            foreach (var n in Notes)
            {
                sum += n.Pitch;
            }
            return sum / (float)Notes.Count;
        }
    }
}

// What a MIDI file hold: parts (track x channel) with notes in seconds, title (first track name), length.
internal sealed class MidiScore
{
    internal int Format;
    internal string Title = "";
    internal readonly List<MidiPart> Parts = new List<MidiPart>();
    internal float Length;
    internal int NoteCount;
    internal bool Truncated;   // file ended inside a track or note/length cap hit: what came before is kept
}

// Me = Standard MIDI File reader (SMF 1.0: format 0, 1, 2; RIFF RMID wrapper; ticks-per-quarter and SMPTE timing).
// Never throw: broken file = null score + error text; damaged tail = keep what came before (Truncated).
// Pure C#, no Unity: offline test compile me too.
// Rules me follow: running status only for channel messages (lenient: a meta or sysex event between keeps it, broken
// writers need that, good files never notice); note-on velocity 0 = note-off; note-on on a pitch still held on that
// channel = held one ends there, new one starts (retrigger, like a one-voice-per-key synth); note never closed =
// ends at its track's end; tempo changes of every track form one tempo map (format 0/1; format 2 = each track its
// own); tempo outside 10..1000 BPM ignored.
internal static class MidiReader
{
    internal const int MaxFileBytes = 2 * 1024 * 1024;
    internal const int MaxNotes = 30000;
    internal const float MaxSeconds = 20f * 60f;
    internal const float MinNoteSeconds = 0.03f;
    private const int DefaultTempo = 500000; // us per quarter = 120 BPM
    private const int MinTempo = 60000;      // 1000 BPM
    private const int MaxTempo = 6000000;    // 10 BPM

    private struct TempoEvent
    {
        internal long Tick;
        internal int UsPerQuarter;
        internal int Order; // file order (ties at same tick: later wins)
    }

    private struct RawNote
    {
        internal long On;
        internal long Off;
        internal byte Pitch;
        internal byte Velocity;
        internal int Channel;
    }

    private sealed class RawTrack
    {
        internal string Name = "";
        internal readonly List<RawNote> Notes = new List<RawNote>();
        internal readonly List<TempoEvent> Tempos = new List<TempoEvent>();
        internal readonly int[] Programs = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        internal long EndTick;
    }

    internal static MidiScore Read(byte[] data, out string error)
    {
        error = null;
        try
        {
            return ReadInner(data, out error);
        }
        catch (Exception e)
        {
            // Never expected (every read is bounds checked); a bug here must not reach game code.
            error = "could not be read (" + e.GetType().Name + ")";
            return null;
        }
    }

    private static MidiScore ReadInner(byte[] data, out string error)
    {
        error = null;
        if (data == null || data.Length < 14)
        {
            error = "is too short to be a MIDI file";
            return null;
        }
        if (data.Length > MaxFileBytes)
        {
            error = "is larger than " + MaxFileBytes / (1024 * 1024) + " MB";
            return null;
        }
        var start = FindSmf(data);
        if (start < 0)
        {
            error = "is not a MIDI file (no MThd header)";
            return null;
        }
        var pos = start + 4;
        var headerLength = (int)Math.Min(ReadU32(data, pos), int.MaxValue);
        pos += 4;
        if (headerLength < 6 || pos + 6 > data.Length)
        {
            error = "has a broken MIDI header";
            return null;
        }
        var format = ReadU16(data, pos);
        var division = ReadU16(data, pos + 4);
        pos = (int)Math.Min((long)pos + headerLength, data.Length);
        if (format > 2)
        {
            error = "uses an unknown MIDI format (" + format + ")";
            return null;
        }
        if (division == 0)
        {
            error = "has no timing (division 0)";
            return null;
        }

        var score = new MidiScore { Format = format };
        var tracks = new List<RawTrack>();
        var tempoOrder = 0;
        while (pos + 8 <= data.Length)
        {
            var isTrack = data[pos] == 'M' && data[pos + 1] == 'T' && data[pos + 2] == 'r' && data[pos + 3] == 'k';
            var length = ReadU32(data, pos + 4);
            pos += 8;
            var end = (long)pos + length;
            if (end > data.Length)
            {
                score.Truncated = true;
                end = data.Length;
            }
            if (isTrack)
            {
                var track = new RawTrack();
                if (!ReadTrack(data, pos, (int)end, track, ref tempoOrder))
                {
                    score.Truncated = true;
                }
                tracks.Add(track);
            }
            pos = (int)end;
        }
        if (tracks.Count == 0)
        {
            error = "has no MIDI tracks";
            return null;
        }

        // Tempo map: format 0/1 = all tracks together; format 2 = per track.
        List<TempoEvent> shared = null;
        if (format != 2)
        {
            shared = new List<TempoEvent>();
            foreach (var t in tracks)
            {
                shared.AddRange(t.Tempos);
            }
        }

        for (var ti = 0; ti < tracks.Count; ti++)
        {
            var track = tracks[ti];
            if (string.IsNullOrEmpty(score.Title) && track.Name.Length > 0)
            {
                score.Title = track.Name;
            }
            if (track.Notes.Count == 0)
            {
                continue;
            }
            var clock = new TickClock(division, format == 2 ? track.Tempos : shared);
            var byChannel = new MidiPart[16];
            foreach (var raw in track.Notes)
            {
                if (score.NoteCount >= MaxNotes)
                {
                    score.Truncated = true;
                    break;
                }
                var on = (float)clock.Seconds(raw.On);
                if (on > MaxSeconds)
                {
                    score.Truncated = true;
                    continue;
                }
                var off = (float)clock.Seconds(raw.Off);
                var part = byChannel[raw.Channel];
                if (part == null)
                {
                    part = new MidiPart
                    {
                        Track = ti,
                        Channel = raw.Channel,
                        Name = track.Name,
                        Program = track.Programs[raw.Channel],
                    };
                    byChannel[raw.Channel] = part;
                }
                part.Notes.Add(new Note(on, Math.Max(MinNoteSeconds, off - on), raw.Pitch, raw.Velocity));
                score.NoteCount++;
                if (off > score.Length)
                {
                    score.Length = off;
                }
            }
            foreach (var part in byChannel)
            {
                if (part != null && part.Notes.Count > 0)
                {
                    part.Notes.Sort();
                    score.Parts.Add(part);
                }
            }
        }
        if (score.Parts.Count == 0)
        {
            error = "has no notes";
            return null;
        }
        return score;
    }

    // "MThd" at start, inside a RIFF RMID "data" chunk, or after a short junk header (MacBinary).
    private static int FindSmf(byte[] d)
    {
        if (IsTag(d, 0, "RIFF") && d.Length >= 12 && IsTag(d, 8, "RMID"))
        {
            var p = 12;
            while (p + 8 <= d.Length)
            {
                var size = ReadU32Le(d, p + 4);
                if (IsTag(d, p, "data"))
                {
                    return IsTag(d, p + 8, "MThd") ? p + 8 : -1;
                }
                var next = (long)p + 8 + size + (size & 1);
                if (next > d.Length)
                {
                    return -1;
                }
                p = (int)next;
            }
            return -1;
        }
        var limit = Math.Min(d.Length - 4, 4096);
        for (var i = 0; i <= limit; i++)
        {
            if (IsTag(d, i, "MThd"))
            {
                return i;
            }
        }
        return -1;
    }

    // One MTrk. False = damaged (stopped early); what came before is kept.
    private static bool ReadTrack(byte[] d, int pos, int end, RawTrack track, ref int tempoOrder)
    {
        long tick = 0;
        var running = 0;
        // Open note per channel*128+pitch (one: retrigger).
        var open = new Dictionary<int, RawNote>();
        var ok = true;
        while (pos < end)
        {
            if (!ReadVlq(d, ref pos, end, out var delta))
            {
                ok = false;
                break;
            }
            tick += delta;
            if (pos >= end)
            {
                ok = false;
                break;
            }
            int status = d[pos];
            if (status >= 0x80)
            {
                pos++;
            }
            else if (running != 0)
            {
                status = running; // data byte: running status
            }
            else
            {
                ok = false; // data byte with no status to repeat
                break;
            }

            if (status == 0xFF)
            {
                if (pos >= end)
                {
                    ok = false;
                    break;
                }
                var type = d[pos++];
                if (!ReadVlq(d, ref pos, end, out var len) || pos + len > end)
                {
                    ok = false;
                    break;
                }
                var p = pos;
                pos += (int)len;
                if (type == 0x2F)
                {
                    break; // end of track
                }
                if (type == 0x51 && len >= 3)
                {
                    var us = (d[p] << 16) | (d[p + 1] << 8) | d[p + 2];
                    if (us >= MinTempo && us <= MaxTempo)
                    {
                        track.Tempos.Add(new TempoEvent { Tick = tick, UsPerQuarter = us, Order = tempoOrder++ });
                    }
                }
                else if (type == 0x03 && len > 0 && track.Name.Length == 0)
                {
                    track.Name = CleanText(d, p, (int)len);
                }
                continue;
            }
            if (status == 0xF0 || status == 0xF7)
            {
                if (!ReadVlq(d, ref pos, end, out var len) || pos + len > end)
                {
                    ok = false;
                    break;
                }
                pos += (int)len;
                continue;
            }
            if (status >= 0xF0)
            {
                // System common / real time messages do not belong in files: cannot know their size, stop here.
                ok = false;
                break;
            }

            running = status;
            var kind = status & 0xF0;
            var channel = status & 0x0F;
            var dataBytes = kind == 0xC0 || kind == 0xD0 ? 1 : 2;
            if (pos + dataBytes > end)
            {
                ok = false;
                break;
            }
            if (d[pos] >= 0x80 || (dataBytes == 2 && d[pos + 1] >= 0x80))
            {
                ok = false; // status byte where data must be: damaged, stop here
                break;
            }
            var a = d[pos];
            var b = dataBytes == 2 ? d[pos + 1] : 0;
            pos += dataBytes;
            switch (kind)
            {
                case 0x90 when b > 0:
                {
                    var key = channel * 128 + a;
                    if (open.TryGetValue(key, out var held))
                    {
                        held.Off = tick;
                        track.Notes.Add(held);
                    }
                    open[key] = new RawNote { On = tick, Pitch = (byte)a, Velocity = (byte)b, Channel = channel };
                    break;
                }
                case 0x90:
                case 0x80:
                {
                    var key = channel * 128 + a;
                    if (open.TryGetValue(key, out var held))
                    {
                        held.Off = tick;
                        track.Notes.Add(held);
                        open.Remove(key);
                    }
                    break;
                }
                case 0xC0:
                    if (track.Programs[channel] < 0)
                    {
                        track.Programs[channel] = a;
                    }
                    break;
            }
        }
        track.EndTick = tick;
        // Never closed: ends at track end (at least a short note).
        foreach (var held in open.Values)
        {
            var n = held;
            n.Off = Math.Max(tick, n.On);
            track.Notes.Add(n);
        }
        track.Notes.Sort((x, y) => x.On.CompareTo(y.On));
        return ok;
    }

    // Tick -> seconds through a tempo map (or SMPTE frames).
    private sealed class TickClock
    {
        private readonly bool _smpte;
        private readonly double _secondsPerTickSmpte;
        private readonly int _ticksPerQuarter;
        private readonly long[] _ticks;
        private readonly double[] _seconds;
        private readonly int[] _tempos;

        internal TickClock(int division, List<TempoEvent> tempos)
        {
            if ((division & 0x8000) != 0)
            {
                _smpte = true;
                var fps = -(sbyte)(division >> 8);
                var perFrame = division & 0xFF;
                var realFps = fps == 29 ? 29.97 : fps;
                _secondsPerTickSmpte = realFps > 0 && perFrame > 0 ? 1.0 / (realFps * perFrame) : 1.0 / 1000.0;
                return;
            }
            _ticksPerQuarter = division;
            var list = tempos != null ? new List<TempoEvent>(tempos) : new List<TempoEvent>();
            list.Sort((x, y) => x.Tick != y.Tick ? x.Tick.CompareTo(y.Tick) : x.Order.CompareTo(y.Order));
            // Keep last tempo per tick; map starts at tick 0 with 120 BPM unless set there.
            var ticks = new List<long> { 0 };
            var temps = new List<int> { DefaultTempo };
            foreach (var t in list)
            {
                if (t.Tick == ticks[ticks.Count - 1])
                {
                    temps[temps.Count - 1] = t.UsPerQuarter;
                }
                else
                {
                    ticks.Add(t.Tick);
                    temps.Add(t.UsPerQuarter);
                }
            }
            _ticks = ticks.ToArray();
            _tempos = temps.ToArray();
            _seconds = new double[_ticks.Length];
            for (var i = 1; i < _ticks.Length; i++)
            {
                _seconds[i] = _seconds[i - 1] + (_ticks[i] - _ticks[i - 1]) * (double)_tempos[i - 1] / (_ticksPerQuarter * 1e6);
            }
        }

        internal double Seconds(long tick)
        {
            if (_smpte)
            {
                return tick * _secondsPerTickSmpte;
            }
            // Last segment starting at or before tick (binary search).
            int lo = 0, hi = _ticks.Length - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) >> 1;
                if (_ticks[mid] <= tick)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return _seconds[lo] + (tick - _ticks[lo]) * (double)_tempos[lo] / (_ticksPerQuarter * 1e6);
        }
    }

    // Variable length quantity, max 4 bytes (28 bits).
    private static bool ReadVlq(byte[] d, ref int pos, int end, out long value)
    {
        value = 0;
        for (var i = 0; i < 4; i++)
        {
            if (pos >= end)
            {
                return false;
            }
            var b = d[pos++];
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }
        return false;
    }

    private static int ReadU16(byte[] d, int p) => (d[p] << 8) | d[p + 1];

    private static long ReadU32(byte[] d, int p) =>
        ((long)d[p] << 24) | ((long)d[p + 1] << 16) | ((long)d[p + 2] << 8) | d[p + 3];

    private static long ReadU32Le(byte[] d, int p) =>
        d[p] | ((long)d[p + 1] << 8) | ((long)d[p + 2] << 16) | ((long)d[p + 3] << 24);

    private static bool IsTag(byte[] d, int p, string tag)
    {
        if (p < 0 || p + tag.Length > d.Length)
        {
            return false;
        }
        for (var i = 0; i < tag.Length; i++)
        {
            if (d[p + i] != tag[i])
            {
                return false;
            }
        }
        return true;
    }

    // Track names are Latin-1 in old files, UTF-8 in new ones: printable ASCII kept, rest dropped, length capped.
    private static string CleanText(byte[] d, int p, int len)
    {
        var sb = new StringBuilder(Math.Min(len, 48));
        for (var i = 0; i < len && sb.Length < 48; i++)
        {
            var c = (char)d[p + i];
            if (c >= 32 && c < 127)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Trim();
    }

    private static readonly string[] Families =
    {
        "Piano", "Chromatic Percussion", "Organ", "Guitar", "Bass", "Strings", "Ensemble", "Brass",
        "Reed", "Pipe", "Synth Lead", "Synth Pad", "Synth Effects", "Ethnic", "Percussive", "Sound Effects",
    };

    // General MIDI program 0..127 -> family name.
    internal static string Family(int program) => program >= 0 && program < 128 ? Families[program / 8] : null;
}
