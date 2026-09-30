using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsMovesetMod;

// Me = which animation each move play (design 2.6). Animation = vanilla Animator trigger name, so every peer (mod or
// not) play same clip from the SetTrigger RPC.
//   Settings: one drop-down per (move, family). List = that setting's own default first, then Off, then the other
//   melee triggers. BepInEx put a value not in list back to FIRST value (AcceptableValueList.Clamp): typo = default.
//   Resolve: effective trigger per (move, family), checked against local player's Animator (its Trigger parameters,
//   same test as ZSyncAnimation.HasParameter). Cache for one rules snapshot + one local Player (new settings, server
//   rules or respawn = rebuild). Bad name = family default, bad default = Off; one Warning per (move, family) per
//   game session (server's rules: warning say ask the server admin).
internal static class MoveTriggers
{
    internal const string Off = "Off";

    // 39 vanilla melee attack triggers of the player Animator (runtime dump, Valheim 1.0.16). Order = drop-down order.
    // Left out on purpose: spear_throw (throw weapon away), tool triggers, suffix-less chain names no weapon fire.
    internal static readonly string[] Melee =
    {
        "swing_longsword0", "swing_longsword1", "swing_longsword2", "sword_secondary", "mace_secondary",
        "swing_axe0", "swing_axe1", "swing_axe2", "axe_secondary",
        "battleaxe_attack0", "battleaxe_attack1", "battleaxe_attack2", "battleaxe_secondary",
        "dualaxes0", "dualaxes1", "dualaxes2", "dualaxes3", "dualaxes_secondary",
        "greatsword0", "greatsword1", "greatsword2", "greatsword_secondary",
        "atgeir_attack0", "atgeir_attack1", "atgeir_attack2", "atgeir_secondary",
        "knife_stab0", "knife_stab1", "knife_stab2", "knife_secondary",
        "dual_knives0", "dual_knives1", "dual_knives2", "dual_knives_secondary",
        "spear_poke",
        "unarmed_attack0", "unarmed_attack1", "unarmed_kick",
        "swing_sledge",
    };

    // Defaults (design table 2.2, Decisions 4 and 12), family order. Jump = family's last combo step, roll = second
    // step; one-animation families (spears, sledges) and battleaxe roll = Off.
    private static readonly string[] JumpDefaults =
    {
        "swing_longsword2", // Swords
        "swing_longsword2", // Maces
        "swing_axe2",       // Axes
        "battleaxe_attack2", // Battleaxes
        "dualaxes3",        // DualAxes
        "greatsword2",      // Greatswords
        "atgeir_attack2",   // Atgeirs
        "knife_stab2",      // Knives
        "dual_knives2",     // DualKnives
        Off,                // Spears
        "unarmed_attack1",  // Fists
        Off,                // Sledges
    };

    private static readonly string[] RollDefaults =
    {
        "swing_longsword1", // Swords
        "swing_longsword1", // Maces
        "swing_axe1",       // Axes
        Off,                // Battleaxes
        "dualaxes1",        // DualAxes
        "greatsword1",      // Greatswords
        "atgeir_attack1",   // Atgeirs
        "knife_stab1",      // Knives
        "dual_knives1",     // DualKnives
        Off,                // Spears
        "unarmed_attack1",  // Fists
        Off,                // Sledges
    };

    // One drop-down list per distinct default (default first). Built at bind time, kept.
    private static readonly Dictionary<string, AcceptableValueList<string>> Lists =
        new Dictionary<string, AcceptableValueList<string>>();

    // Cache (Resolve). Null entry = Off.
    private static readonly string[,] Cache = new string[Moves.Count, Families.Count];
    private static readonly bool[,] Warned = new bool[Moves.Count, Families.Count];
    private static MoveRules _cacheRules;
    private static Player _cachePlayer;

    // Trigger names of local Animator, read once per rebuild: Animator.parameters copy whole array (~150 objects)
    // each read, so me no call ZSyncAnimation.HasParameter once per name. Reused set.
    private static readonly HashSet<string> AnimatorTriggers = new HashSet<string>(StringComparer.Ordinal);

#if DEBUG
    // Self test: pretend Animator answer (null = real HasParameter).
    internal static Func<string, bool> TestHasParameter { get; set; }

    // Self test: warnings logged since last ResetWarnings.
    internal static int WarningCount;

    internal static void ResetWarnings()
    {
        Array.Clear(Warned, 0, Warned.Length);
        WarningCount = 0;
    }
#endif

    // Family default of one move. Off for unknown move or family.
    internal static string Default(MoveKind kind, WeaponFamily family)
    {
        var f = Families.Index(family);
        if (f < 0)
        {
            return Off;
        }
        switch (kind)
        {
            case MoveKind.Jump:
                return JumpDefaults[f];
            case MoveKind.Roll:
                return RollDefaults[f];
            default:
                return Off;
        }
    }

    // Drop-down for setting with this default: default first, then Off, then every other melee trigger.
    internal static AcceptableValueList<string> ValuesFor(string defaultValue)
    {
        if (Lists.TryGetValue(defaultValue, out var list))
        {
            return list;
        }
        var values = new List<string>(Melee.Length + 1) { defaultValue };
        if (defaultValue != Off)
        {
            values.Add(Off);
        }
        foreach (var name in Melee)
        {
            if (name != defaultValue)
            {
                values.Add(name);
            }
        }
        list = new AcceptableValueList<string>(values.ToArray());
        Lists[defaultValue] = list;
        return list;
    }

