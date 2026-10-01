using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace MC.Exploration.SailingSkillMod;

// Me = the Sailing skill itself: id, definition, words, icon, console cheats. Vanilla keep skills in an enum; me add
// one number that is no enum name. Always-on patches (Patches/SkillRegistrationPatches.cs) make vanilla accept it:
// IsSkillValid (Load keep saved level), GetSkillDef + m_skills (Save, Skills panel, level-up message need m_info).
// Death penalty, world skill-gain modifier, Rested bonus, level-up message, Skills panel: all vanilla, no patch.
// Mod off: registration stay, level kept; only XP and effects stop. Mod removed: vanilla Load drop the level, next save
// lose it (README say so).
internal static class SailingSkill
{
    // NEVER change: written into character saves. = Math.Abs("MC.Exploration.Sailing.Skill".GetStableHashCode())
    // (Jotunn rule: from our permanent GUID, positive, > 999). Self test sailing.skill check the formula.
    internal const int Id = 697262889;
    internal const Skills.SkillType Type = (Skills.SkillType)Id;

    // Vanilla build name token itself: "$skill_" + type.ToString().ToLower() = number for a non-enum value.
    internal const string NameKey = "skill_697262889";
    internal const string DescriptionKey = "mc_sailing_skill_desc";
    internal const string DisplayName = "Sailing";
    internal const string Description =
        "Earned at the helm while your ship moves. The higher it is, the better the ship you steer catches the wind at "
        + "poor angles, turns, stops and speeds up; the wider you reveal the map while aboard; and the less damage a "
        + "ship with you aboard takes.";

    // Names the console accept (after vanilla "raiseskill"/"resetskill"): our name, and the number (vanilla dev
    // helper ItemSets reset every def of m_skills by m_skill.ToString()).
    internal const string CheatName = "Sailing";
    private const string CheatNumber = "697262889";

    // One shared definition: every Skills (main menu preview, local player) point at it, icon set once for all.
    internal static readonly Skills.SkillDef Def = new Skills.SkillDef
    {
        m_skill = Type,
        m_description = "$" + DescriptionKey,
        m_increseStep = 1f,
    };

    private static Action<Localization, string, string> _addWord;
    private static FieldInfo _cacheField;
    private static FieldInfo _instanceField;
    private static bool _iconTried;

