using System;
using BepInEx.Bootstrap;
using MC.Shared;

namespace MC.Combat.CreaturesMoraleMod;

// Me = other mods that also change when creatures attack players (design 6.3). Me only warn, me never stand down:
// both run, result hard to predict, user pick one. Found by plugin GUID or by plugin name (case-insensitive part of
// the name), because most GUIDs are not checked.
// Me look at world start (ZNet.Awake postfix) and when turned on inside a world, not at plugin start: first
// activation run inside our Awake, and BepInEx put a plugin in PluginInfos only when it load it (mods loading after
// "MC." would be missed). One warning set per activation.
internal static class Compat
{
    private sealed class Known
    {
        internal readonly string Title;
        internal readonly string Guid;       // null = GUID not known
        internal readonly string[] NameParts;

        internal Known(string title, string guid, params string[] nameParts)
        {
            Title = title;
            Guid = guid;
            NameParts = nameParts;
        }
    }

    // GUIDs: TruePassiveMobs and The Mark of Oden from the research brief's source read (unverified); others unknown.
    private static readonly Known[] Overlapping =
    {
        new Known("TruePassiveMobs", "com.lhoffl.TruePassiveMobs", "TruePassiveMobs", "True Passive Mobs"),
        new Known("The Mark of Oden", "picsoul.valheim.markofoden", "Mark of Oden", "MarkOfOden"),
        new Known("FearMe", null, "FearMe", "Fear Me"),
        new Known("Odin's Ótti", null, "Odin's Otti", "Odins Otti", "OdinsOtti", "Ótti"),
        new Known("CowardlyGreydwarfs", null, "CowardlyGreydwarfs", "Cowardly Greydwarfs"),
        new Known("FleeOnSight", null, "FleeOnSight", "Flee On Sight"),
        new Known("Monster AI Tweaks", null, "MonsterAITweaks", "Monster AI Tweaks"),
    };

    private static bool _warned;

    // OnActivated: warn again at next look.
    internal static void Reset()
    {
        _warned = false;
        if (ZNet.instance != null)
        {
            WarnOnce();
        }
    }

    // ZNet.Awake postfix (world start), and Reset when already in a world.
    internal static void WarnOnce()
    {
        if (_warned)
        {
            return;
        }
        _warned = true;
        foreach (var known in Overlapping)
        {
            var found = Find(known);
            if (found != null)
            {
                Log.Warning($"{found} is installed and also changes when creatures attack players. Both mods will run "
                            + $"together with {ModInfo.Name} and the result is hard to predict: use only one of them.");
            }
        }
    }

    // Name of the loaded plugin that match, or null.
    private static string Find(Known known)
    {
        foreach (var pair in Chainloader.PluginInfos)
        {
            var info = pair.Value;
            if (info == null || info.Metadata == null || info.Metadata.GUID == ModInfo.Guid)
            {
                continue;
            }
            if (known.Guid != null && string.Equals(info.Metadata.GUID, known.Guid, StringComparison.OrdinalIgnoreCase))
            {
                return Label(known, info);
            }
            var name = info.Metadata.Name ?? "";
            foreach (var part in known.NameParts)
            {
                if (name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return Label(known, info);
                }
            }
        }
        return null;
    }

    private static string Label(Known known, BepInEx.PluginInfo info) =>
        string.Equals(info.Metadata.Name, known.Title, StringComparison.OrdinalIgnoreCase)
            ? known.Title
            : $"{known.Title} ({info.Metadata.Name})";
}