    // Name one of the drop-down values? (Off or a listed trigger.) Linear, only at cache rebuild.
    internal static bool IsListed(string name)
    {
        if (name == Off)
        {
            return true;
        }
        foreach (var t in Melee)
        {
            if (t == name)
            {
                return true;
            }
        }
        return false;
    }

    // Settings, server rules or feature on/off: rebuild at next Resolve.
    internal static void Invalidate()
    {
        _cacheRules = null;
        _cachePlayer = null;
    }

    // Trigger the move play for this family under these rules, or null (Off: normal swing). Hot path = two
    // reference compares and an array read; rebuild (one copy of Animator parameter list, then set lookups) only when
    // rules snapshot or local Player changed.
    internal static string Resolve(MoveRules rules, MoveKind kind, WeaponFamily family)
    {
        var k = Moves.Index(kind);
        var f = Families.Index(family);
        if (rules == null || k < 0 || k >= Moves.Count || f < 0)
        {
            return null;
        }
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return null;
        }
        if (!ReferenceEquals(rules, _cacheRules) || !ReferenceEquals(player, _cachePlayer))
        {
            Build(rules, player);
        }
        return Cache[k, f];
    }

    // Step k of chain `baseName` (n levels) when trigger = baseName + k, k < n, n > 1; else -1. Move that play own
    // family chain step count as that step (combo continue from it, design 2.5).
    internal static int StepOf(string trigger, string baseName, int chainLevels)
    {
        if (chainLevels <= 1 || string.IsNullOrEmpty(trigger) || string.IsNullOrEmpty(baseName)
            || trigger.Length <= baseName.Length || !trigger.StartsWith(baseName, StringComparison.Ordinal))
        {
            return -1;
        }
        var step = 0;
        for (var i = baseName.Length; i < trigger.Length; i++)
        {
            var c = trigger[i];
            if (c < '0' || c > '9' || step > 1000)
            {
                return -1;
            }
            step = step * 10 + (c - '0');
        }
        return step < chainLevels ? step : -1;
    }

    private static void Build(MoveRules rules, Player player)
    {
        var zanim = player.m_zanim;
        var animator = zanim != null ? zanim.m_animator : null;
        if (animator == null || !ReadTriggers(animator))
        {
            // No Animator (sync) yet, or Animator not set up (no parameters: every name would look missing): all
            // Off, no warning, try again next time (cache stay invalid).
            Array.Clear(Cache, 0, Cache.Length);
            Invalidate();
            return;
        }
        var skipList = IsTestRules(rules);
        var fromServer = ServerRules.FromServer(rules);
        foreach (var kind in Moves.All)
        {
            var k = Moves.Index(kind);
            var configured = kind == MoveKind.Jump ? rules.JumpTriggers : rules.RollTriggers;
            foreach (var family in Families.All)
            {
                var f = Families.Index(family);
                Cache[k, f] = Check(kind, family, configured[f], skipList, fromServer);
            }
        }
        AnimatorTriggers.Clear(); // names only needed during rebuild
        _cacheRules = rules;
        _cachePlayer = player;
    }

    // Fill AnimatorTriggers from one copy of the parameter list (same test as ZSyncAnimation.HasParameter: type
    // Trigger, exact name). False = no parameter at all.
    private static bool ReadTriggers(Animator animator)
    {
        AnimatorTriggers.Clear();
        var parameters = animator.parameters;
        if (parameters == null || parameters.Length == 0)
        {
            return false;
        }
        foreach (var p in parameters)
        {
            if (p != null && p.type == AnimatorControllerParameterType.Trigger && !string.IsNullOrEmpty(p.name))
            {
                AnimatorTriggers.Add(p.name);
            }
        }
        return true;
    }

    private static string Check(MoveKind kind, WeaponFamily family, string configured, bool skipList, bool fromServer)
    {
        if (configured == Off)
        {
            return null;
        }
        if (!string.IsNullOrEmpty(configured) && (skipList || IsListed(configured)) && HasTrigger(configured))
        {
            return configured;
        }
        var def = Default(kind, family);
        var fallback = def != Off && def != configured && HasTrigger(def) ? def : null;
        Warn(kind, family, configured, fallback, fromServer);
        return fallback;
    }

    private static bool HasTrigger(string name)
    {
#if DEBUG
        if (TestHasParameter != null)
        {
            return TestHasParameter(name);
        }
#endif
        return AnimatorTriggers.Contains(name);
    }

    private static bool IsTestRules(MoveRules rules)
    {
#if DEBUG
        return rules != null && ReferenceEquals(rules, ServerRules.TestRules);
#else
        return false;
#endif
    }

    private static void Warn(MoveKind kind, WeaponFamily family, string configured, string fallback, bool fromServer)
    {
        var k = Moves.Index(kind);
        var f = Families.Index(family);
        if (Warned[k, f])
        {
            return;
        }
        Warned[k, f] = true;
#if DEBUG
        WarningCount++;
#endif
        var what = $"the {Moves.Name(kind)} of {Families.Label(family)}";
        var result = fallback != null
            ? $"{what} uses {fallback} instead"
            : $"{what} stays a normal swing";
        Log.Warning($"\"{configured}\" is not an attack animation of this game; {result}. "
                    + Moves.AnimationAdvice(kind, fromServer));
    }
}
