using System;
using System.Collections.Generic;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

[Flags]
internal enum BatchFlags : byte
{
    None = 0,
    End = 1,     // performance over: listeners let every note go
    Encore = 2,  // performer just played well enough: listeners in BonusRange get the Music effect
    Live = 4,    // notes are played live (mini-game): listeners keep a bigger safety delay
}

// Me = one packet of a performance: who plays (performer ZDOID, position, instrument), which performance (id picked by
// the performer's game), when it was sent (performer's performance clock), flags, and up to MaxNotes notes. Note times
// are the performance clock (seconds) at which the note sounds for the performer; listeners map that clock to their
// own audio clock (never compare clocks across machines).
// Wire (layout 2): byte layout, int performance, ZDOID performer, Vector3 position, byte instrument, int seq,
// float sentAt, byte flags, byte count, then per note: int timeMs, byte pitch, byte velocity, ushort lengthMs.
// Velocity 0 = note off (free play): notes of that pitch sounding at timeMs end there (length ignored).
// Package goes inside a ZRpc call as a ZPackage (never raw parameters: a bad parameter read logs the host out).
internal sealed class NoteBatch
{
    internal const byte Layout = 2;
    internal const int MaxNotes = 48;
    internal const int MaxLengthMs = 60000;

    internal int Performance;
    internal ZDOID Performer;
    internal Vector3 Position;
    internal InstrumentKind Instrument;
    internal int Seq;
    internal float SentAt;
    internal BatchFlags Flags;
    internal readonly List<Note> Notes = new List<Note>(MaxNotes);

    internal bool Live => (Flags & BatchFlags.Live) != 0;
    internal bool IsEnd => (Flags & BatchFlags.End) != 0;
    internal bool IsEncore => (Flags & BatchFlags.Encore) != 0;

    internal void Clear()
    {
        Notes.Clear();
        Flags = BatchFlags.None;
    }

    internal ZPackage ToPackage()
    {
        var pkg = new ZPackage();
        pkg.Write(Layout);
        pkg.Write(Performance);
        pkg.Write(Performer);
        pkg.Write(Position);
        pkg.Write((byte)Instrument);
        pkg.Write(Seq);
        pkg.Write(SentAt);
        pkg.Write((byte)Flags);
        var count = Math.Min(Notes.Count, MaxNotes);
        pkg.Write((byte)count);
        for (var i = 0; i < count; i++)
        {
            var n = Notes[i];
            pkg.Write((int)Math.Round(n.Time * 1000.0));
            pkg.Write(n.Pitch);
            pkg.Write(n.Velocity);
            pkg.Write((ushort)Math.Max(0, Math.Min(MaxLengthMs, (int)Math.Round(n.Length * 1000.0))));
        }
        return pkg;
    }

    // Never trust the wire: wrong layout, performance id 0, bad instrument, too many notes or a short package = false (into left in an
    // unspecified state). Pitch, velocity and length kept in range; note times are checked by the readers (note window).
    // readPerformer false (server): the performer field is skipped, never made into a ZDOID (a ZDOID adds its user id to
    // the game's global table; the server stamps the sender's own character anyway).
    internal static bool TryRead(ZPackage pkg, NoteBatch into, bool readPerformer = true)
    {
        try
        {
            if (pkg == null || pkg.ReadByte() != Layout)
            {
                return false;
            }
            into.Notes.Clear();
            into.Performance = pkg.ReadInt();
            if (into.Performance == 0)
            {
                return false; // games pick ids from 1 up
            }
            if (readPerformer)
            {
                into.Performer = pkg.ReadZDOID();
            }
            else
            {
                pkg.ReadLong();
                pkg.ReadUInt();
                into.Performer = ZDOID.None;
            }
            into.Position = pkg.ReadVector3();
            var instrument = pkg.ReadByte();
            if (instrument < (byte)InstrumentKind.Flute || instrument > (byte)InstrumentKind.Tambourine)
            {
                return false;
            }
            into.Instrument = (InstrumentKind)instrument;
            into.Seq = pkg.ReadInt();
            into.SentAt = pkg.ReadSingle();
            into.Flags = (BatchFlags)(pkg.ReadByte() & 0x07);
            var count = pkg.ReadByte();
            if (count > MaxNotes || float.IsNaN(into.SentAt) || float.IsInfinity(into.SentAt)
                || float.IsNaN(into.Position.x) || float.IsInfinity(into.Position.x))
            {
                return false;
            }
            for (var i = 0; i < count; i++)
            {
                var timeMs = pkg.ReadInt();
                var pitch = pkg.ReadByte();
                var velocity = pkg.ReadByte();
                var lengthMs = pkg.ReadUShort();
                if (pitch > 127 || velocity > 127)
                {
                    return false;
                }
                into.Notes.Add(new Note(timeMs / 1000f, Math.Min(lengthMs, (ushort)MaxLengthMs) / 1000f, pitch, velocity));
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