    internal static bool IsCheatName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        var n = name.Trim();
        return string.Equals(n, CheatName, StringComparison.OrdinalIgnoreCase) || n == CheatNumber;
    }

    internal static bool IsAll(string name) =>
        name != null && string.Equals(name.Trim(), "all", StringComparison.OrdinalIgnoreCase);

    // ---------- words ----------

    // Plugin BindConfig (end). assembly_guiutils not publicized: private AddWord, m_cache, m_instance by reflection.
    // Name gone after game update = throw here: framework show Error status, feature off this session. Registration
    // patches stay (they need no word): level kept, name show as [skill_697262889].
    internal static void InitWords()
    {
        var addWord = AccessTools.Method(typeof(Localization), "AddWord", new[] { typeof(string), typeof(string) });
        var cache = AccessTools.Field(typeof(Localization), "m_cache");
        var instance = AccessTools.Field(typeof(Localization), "m_instance");
        if (addWord == null || cache == null || instance == null || !instance.IsStatic)
        {
            throw new MissingMemberException("Localization.AddWord, m_cache or m_instance not found: the Sailing skill name "
                                             + "cannot be added.");
        }
        _addWord = AccessTools.MethodDelegate<Action<Localization, string, string>>(addWord, null, false);
        _cacheField = cache;
        _instanceField = instance;
        // Localization made already (another mod touched it): add now. Else SetupLanguage postfix add them when it is
        // made. Me never create it (Localization.instance too early = locale from platform before it has a user).
        if (_instanceField.GetValue(null) is Localization loc)
        {
            AddWords(loc);
        }
    }

    // SetupLanguage postfix (every language load: vanilla Clear() on language change wipe every word) and InitWords.
    internal static void AddWords(Localization loc)
    {
        if (loc == null || _addWord == null)
        {
            return;
        }
        _addWord(loc, NameKey, DisplayName);
        _addWord(loc, DescriptionKey, Description);
        // "[skill_697262889]" may sit in the translate cache from a lookup before now.
        if (_cacheField.GetValue(loc) is LRUCache<string> cache)
        {
            cache.EvictAll();
        }
    }

    // ---------- definition and icon ----------

    // Skills.Awake postfix: def in this instance's m_skills (vanilla GetSkillDef find it, other mods reading m_skills
    // see it), icon on first chance.
    internal static void Register(Skills skills)
    {
        if (skills == null)
        {
            return;
        }
        var defs = skills.m_skills;
        if (defs == null)
        {
            return;
        }
        var have = false;
        for (var i = 0; i < defs.Count; i++)
        {
            if (defs[i] != null && defs[i].m_skill == Type)
            {
                have = true;
                break;
            }
        }
        if (!have)
        {
            defs.Add(Def);
        }
        EnsureIcon();
    }

    // Code-drawn sail (SkillIcon); drawing failed = Karve piece icon (needs ZNetScene: in world). Headless server: no
    // sprite at all (nobody see it). Kept whole session: def always registered.
    internal static void EnsureIcon()
    {
        if (Def.m_icon != null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            return;
        }
        if (!_iconTried)
        {
            _iconTried = true;
            try
            {
                Def.m_icon = SkillIcon.Get();
            }
            catch (Exception e)
            {
                Log.Warning($"Could not draw the Sailing skill icon; using the Karve's icon instead. {e.Message}");
            }
        }
        if (Def.m_icon == null)
        {
            Def.m_icon = FallbackIcon();
        }
    }

    private static Sprite FallbackIcon()
    {
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            return null;
        }
        foreach (var name in new[] { "Karve", "VikingShip", "Raft" })
        {
            var prefab = scene.GetPrefab(name);
            var piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (piece != null && piece.m_icon != null)
            {
                return piece.m_icon;
            }
        }
        return null;
    }

    // ---------- console ----------

    // raiseskill sailing N: vanilla steps for one skill (Skills.CheatRaiseSkill), our name in the message.
    internal static void CheatRaise(Skills skills, float value, bool showMessage)
    {
        var skill = skills.GetSkill(Type);
        skill.m_level = Mathf.Clamp(skill.m_level + value, 0f, 100f);
        if (skills.m_useSkillCap)
        {
            skills.RebalanceSkills(Type);
        }
        if (showMessage)
        {
            var player = skills.m_player;
            if (player != null)
            {
                player.Message(MessageHud.MessageType.TopLeft, $"Skill increased {DisplayName}: {(int)skill.m_level}", 0,
                    Def.m_icon);
            }
            Print($"Skill {DisplayName} = {skill.m_level}");
        }
    }

    // resetskill sailing: entry gone (vanilla ResetSkill), like other skills.
    internal static void CheatReset(Skills skills, bool print)
    {
        skills.ResetSkill(Type);
        if (print)
        {
            Print($"Skill {DisplayName} reset");
        }
    }

    // Terminal.InitTerminal postfix: tab list of raiseskill / resetskill get "Sailing" (vanilla list = enum names).
    // Wrap the fetcher once; vanilla cache list at first use: me clear cache so next Tab fetch again.
    // Plugin BindConfig also call me when the console was set up before this plugin loaded (postfix too late then).
    private static bool _commandsWrapped;

    internal static void WrapCommandsIfReady()
    {
        if (Terminal.m_terminalInitialized)
        {
            WrapCommands();
        }
    }

    internal static void WrapCommands()
    {
        if (_commandsWrapped)
        {
            return;
        }
        var commands = Terminal.commands;
        if (commands == null)
        {
            return;
        }
        _commandsWrapped = true;
        Wrap(commands, "raiseskill");
        Wrap(commands, "resetskill");
    }

    private static void Wrap(Dictionary<string, Terminal.ConsoleCommand> commands, string key)
    {
        if (!commands.TryGetValue(key, out var command) || command == null)
        {
            Log.Warning($"Console command {key} not found: Tab will not complete {DisplayName} there.");
            return;
        }
        var inner = command.m_tabOptionsFetcher;
        command.m_tabOptionsFetcher = () =>
        {
            List<string> list = null;
            try
            {
                list = inner != null ? inner() : null;
            }
            catch (Exception e)
            {
                PatchGuard.Report("Sailing tab options", e);
            }
            list ??= new List<string>();
            if (!list.Contains(CheatName))
            {
                list.Add(CheatName);
            }
            return list;
        };
        command.m_tabOptions = null;
    }

    private static void Print(string text)
    {
        var console = global::Console.instance;
        if (console != null)
        {
            console.Print(text);
        }
    }
}
