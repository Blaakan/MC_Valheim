using System;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = the Music status effect: "+N comfort" for a while after a good performance (performer and players near).
// Registered in ObjectDB (always on, InstrumentContent) under name SE_MC_Music: the owner's game look it up by hash.
// Comfort itself is added by the Player.GetComfortLevel postfix (ComfortPatches) while the local player has me; me
// only show it: icon text "+3 9:41" (bonus + time left), tooltip. SEMan add a CLONE of the template
// (MemberwiseClone): live numbers come from the rules, never from fields of me. TTL set at each grant from the rules
// (BonusMinutes) before add/reset. Every override catch own errors (HUD call me every frame).
internal sealed class MusicEffect : StatusEffect
{
    internal const string DisplayName = "Music";

    private static int _keyBonus = int.MinValue;
    private static int _keySeconds = int.MinValue;
    private static string _iconCache = "";

    internal static MusicEffect CreateTemplate()
    {
        var se = ScriptableObject.CreateInstance<MusicEffect>();
        // Name first: NameHash cache it on first call, clones copy the cache.
        se.name = InstrumentContent.EffectName;
        se.hideFlags = HideFlags.HideAndDontSave;
        se.m_name = DisplayName;
        se.m_tooltip = "";
        se.m_ttl = MusicRules.Default.BonusMinutes * 60f;
        se.m_startMessageType = MessageHud.MessageType.TopLeft;
        se.m_startMessage = "";
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            se.m_icon = InstrumentIcons.EffectIcon();
        }
        return se;
    }

    public override string GetIconText()
    {
        try
        {
            // Key on ints (bonus, whole seconds left): the vanilla time text only change when the seconds do, so the
            // strings are made once per second, not every frame.
            var bonus = MusicBonus.BonusInForce(ServerRules.Current);
            var seconds = m_ttl > 0f ? Mathf.CeilToInt(m_ttl - m_time) : int.MinValue + 1;
            if (bonus != _keyBonus || seconds != _keySeconds)
            {
                _keyBonus = bonus;
                _keySeconds = seconds;
                var time = base.GetIconText();
                _iconCache = bonus > 0 ? "+" + bonus.ToString(CultureInfo.InvariantCulture) + "  " + time : time;
            }
            return _iconCache;
        }
        catch (Exception e)
        {
            PatchGuard.Report("MusicEffect.GetIconText", e);
            return "";
        }
    }

    public override string GetTooltipString()
    {
        try
        {
            var rules = ServerRules.Current;
            var bonus = MusicBonus.BonusInForce(rules);
            if (bonus <= 0)
            {
                return "Music heard nearby.";
            }
            var text = "A good performance warmed you: +" + bonus.ToString(CultureInfo.InvariantCulture)
                       + " comfort, so Rested lasts longer when you rest";
            return text + ".";
        }
        catch (Exception e)
        {
            PatchGuard.Report("MusicEffect.GetTooltipString", e);
            return "";
        }
    }
}
