#if DEBUG
using System.Collections.Generic;
using UnityEngine;

namespace MC.Core.ProbeWorldMod;

// Menu buttons me press write a few player prefs (last character, last world, crossplay toggle, PlayFab auto
// login). Prefs are not saves (registry, not redirected), so me remember them before and put them back right after
// world start (and again before quit): next normal game start still select player's own character and world.
// Raw PlayerPrefs keys: on Steam PC, PlatformPrefs = PlayerPrefs without prefix.
internal static class PrefsGuard
{
    private enum Kind
    {
        Text,
        Number,
    }

    private static readonly KeyValuePair<string, Kind>[] Keys =
    {
        new KeyValuePair<string, Kind>("profile", Kind.Text),
        new KeyValuePair<string, Kind>("world", Kind.Text),
        new KeyValuePair<string, Kind>("crossplay", Kind.Number),
        new KeyValuePair<string, Kind>("ShouldTryAutoLogin", Kind.Number),
    };

    private static readonly Dictionary<string, object> Saved = new Dictionary<string, object>();
    private static bool _taken;

    internal static void Snapshot()
    {
        Saved.Clear();
        foreach (var k in Keys)
        {
            if (!PlayerPrefs.HasKey(k.Key))
            {
                Saved[k.Key] = null;
            }
            else if (k.Value == Kind.Text)
            {
                Saved[k.Key] = PlayerPrefs.GetString(k.Key);
            }
            else
            {
                Saved[k.Key] = PlayerPrefs.GetInt(k.Key);
            }
        }
        _taken = true;
    }

    // Return how many keys me had to put back.
    internal static int Restore()
    {
        if (!_taken)
        {
            return 0;
        }
        var changed = 0;
        foreach (var k in Keys)
        {
            var old = Saved[k.Key];
            if (old == null)
            {
                if (PlayerPrefs.HasKey(k.Key))
                {
                    PlayerPrefs.DeleteKey(k.Key);
                    changed++;
                }
            }
            else if (old is string s)
            {
                if (!PlayerPrefs.HasKey(k.Key) || PlayerPrefs.GetString(k.Key) != s)
                {
                    PlayerPrefs.SetString(k.Key, s);
                    changed++;
                }
            }
            else if (old is int i)
            {
                if (!PlayerPrefs.HasKey(k.Key) || PlayerPrefs.GetInt(k.Key) != i)
                {
                    PlayerPrefs.SetInt(k.Key, i);
                    changed++;
                }
            }
        }
        return changed;
    }
}
#endif
