using System;
using BepInEx.Configuration;
using UnityEngine;

namespace MC.Shared;

// Me = the well-known "ConfigurationManagerAttributes" shape. ConfigurationManager (in-game F1 config UI)
// read these fields by name through reflection, so no dependency needed. Pass one in ConfigDescription tags
// (FIRST tag: managers stop reading tags at the first one they do not know).
// Builds checked 2026-10-02 (docs/modding/framework.md, "ConfigurationManager"): Nexus 740 (aedenthorn 0.5.0),
// shudnal's Valheim Configuration Manager, upstream BepInEx.ConfigurationManager. All three read public fields of a
// class with this exact name, any namespace, so every mod keep its own internal copy.
#pragma warning disable CS0649
internal sealed class ConfigurationManagerAttributes
{
    public bool? ShowRangeAsPercent;
    public Action<ConfigEntryBase> CustomDrawer;
    public bool? Browsable;
    public string Category;
    public object DefaultValue;
    public bool? HideDefaultButton;
    public bool? HideSettingName;
    public string Description;
    public string DispName;
    public int? Order;
    public bool? ReadOnly;
    public bool? IsAdvanced;
    public Func<object, string> ObjToStr;
    public Func<string, object> StrToObj;

    private static GUIStyle _wrapLabel;

    // CustomDrawer for values the mod write itself (Status): plain wrapped text, nothing to click or type in.
    // Manager call me inside its OnGUI.
    internal static void ReadOnlyText(ConfigEntryBase entry)
    {
        _wrapLabel ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
        // Manager may scale its skin (shudnal's follow the game's UI scale): take font of this moment, not first one.
        _wrapLabel.font = GUI.skin.label.font;
        _wrapLabel.fontSize = GUI.skin.label.fontSize;
        _wrapLabel.normal.textColor = GUI.skin.label.normal.textColor;
        GUILayout.Label(entry?.BoxedValue?.ToString() ?? "", _wrapLabel, GUILayout.ExpandWidth(true));
    }
}
#pragma warning restore CS0649
