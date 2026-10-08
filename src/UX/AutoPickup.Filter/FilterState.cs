using System;
using System.Collections.Generic;
using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.UX.AutoPickupFilterMod;

// Me = filter of local character: mode + Ignored + Selected (item keys = prefab spawn names), stored in
// Player.m_customData (saved in .fch, travel to any server, vanilla ignore unknown keys). Me also give the verdict
// "auto pickup must skip this drop" with small caches, so the 50 Hz gate cost one dictionary lookup per drop.
internal static class FilterState
{
    internal const string KeyVersion = ModInfo.Guid + ".Version";
    internal const string KeyMode = ModInfo.Guid + ".Mode";
    internal const string KeyIgnored = ModInfo.Guid + ".Ignored";
    internal const string KeySelected = ModInfo.Guid + ".Selected";
    private const string FormatVersion = "1";

    // Drop-key cache (drops without drop prefab) grow per drop instance: me clear it past this.
    private const int MaxDropCache = 1024;

    private static Player _owner;

    internal static FilterMode Mode = FilterMode.Everything;
    internal static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.Ordinal);
    internal static readonly HashSet<string> Selected = new HashSet<string>(StringComparer.Ordinal);

    // Any change (mode, list, character, display setting) bump me: UI redraw when it differ from what it show.
    internal static int Version;

    // Prefab instance id -> key is in active list. Name read (alloc) once per item type, not per frame.
    private static readonly Dictionary<int, bool> ListedByPrefab = new Dictionary<int, bool>();
    private static readonly Dictionary<int, bool> ListedByDrop = new Dictionary<int, bool>();

    private static bool _warnedMode;

    internal static HashSet<string> ActiveList =>
        Mode == FilterMode.SkipIgnored ? Ignored : Mode == FilterMode.OnlySelected ? Selected : null;

    // Load state of local player if not done. False = no local player (menu, loading).
    internal static bool EnsureLocal()
    {
        var p = Player.m_localPlayer;
        if (p == null)
        {
            return false;
        }
        EnsureLoaded(p);
        return true;
    }

    internal static void EnsureLoaded(Player p)
    {
        if (ReferenceEquals(p, _owner))
        {
            return;
        }
        _owner = p;
        Load(p);
        // New character (login, other character, respawned body): no V message until V really flip.
        AutoPickupScope.LastEnabled = null;
        HarvestGrace.Clear();
        Version++;
    }

    private static void Load(Player p)
    {
        Ignored.Clear();
        Selected.Clear();
        ClearCaches();
        var data = p.m_customData;
        if (data != null && data.TryGetValue(KeyMode, out var modeText))
        {
            Mode = ParseMode(modeText);
            if (data.TryGetValue(KeyVersion, out var fmt) && fmt != FormatVersion)
            {
                Log.Warning($"Loot filter data of this character has format {fmt} (this version reads {FormatVersion}); "
                            + "reading it anyway.");
            }
            ParseList(data.TryGetValue(KeyIgnored, out var ign) ? ign : null, Ignored, resolve: false);
            ParseList(data.TryGetValue(KeySelected, out var sel) ? sel : null, Selected, resolve: false);
            Log.Debug($"Loot filter loaded for this character: mode {Mode}, {Ignored.Count} ignored, {Selected.Count} selected.");
        }
        else
        {
            // Never touched: Everything + config defaults, nothing written (later config edits still apply).
            Mode = FilterMode.Everything;
            ReadDefaults();
            Log.Debug($"Loot filter: character never used it, mode Everything, defaults {Ignored.Count} ignored, "
                      + $"{Selected.Count} selected.");
        }
    }

    private static void ReadDefaults()
    {
        Ignored.Clear();
        Selected.Clear();
        ParseList(Plugin.DefaultIgnoredText, Ignored, resolve: true);
        ParseList(Plugin.DefaultSelectedText, Selected, resolve: true);
    }

    private static FilterMode ParseMode(string text)
    {
        switch (text)
        {
            case nameof(FilterMode.Everything):
                return FilterMode.Everything;
            case nameof(FilterMode.SkipIgnored):
                return FilterMode.SkipIgnored;
            case nameof(FilterMode.OnlySelected):
                return FilterMode.OnlySelected;
        }
        if (!_warnedMode)
        {
            _warnedMode = true;
            Log.Warning($"Unknown loot filter mode '{text}' saved in this character: using Everything.");
        }
        return FilterMode.Everything;
    }

    // "Stone, wood ,Resin" -> set. resolve = fix casing through ObjectDB (typed names); unknown names kept as
    // typed (item of a mod not installed now come back with it).
    private static void ParseList(string text, HashSet<string> into, bool resolve)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        foreach (var raw in text.Split(','))
        {
            var name = raw.Trim();
            if (name.Length == 0)
            {
                continue;
            }
            if (resolve)
            {
                name = ItemCatalog.Resolve(name) ?? name;
            }
            into.Add(name);
        }
    }

    // All four keys at once, in m_customData (memory). Vanilla write .fch only when it save character: world save
    // (every 30 min), sleep in bed, menu save, server ask, logout/quit. Respawn only carry data from dead body to new
    // body in memory; crash before next save lose last changes.
    private static void Save()
    {
        if (_owner == null || _owner.m_customData == null)
        {
            return;
        }
        var data = _owner.m_customData;
        data[KeyVersion] = FormatVersion;
        data[KeyMode] = Mode.ToString();
        data[KeyIgnored] = Join(Ignored);
        data[KeySelected] = Join(Selected);
    }

    internal static string Join(HashSet<string> set)
    {
        if (set.Count == 0)
        {
            return "";
        }
        var list = new List<string>(set);
        list.Sort(StringComparer.Ordinal);
        return string.Join(",", list);
    }

    // Harvest grace tags are not cache: mode, list or setting change never take them away.
    private static void ClearCaches()
    {
        ListedByPrefab.Clear();
        ListedByDrop.Clear();
    }

    private static void Changed(string what)
    {
        ClearCaches();
        Version++;
        Save();
        Log.Debug($"Loot filter: {what} (mode {Mode}, {Ignored.Count} ignored, {Selected.Count} selected).");
    }

    // Setting that change display or verdict (markers, hover line, harvest grace, controls text).
    internal static void BumpVersion()
    {
        ClearCaches();
        Version++;
    }

    // Defaults edited: character that never used filter take new defaults now (still nothing written).
    internal static void OnDefaultsChanged()
    {
        try
        {
            if (_owner == null || _owner.m_customData == null || _owner.m_customData.ContainsKey(KeyMode))
            {
                return;
            }
            ReadDefaults();
            BumpVersion();
        }
        catch (Exception e)
        {
            Log.Warning($"Could not apply the new default lists: {e.Message}");
        }
    }

    internal static void Reset()
    {
        _owner = null;
        Mode = FilterMode.Everything;
        Ignored.Clear();
        Selected.Clear();
        ClearCaches();
        Version++;
    }

    // ---------------------------------------------------------------- changes

    // Add key if missing, remove if there. True = now in list.
    internal static bool Toggle(HashSet<string> list, string key)
    {
        bool added;
        if (list.Remove(key))
        {
            added = false;
        }
        else
        {
            list.Add(key);
            added = true;
        }
        Changed($"{(added ? "added" : "removed")} {key} {(added ? "to" : "from")} {ListName(list)}");
        return added;
    }

    internal static void SetMode(FilterMode mode)
    {
        Mode = mode;
        Changed($"mode set to {mode}");
    }

    internal static FilterMode CycleMode()
    {
        SetMode(Mode == FilterMode.Everything ? FilterMode.SkipIgnored
            : Mode == FilterMode.SkipIgnored ? FilterMode.OnlySelected
            : FilterMode.Everything);
        return Mode;
    }

    internal static int ClearList(HashSet<string> list)
    {
        var n = list.Count;
        list.Clear();
        Changed($"{ListName(list)} list cleared");
        return n;
    }

    internal static void ApplyDefaults()
    {
        ReadDefaults();
        Changed("lists replaced by the config defaults");
    }

    internal static string ListName(HashSet<string> list) => ReferenceEquals(list, Ignored) ? "Ignored" : "Selected";

    // ---------------------------------------------------------------- verdicts

    // Key of a prefab in active list? Cached per prefab instance id.
    private static bool IsListed(GameObject prefab)
    {
        var id = prefab.GetInstanceID();
        if (ListedByPrefab.TryGetValue(id, out var listed))
        {
            return listed;
        }
        var list = ActiveList;
        listed = list != null && list.Contains(prefab.name);
        ListedByPrefab[id] = listed;
        return listed;
    }

    // Drop without drop prefab (ObjectDB lookup failed in ItemDrop.Awake, e.g. unregistered modded prefab):
    // key from object name, cached per drop instance.
    private static bool IsListedByDrop(ItemDrop drop)
    {
        var id = drop.GetInstanceID();
        if (ListedByDrop.TryGetValue(id, out var listed))
        {
            return listed;
        }
        if (ListedByDrop.Count >= MaxDropCache)
        {
            ListedByDrop.Clear();
        }
        var list = ActiveList;
        listed = list != null && list.Contains(Utils.GetPrefabName(drop.gameObject));
        ListedByDrop[id] = listed;
        return listed;
    }

    // Lists alone say "skip this drop"? (No harvest grace.)
    internal static bool ListBlocks(ItemDrop drop)
    {
        var mode = Mode;
        if (mode == FilterMode.Everything)
        {
            return false;
        }
        var prefab = drop.m_itemData.m_dropPrefab;
        var listed = prefab != null ? IsListed(prefab) : IsListedByDrop(drop);
        return mode == FilterMode.SkipIgnored ? listed : !listed;
    }

    // Auto pickup must skip this drop? Harvest grace (tag set when drop was born) only asked when lists say block.
    internal static bool Blocks(ItemDrop drop)
    {
        if (!ListBlocks(drop))
        {
            return false;
        }
        return !Plugin.ExemptHarvestOn || !HarvestGrace.IsTagged(drop);
    }

    // Slot badge: item type in active list (nothing in Everything mode).
    internal static bool IsMarked(ItemDrop.ItemData item)
    {
        if (Mode == FilterMode.Everything)
        {
            return false;
        }
        var prefab = item.m_dropPrefab;
        return prefab != null && IsListed(prefab);
    }

    // ---------------------------------------------------------------- text helpers

    internal static string ModeLabel(FilterMode mode) =>
        mode == FilterMode.SkipIgnored ? "Skip ignored" : mode == FilterMode.OnlySelected ? "Only selected" : "Everything";

    // "Stone (Stone), Wood (Wood)" style list for console. Sorted by key.
    internal static string DescribeForConsole(HashSet<string> set)
    {
        if (set.Count == 0)
        {
            return "none";
        }
        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            var key = list[i];
            var shown = ItemCatalog.DisplayName(key);
            sb.Append(key);
            if (!string.Equals(shown, key, StringComparison.Ordinal))
            {
                sb.Append(" (").Append(shown).Append(')');
            }
        }
        return sb.ToString();
    }
}
