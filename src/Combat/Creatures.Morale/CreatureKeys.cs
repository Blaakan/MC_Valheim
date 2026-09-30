using System;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = ZDO keys of the mod and their read/write helpers (design 4.3).
//   player ZDO   <guid>.Standing   byte[]  that player's standing (Standing.cs has the layout)
//   creature ZDO <guid>.Provokers  byte[]  per-player provocation deadlines (layout below)
//   creature ZDO <guid>.RoutUntil  long    routed until (server clock ticks); shaken until + ShakenSeconds
//   creature ZDO <guid>.RoutFrom   Vector3 flee from here
// Creature keys written only by the creature's owner, only on events. Never removed (a removed key never reach other
// games), overwritten. All deadlines on server clock (ZNet.GetTime ticks): same meaning on every game.
// ZDO compare byte[] by reference: every write = NEW array, else no revision bump, nobody get it.
internal static class CreatureKeys
{
    internal static readonly int Standing = (ModInfo.Guid + ".Standing").GetStableHashCode();
    internal static readonly int Provokers = (ModInfo.Guid + ".Provokers").GetStableHashCode();
    internal static readonly int RoutUntil = (ModInfo.Guid + ".RoutUntil").GetStableHashCode();
    internal static readonly int RoutFrom = (ModInfo.Guid + ".RoutFrom").GetStableHashCode();

    // Provokers layout 1: byte layout, byte n (0..4), n x { long playerId, long untilTicks } (little endian).
    // 2 + 16n bytes, at most 66. Layout bump = ModNetworkVersion bump too.
    internal const byte ProvokersLayout = 1;
    internal const int MaxProvokers = 4;
    private const int Header = 2;
    private const int EntrySize = 16;

    // Same player again inside this = no write (at most one write per player per second).
    internal const long ThrottleTicks = TimeSpan.TicksPerSecond;

    // ---------- pure (self tests hammer these) ----------

    // Array readable as layout 1? Anything else = empty (nobody provoked).
    internal static int ProvokerCount(byte[] data)
    {
        if (data == null || data.Length < Header || data[0] != ProvokersLayout)
        {
            return 0;
        }
        var n = data[1];
        if (n > MaxProvokers || data.Length != Header + n * EntrySize)
        {
            return 0;
        }
        return n;
    }

    // Deadline of that player's entry, in place, no allocation. False = no entry.
    internal static bool TryGetUntil(byte[] data, long playerId, out long until)
    {
        var n = ProvokerCount(data);
        for (var i = 0; i < n; i++)
        {
            var at = Header + i * EntrySize;
            if (ReadLong(data, at) == playerId)
            {
                until = ReadLong(data, at + 8);
                return true;
            }
        }
        until = 0L;
        return false;
    }

    // Provoked by that player right now = entry with deadline after now.
    internal static bool HasLive(byte[] data, long playerId, long now) =>
        playerId != 0L && TryGetUntil(data, playerId, out var until) && until > now;

    // Provoked by anybody right now = some entry with deadline after now. In place, no allocation.
    internal static bool AnyLive(byte[] data, long now)
    {
        var n = ProvokerCount(data);
        for (var i = 0; i < n; i++)
        {
            if (ReadLong(data, Header + i * EntrySize + 8) > now)
            {
                return true;
            }
        }
        return false;
    }

    // Write needed? Skip when the player's entry was written less than a second ago (its new deadline would move by
    // less than ThrottleTicks).
    internal static bool ShouldWrite(byte[] data, long playerId, long newUntil) =>
        !TryGetUntil(data, playerId, out var until) || newUntil - until >= ThrottleTicks;

