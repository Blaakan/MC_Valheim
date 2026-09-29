using System;
using System.Collections.Generic;
using System.Text;
using MC.Shared;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = the one strip helper every clone go through (design 3.6, decision 23): side button, window, tabs, rows, detail
// lines, search field. Run while clone sit under an inactive holder (no Awake/Start ran yet). Me remove:
//   - every UIGamePad, UIInputHint, UITooltip, Localize (except the ones in keep): no leftover pad take our group in
//     Start and click a tab on a vanilla key, no leftover glyph, no auto-translate over our text;
//   - every component from a non-vanilla assembly (other mods' additions to the vanilla source);
//   - every Button.onClick: new empty event (cloned persistent prefab calls would still fire otherwise);
// and set Navigation.mode on every Selectable on purpose. What me removed go to Debug log once per kind of clone.
internal static class CloneUtil
{
    private static readonly HashSet<string> LoggedKinds = new HashSet<string>();
    private static readonly Dictionary<string, int> Removed = new Dictionary<string, int>();
    private static readonly List<string> Dumps = new List<string>();

    /// <summary>What Strip removed per kind (first clone of each kind), for the layout dump.</summary>
    internal static IReadOnlyList<string> StripLog => Dumps;

    internal static void Strip(GameObject clone, string kind, Navigation.Mode navigation, params Component[] keep)
    {
        if (clone == null)
        {
            return;
        }
        Removed.Clear();
        foreach (var pad in clone.GetComponentsInChildren<UIGamePad>(true))
        {
            if (pad == null || Kept(pad, keep))
            {
                continue;
            }
            DropObject(pad.m_hint, clone, "pad hint");
            Kill(pad);
        }
        foreach (var hint in clone.GetComponentsInChildren<UIInputHint>(true))
        {
            if (hint == null || Kept(hint, keep))
            {
                continue;
            }
            DropObject(hint.m_gamepadHint, clone, "input hint object");
            DropObject(hint.m_mouseKeyboardHint, clone, "input hint object");
            DropObject(hint.m_gamepadMouseHint, clone, "input hint object");
            if (hint.m_inputLayoutSettings != null)
            {
                foreach (var s in hint.m_inputLayoutSettings)
                {
                    if (s != null)
                    {
                        DropObject(s.m_hintObject, clone, "input hint object");
                    }
                }
            }
            Kill(hint);
        }
        foreach (var tip in clone.GetComponentsInChildren<UITooltip>(true))
        {
            if (tip != null && !Kept(tip, keep))
            {
                Kill(tip);
            }
        }
        foreach (var loc in clone.GetComponentsInChildren<Localize>(true))
        {
            if (loc != null && !Kept(loc, keep))
            {
                Kill(loc);
            }
        }
        // Foreign components: reverse order, so a component that need another one go first.
        var all = clone.GetComponentsInChildren<Component>(true);
        for (var i = all.Length - 1; i >= 0; i--)
        {
            var c = all[i];
            if (c == null || Kept(c, keep) || IsVanilla(c.GetType()))
            {
                continue;
            }
            Kill(c);
        }
        foreach (var button in clone.GetComponentsInChildren<Button>(true))
        {
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
            }
        }
        foreach (var s in clone.GetComponentsInChildren<Selectable>(true))
        {
            if (s != null)
            {
                s.navigation = new Navigation { mode = navigation };
            }
        }
        LogOnce(kind);
    }

    /// <summary>Vanilla = Unity, TextMeshPro and the game's own assemblies. Everything else = some mod's addition.</summary>
    internal static bool IsVanilla(Type t)
    {
        var name = t.Assembly.GetName().Name ?? "";
        return name.StartsWith("assembly_", StringComparison.Ordinal)
               || name.StartsWith("UnityEngine", StringComparison.Ordinal)
               || name.StartsWith("Unity.", StringComparison.Ordinal)
               || name == "gui_framework" || name == "SoftReferenceableAssets" || name == "Splatform";
    }

    private static bool Kept(Component c, Component[] keep)
    {
        if (keep == null)
        {
            return false;
        }
        foreach (var k in keep)
        {
            if (k != null && ReferenceEquals(k, c))
            {
                return true;
            }
        }
        return false;
    }

    private static void Kill(Component c)
    {
        var key = c.GetType().Name;
        try
        {
            Object.DestroyImmediate(c);
            Removed[key] = Removed.TryGetValue(key, out var n) ? n + 1 : 1;
        }
        catch (Exception e)
        {
            Log.Debug($"Clone strip: could not remove {key}: {e.Message}");
        }
    }

    // Hint object inside the clone = our copy: remove. Outside = vanilla's own (Instantiate keep outside refs): leave it.
    private static void DropObject(GameObject obj, GameObject clone, string what)
    {
        if (obj == null || obj == clone || !obj.transform.IsChildOf(clone.transform))
        {
            return;
        }
        Object.DestroyImmediate(obj);
        Removed[what] = Removed.TryGetValue(what, out var n) ? n + 1 : 1;
    }

    private static void LogOnce(string kind)
    {
        if (!LoggedKinds.Add(kind))
        {
            return;
        }
        var sb = new StringBuilder();
        sb.Append("Clone strip (").Append(kind).Append("): ");
        if (Removed.Count == 0)
        {
            sb.Append("nothing to remove");
        }
        else
        {
            var first = true;
            foreach (var kv in Removed)
            {
                sb.Append(first ? "" : ", ").Append(kv.Key).Append(" x").Append(kv.Value);
                first = false;
            }
        }
        sb.Append('.');
        var line = sb.ToString();
        Dumps.Add(line);
        Log.Debug(line);
    }

    /// <summary>Feature off or new session: log each kind again next time.</summary>
    internal static void Reset()
    {
        LoggedKinds.Clear();
        Dumps.Clear();
    }
}
