using System;
using BepInEx.Configuration;

namespace MC.Shared;

// Me = the well-known "ConfigurationManagerAttributes" shape. ConfigurationManager (in-game F1 config UI)
// read these fields by name through reflection, so no dependency needed. Pass one in ConfigDescription tags.
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
}
#pragma warning restore CS0649
