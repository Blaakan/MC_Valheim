using System;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.SneakAmbushMod;

internal enum CueKind
{
    Still,
    Foliage,
    Fog,
    Mist,
    Smoke,
}

// Me = one stealth status icon (display only, change nothing). SEMan add a CLONE of the template (MemberwiseClone),
// so me never read numbers stored on me: text and tooltip come from live state and rules (StealthCues). Hud call
// GetIconText every frame: cached strings, no allocation while unchanged. Every override catch own errors.
internal sealed class StealthCue : StatusEffect
{
    internal CueKind Kind;

    public override string GetIconText()
    {
        try
        {
            return StealthCues.IconText(Kind);
        }
        catch (Exception e)
        {
            PatchGuard.Report("StealthCue.GetIconText", e);
            return "";
        }
    }

    public override string GetTooltipString()
    {
        try
        {
            return StealthCues.Tooltip(Kind);
        }
        catch (Exception e)
        {
            PatchGuard.Report("StealthCue.GetTooltipString", e);
            return "";
        }
    }
}

// Me = the five stealth cues (E3, G4): Holding still, In foliage, Fog, In the mist (only while crouching), In smoke
// (whenever traced point in an active cloud). Templates made once per session, object name
// "<guid>.<Cue>" = identity (name hash). UpdateStealth postfix call Sync at each refresh: compare wanted set with
// HaveStatusEffect, so death (clear all) and other mods cannot desync. ShowStealthCues off = no icons, bonuses stay.
internal static class StealthCues
{
    private static readonly StealthCue[] Templates = new StealthCue[5];
    private static readonly string[] Names = { "Holding still", "In foliage", "Fog", "In the mist", "In smoke" };
    private static readonly string[] Ids = { "HoldingStill", "InFoliage", "Fog", "InMist", "InSmoke" };
    private static readonly int[] Hashes = BuildHashes();

    // Icon text cache per cue: last number shown (-1 = "max", int.MinValue = none yet) and its string.
    private static readonly int[] TextValue = { int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue };
    private static readonly string[] TextCache = { "", "", "", "", "" };

#if DEBUG
    // Self test force icons on or off (never the config file). Null = personal setting ShowStealthCues.
    internal static bool? TestShowCues { get; set; }
#endif

    // Personal setting ShowStealthCues (not bound yet = on), or the self test override.
    private static bool ShowSetting
    {
        get
        {
#if DEBUG
            if (TestShowCues.HasValue)
            {
                return TestShowCues.Value;
            }
#endif
            return Plugin.ShowStealthCues == null || Plugin.ShowStealthCues.Value;
        }
    }

    // Name hash = status effect identity (StatusEffect.NameHash hash the object name).
    internal static int NameHash(CueKind kind) => Hashes[(int)kind];

    private static int[] BuildHashes()
    {
        var hashes = new int[Ids.Length];
        for (var i = 0; i < Ids.Length; i++)
        {
            hashes[i] = (ModInfo.Guid + "." + Ids[i]).GetStableHashCode();
        }
        return hashes;
    }

    // Is this cue on the player now (self tests, display)?
    internal static bool Has(Player player, CueKind kind)
    {
        var seman = player != null ? player.GetSEMan() : null;
        return seman != null && seman.HaveStatusEffect(NameHash(kind));
    }

    // At each stealth refresh of the local player (UpdateStealth postfix).
    internal static void Sync(Player player, AmbushRules rules)
    {
        var seman = player.GetSEMan();
        if (seman == null)
        {
            return;
        }
        var show = !rules.IsPending && ShowSetting;
        var now = Time.time;
        if (StealthState.FoliageTouching || StealthState.FoliageCrown)
        {
            StealthState.FoliageSeenAt = now;
        }
        if (StealthState.InSmoke)
        {
            StealthState.SmokeSeenAt = now;
        }
        // Fog icon: on from 2%, off below 1% (no flicker around one value).
        var fog = StealthState.Crouching ? StealthState.FogPercent(rules) : 0;
        StealthState.FogShown = StealthState.FogShown ? fog >= 1 : fog >= 2;

        Set(player, seman, CueKind.Still, show && StealthState.Crouching && StealthState.StillActive);
        Set(player, seman, CueKind.Foliage, show && now - StealthState.FoliageSeenAt <= StealthState.CueLinger);
        Set(player, seman, CueKind.Fog, show && StealthState.FogShown);
        Set(player, seman, CueKind.Mist, show && StealthState.Crouching && StealthState.InMist);
        Set(player, seman, CueKind.Smoke, show && now - StealthState.SmokeSeenAt <= StealthState.CueLinger);
    }

    // Feature off: every cue off the player.
    internal static void RemoveAll(Player player)
    {
        var seman = player != null ? player.GetSEMan() : null;
        if (seman == null)
        {
            return;
        }
        for (var i = 0; i < Templates.Length; i++)
        {
            var hash = NameHash((CueKind)i);
            if (seman.HaveStatusEffect(hash))
            {
                seman.RemoveStatusEffect(hash, quiet: true);
            }
        }
    }