    // New array: expired entries pruned, player's entry set to until; with MaxProvokers live others, the one ending
    // first is dropped. Order: kept entries as they were, player's entry last.
    internal static byte[] WithProvocation(byte[] data, long playerId, long until, long now)
    {
        var n = ProvokerCount(data);
        var keepIds = new long[MaxProvokers + 1];
        var keepUntil = new long[MaxProvokers + 1];
        var kept = 0;
        for (var i = 0; i < n; i++)
        {
            var at = Header + i * EntrySize;
            var id = ReadLong(data, at);
            var end = ReadLong(data, at + 8);
            if (id == playerId || end <= now)
            {
                continue;
            }
            keepIds[kept] = id;
            keepUntil[kept] = end;
            kept++;
        }
        while (kept >= MaxProvokers)
        {
            var first = 0;
            for (var i = 1; i < kept; i++)
            {
                if (keepUntil[i] < keepUntil[first])
                {
                    first = i;
                }
            }
            for (var i = first; i < kept - 1; i++)
            {
                keepIds[i] = keepIds[i + 1];
                keepUntil[i] = keepUntil[i + 1];
            }
            kept--;
        }
        keepIds[kept] = playerId;
        keepUntil[kept] = until;
        kept++;
        var result = new byte[Header + kept * EntrySize];
        result[0] = ProvokersLayout;
        result[1] = (byte)kept;
        for (var i = 0; i < kept; i++)
        {
            var at = Header + i * EntrySize;
            WriteLong(result, at, keepIds[i]);
            WriteLong(result, at + 8, keepUntil[i]);
        }
        return result;
    }

    // "Nobody provoked" (rout wipe the anger). Fresh array every time (ZDO compare by reference).
    internal static byte[] EmptyProvokers() => new[] { ProvokersLayout, (byte)0 };

    // ---------- ZDO ----------

    internal static bool IsProvokedBy(ZDO zdo, long playerId, long now) =>
        zdo != null && HasLive(zdo.GetByteArray(Provokers), playerId, now);

    internal static bool IsProvoked(ZDO zdo, long now) => zdo != null && AnyLive(zdo.GetByteArray(Provokers), now);

    // Owner only. True = written.
    internal static bool Provoke(ZDO zdo, long playerId, long now, long durationTicks)
    {
        if (zdo == null || playerId == 0L)
        {
            return false;
        }
        var old = zdo.GetByteArray(Provokers);
        var until = now + durationTicks;
        if (!ShouldWrite(old, playerId, until))
        {
            return false;
        }
        zdo.Set(Provokers, WithProvocation(old, playerId, until, now));
        return true;
    }

    internal static void ClearProvokers(ZDO zdo)
    {
        if (zdo != null && ProvokerCount(zdo.GetByteArray(Provokers)) > 0)
        {
            zdo.Set(Provokers, EmptyProvokers());
        }
    }

    internal static long GetRoutUntil(ZDO zdo) => zdo != null ? zdo.GetLong(RoutUntil, 0L) : 0L;

    internal static Vector3 GetRoutFrom(ZDO zdo, Vector3 fallback) => zdo != null ? zdo.GetVec3(RoutFrom, fallback) : fallback;

    // Later rout extend, earlier never shorten.
    internal static long SetRout(ZDO zdo, long until, Vector3 from)
    {
        var old = zdo.GetLong(RoutUntil, 0L);
        if (until > old)
        {
            zdo.Set(RoutUntil, until);
        }
        else
        {
            until = old;
        }
        zdo.Set(RoutFrom, from);
        return until;
    }

    // ---------- bytes ----------

    internal static bool SameBytes(byte[] a, byte[] b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    // Little endian by hand: same bytes on every machine.
    internal static long ReadLong(byte[] b, int at)
    {
        ulong v = 0;
        for (var i = 7; i >= 0; i--)
        {
            v = (v << 8) | b[at + i];
        }
        return (long)v;
    }

    internal static void WriteLong(byte[] b, int at, long value)
    {
        var v = (ulong)value;
        for (var i = 0; i < 8; i++)
        {
            b[at + i] = (byte)(v & 0xFF);
            v >>= 8;
        }
    }

    internal static int ReadInt(byte[] b, int at) =>
        b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24);

    internal static void WriteInt(byte[] b, int at, int value)
    {
        b[at] = (byte)value;
        b[at + 1] = (byte)(value >> 8);
        b[at + 2] = (byte)(value >> 16);
        b[at + 3] = (byte)(value >> 24);
    }
}
