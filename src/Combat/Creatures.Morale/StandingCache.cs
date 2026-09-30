using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = decoded standing of each player, read from their player ZDO at most once per second (design 2.2 "Reading").
// A remote player's byte array is a new object after every sync (ZDO.Deserialize), so me compare content with my own
// copy and decode only when it changed. Missing key, bad bytes (logged once per player) or withdrawn = null = no
// standing = that player is treated as vanilla. Destroyed players purged with the CreatureState purge (30 s); all
// cleared at world end and when turned off.
internal static class StandingCache
{
    private sealed class Entry
    {
        internal Player Player;
        internal byte[] Source;   // array object last read (same object = nothing changed, local player's case)
        internal byte[] Copy;     // own copy of its content
        internal StandingData Data;
        internal float NextRead = -1f;
        internal bool BadLogged;
    }

    private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();
    private static readonly List<int> Dead = new List<int>();

    // Hot path (sense checks): one dictionary lookup, a read at most once per second.
    internal static StandingData Get(Player player)
    {
        if (player == null)
        {
            return null;
        }
        var id = player.GetInstanceID();
        if (!Entries.TryGetValue(id, out var entry))
        {
            entry = new Entry { Player = player };
            Entries[id] = entry;
        }
        var now = Time.time;
        if (now >= entry.NextRead)
        {
            entry.NextRead = now + 1f;
            Read(entry);
        }
        return entry.Data;
    }

    private static void Read(Entry entry)
    {
        var nview = entry.Player.m_nview;
        var bytes = nview != null && nview.IsValid() ? nview.GetZDO().GetByteArray(CreatureKeys.Standing) : null;
        if (bytes == null)
        {
            entry.Source = null;
            entry.Copy = null;
            entry.Data = null;
            return;
        }
        if (ReferenceEquals(bytes, entry.Source))
        {
            return;
        }
        entry.Source = bytes;
        if (entry.Copy != null && CreatureKeys.SameBytes(bytes, entry.Copy))
        {
            return;
        }
        entry.Copy = (byte[])bytes.Clone();
        if (Standing.TryDecode(bytes, out var data))
        {
            entry.Data = data;
            return;
        }
        entry.Data = null;
        if (!entry.BadLogged)
        {
            entry.BadLogged = true;
            Log.Warning($"The creature standing published by {entry.Player.GetPlayerName()} cannot be read by this "
                        + "version; creatures treat that player as in the normal game.");
        }
    }

    // Own standing just written (or withdrawn): next Get read it at once.
    internal static void Invalidate(Player player)
    {
        if (player != null && Entries.TryGetValue(player.GetInstanceID(), out var entry))
        {
            entry.NextRead = -1f;
        }
    }

    // Drop players whose object is gone (logout, respawn = new object).
    internal static void Purge()
    {
        Dead.Clear();
        foreach (var pair in Entries)
        {
            if (pair.Value.Player == null)
            {
                Dead.Add(pair.Key);
            }
        }
        foreach (var id in Dead)
        {
            Entries.Remove(id);
        }
        Dead.Clear();
    }

    internal static void Clear()
    {
        Entries.Clear();
    }
}