    private static void Set(Player player, SEMan seman, CueKind kind, bool want)
    {
        var hash = NameHash(kind);
        var have = seman.HaveStatusEffect(hash);
        if (want == have)
        {
            return;
        }
        if (!want)
        {
            seman.RemoveStatusEffect(hash, quiet: true);
            return;
        }
        var template = Template(player, kind);
        if (template != null && template.m_icon != null)
        {
            seman.AddStatusEffect(template);
        }
    }

    // Template of a cue, made at first use (self test read its icon too).
    internal static StealthCue Template(Player player, CueKind kind)
    {
        var template = Templates[(int)kind];
        if (template == null)
        {
            template = ScriptableObject.CreateInstance<StealthCue>();
            template.name = ModInfo.Guid + "." + Ids[(int)kind];
            template.hideFlags = HideFlags.HideAndDontSave;
            template.Kind = kind;
            template.m_name = Names[(int)kind];
            template.m_ttl = 0f;
            template.m_tooltip = "";
            Templates[(int)kind] = template;
        }
        if (template.m_icon == null)
        {
            template.m_icon = FindIcon(player, kind);
        }
        return template;
    }

    // Vanilla sprites by name, each falling back to the Sneak skill icon (looks unverified: self test note them).
    private static Sprite FindIcon(Player player, CueKind kind)
    {
        Sprite icon = null;
        switch (kind)
        {
            case CueKind.Foliage:
                icon = ItemIcon("Fiddleheadfern");
                break;
            case CueKind.Fog:
                var db = ObjectDB.instance;
                var wet = db != null ? db.GetStatusEffect("Wet".GetStableHashCode()) : null;
                icon = wet != null ? wet.m_icon : null;
                break;
            case CueKind.Mist:
                icon = ItemIcon("Wisp");
                break;
            case CueKind.Smoke:
                icon = SmokeContent.Icon;
                break;
        }
        return icon != null ? icon : SneakIcon(player);
    }

    private static Sprite ItemIcon(string prefabName)
    {
        var db = ObjectDB.instance;
        var prefab = db != null ? db.GetItemPrefab(prefabName) : null;
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
        {
            return null;
        }
        var icons = drop.m_itemData.m_shared.m_icons;
        return icons != null && icons.Length > 0 ? icons[0] : null;
    }

    private static Sprite SneakIcon(Player player)
    {
        var skills = player != null ? player.GetSkills() : null;
        if (skills == null || skills.m_skills == null)
        {
            return null;
        }
        foreach (var def in skills.m_skills)
        {
            if (def != null && def.m_skill == Skills.SkillType.Sneak)
            {
                return def.m_icon;
            }
        }
        return null;
    }

    // ---------- live texts ----------

    // Every frame per shown cue: one int compare when unchanged.
    internal static string IconText(CueKind kind)
    {
        var rules = ServerRules.Current;
        int value;
        switch (kind)
        {
            case CueKind.Still:
                value = rules.StillBonus;
                break;
            case CueKind.Foliage:
                value = StealthState.FoliageTouching && rules.FoliageBonus > 0 ? rules.FoliageBonus : 0;
                break;
            case CueKind.Fog:
                value = StealthState.FogPercent(rules);
                break;
            default:
                return "";
        }
        if (value <= 0)
        {
            return "";
        }
        if (StealthState.Capped)
        {
            value = -1;
        }
        var i = (int)kind;
        if (TextValue[i] != value)
        {
            TextValue[i] = value;
            TextCache[i] = value < 0 ? "max" : "-" + value + "%";
        }
        return TextCache[i];
    }

    // Active effects page (inventory): built on open, allocation fine.
    internal static string Tooltip(CueKind kind)
    {
        var rules = ServerRules.Current;
        string text;
        switch (kind)
        {
            case CueKind.Still:
                text = $"You are crouched and not moving: creatures see you at {100 - rules.StillBonus}% of the "
                       + "distance, so they must come much closer to notice you. "
                       + (rules.StillEndsAtOnce
                           ? "Moving ends it at once."
                           : "Moving ends it; your stealth bar then rises at its normal speed.");
                break;
            case CueKind.Foliage:
                text = "Bushes and tree crowns block creatures' sight when they are between you and them, and they "
                       + "shade you from the sun and moon (in daylight your stealth bar drops).";
                if (rules.FoliageBonus > 0)
                {
                    text += $" Touching foliage also makes creatures see you at {100 - rules.FoliageBonus}% of the "
                            + "distance.";
                }
                break;
            case CueKind.Fog:
                text = $"The fog hides you: creatures see you at {100 - StealthState.FogPercent(rules)}% of the "
                       + "distance. Thicker fog hides you better.";
                break;
            case CueKind.Mist:
                return "Creatures without mist sight cannot see you from more than 10 m away.";
            case CueKind.Smoke:
                return "Creatures outside the smoke cannot " + (rules.BlocksHearing ? "see or hear" : "see")
                       + " you (sleeping ones still wake up when you come "
                       + $"close). Creatures in the smoke with you notice you only within "
                       + $"{rules.InsideSightRange.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} m. "
                       + "A creature you hit sees you. Bosses are not fooled.";
            default:
                return "";
        }
        if (StealthState.Capped)
        {
            text += " You are already as hidden as this mod allows: more cover adds nothing now.";
        }
        return text;
    }
}
