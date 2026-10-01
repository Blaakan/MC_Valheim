using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using MC.Shared;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = notes about other mods that touch the same things (design 6.3). Nothing block me: log once per found mod so a
// player know why stars or Hexen look different. Matched on plugin name or GUID pieces (GUIDs not all known), spaces
// ignored. Run once per process when me turn on.
internal static class Compat
{
    private struct Note
    {
        internal string Piece;
        internal bool Warn;
        internal string Text;
    }

    private static readonly Note[] Notes =
    {
        new Note
        {
            Piece = "WorldAdvancementProgression", Warn = true,
            Text = "blocks unknown global keys by default: add mc_dn_stones to its allowed keys, or the broken Malicious "
                   + "Ice count never sticks and the Deep North never awakens.",
        },
        new Note { Piece = "StarLevelSystem", Text = LevelText },
        new Note { Piece = "CreatureLevelAndLootControl", Text = LevelText },
        new Note { Piece = "CreatureLevelControl", Text = LevelText },
        new Note { Piece = "ReefLevels", Text = LevelText },
        new Note { Piece = "CreatureManager", Text = LevelText },
        new Note { Piece = "CreatureLevelUp", Text = LevelText },
        new Note
        {
            Piece = "HexenBeGone",
            Text = "blocks Hexen from natural spawns by default, which includes the Hexen of the Deep North areas.",
        },
    };

    private const string LevelText = "decides creature levels itself, so the Deep North stage star odds may not apply.";

    private static bool _done;
    private static readonly HashSet<string> Logged = new HashSet<string>();

    internal static void LogOnce()
    {
        if (_done)
        {
            return;
        }
        _done = true;
        foreach (var pair in Chainloader.PluginInfos)
        {
            var info = pair.Value;
            if (info == null || info.Metadata == null)
            {
                continue;
            }
            var guid = info.Metadata.GUID ?? "";
            if (string.Equals(guid, ModInfo.Guid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var name = string.IsNullOrEmpty(info.Metadata.Name) ? guid : info.Metadata.Name;
            var key = (name + " " + guid).Replace(" ", "");
            foreach (var note in Notes)
            {
                if (key.IndexOf(note.Piece, StringComparison.OrdinalIgnoreCase) < 0 || !Logged.Add(guid))
                {
                    continue;
                }
                var text = $"{name} is installed: it {note.Text}";
                if (note.Warn)
                {
                    Log.Warning(text);
                }
                else
                {
                    Log.Info(text);
                }
                break;
            }
        }
    }
}
