using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Which animation the shield bash play (design 2.7). Server rule. Order = wire value (layout 2): never reorder, only
// append. ShieldUp (block pose held during the punch) gone 2026-09-30: the pose never showed in the swing (decision
// 33); config file that still say ShieldUp read as ShieldPunch (Plugin list of accepted names).
internal enum BashAnimationKind
{
    ShieldPunch,  // unarmed_attack1 (default, first fallback)
    OtherPunch,   // unarmed_attack0 (last fallback)
    Kick,         // unarmed_kick
    Custom,       // BashCustomTrigger, checked at runtime (player attack trigger only)
}

// One line of the Towers list: prefab name + blunt damage of its bash.
internal readonly struct TowerEntry
{
    internal readonly string Prefab;
    internal readonly float BashDamage;

    internal TowerEntry(string prefab, float bashDamage)
    {
        Prefab = prefab;
        BashDamage = bashDamage;
    }
}

// Me = every gameplay number of the mod in one snapshot (design 5): tower list, block, brace, bash. Server send its
// own to every player (ServerRules): same tower shields for everybody. Snapshot never change after Seal (Own, TryRead,
// With): game code may keep a reference and compare Key.
internal sealed class TowerRules
{
    // Ranges: config binding and wire clamp use same numbers.
    internal const float MinBlockArmorMultiplier = 1f;
    internal const float MaxBlockArmorMultiplier = 10f;
    internal const int MaxBlockForcePercent = 200;
    internal const int MaxCarrySlowPercent = 40;
    internal const int MaxBraceSlowPercent = 90;
    internal const int MaxResistPercent = 100;
    internal const float MinBashAnimationSpeed = 0.3f;
    internal const float MaxBashAnimationSpeed = 1.5f;
    internal const float MinBashStamina = 1f;
    internal const float MaxBashStamina = 50f;
    internal const float MaxBashCooldown = 10f;
    internal const float MinBashStagger = 2f;           // above 1: BashStagger.IsBashHit tell a bash by multiplier > 1
    internal const float MaxBashStagger = 50f;          // never >= 100: vanilla stagger such hit before block check
    internal const float MaxBashStaggerLock = 30f;
    internal const float MaxBashKnockback = 200f;
    internal const float MinBashRange = 1f;
    internal const float MaxBashRange = 3f;
    internal const float MinBashAngle = 10f;
    internal const float MaxBashAngle = 180f;
    internal const float MaxBashDamage = 200f;
    internal const float DefaultBashDamage = 10f;       // Towers entry without ":n"

    // Defaults (design 5). Config binding use them, self tests too.
    internal const string DefaultTowers =
        "ShieldWoodTower:6, ShieldBoneTower:8, ShieldIronTower:12, ShieldSerpentscale:15, ShieldBlackmetalTower:20, "
        + "ShieldFlametalTower:28, ShieldGoldTower:32";
    internal const float DefaultBlockArmorMultiplier = 2.5f;
    internal const int DefaultBlockForcePercent = 100;
    internal const int DefaultCarrySlowPercent = 30;
    internal const int DefaultBraceSlowPercent = 30;
    internal const int DefaultBraceStaggerResistPercent = 80;
    internal const int DefaultBraceKnockbackResistPercent = 100;
    internal const bool DefaultBlockUnblockableAttacks = true;
    internal const BashAnimationKind DefaultBashAnimation = BashAnimationKind.ShieldPunch;
    internal const string DefaultBashCustomTrigger = "";
    internal const float DefaultBashAnimationSpeed = 0.6f;  // punch clip x2 -> x1.2: hit about 0.78 s after press
    internal const float DefaultBashStamina = 20f;          // stagger not easier than a buckler parry (decision 31)
    internal const float DefaultBashCooldown = 2f;          // rate of the mace secondary, same stamina (decision 30)
    internal const float DefaultBashStagger = 25f;
    internal const float DefaultBashStaggerLock = 8f;       // one creature bash-staggered a third of the time at most
    internal const float DefaultBashKnockback = 40f;
    internal const float DefaultBashRange = 1.8f;
    internal const float DefaultBashAngle = 60f;

    // Wire: version, then every value in fixed order. Version bump = other layout (ModNetworkVersion too).
    // Layout 2 (2026-09-30): ShieldUp gone from the enum, BashAnimationSpeed and BashCooldown added,
    // BashStaggerCooldown renamed BashStaggerLock.
    internal const int Layout = 2;

    // Animation names the config offer, enum order. Old name ShieldUp not in it: config list turn it into the first
    // one (ShieldPunch), no warning.
    internal static readonly string[] AnimationNames = Enum.GetNames(typeof(BashAnimationKind));

    private static readonly char[] ListSeparators = { ',', ';', '\n', '\r' };
    private static string _defaultListKey;

    // Values. Set only before Seal (Own, TryRead, With); read-only after.
    internal string Towers = DefaultTowers;
    internal float BlockArmorMultiplier = DefaultBlockArmorMultiplier;
    internal int BlockForcePercent = DefaultBlockForcePercent;
    internal int CarrySlowPercent = DefaultCarrySlowPercent;
    internal int BraceSlowPercent = DefaultBraceSlowPercent;
    internal int BraceStaggerResistPercent = DefaultBraceStaggerResistPercent;
    internal int BraceKnockbackResistPercent = DefaultBraceKnockbackResistPercent;
    internal bool BlockUnblockableAttacks = DefaultBlockUnblockableAttacks;
    internal BashAnimationKind BashAnimation = DefaultBashAnimation;
    internal string BashCustomTrigger = DefaultBashCustomTrigger;
    internal float BashAnimationSpeed = DefaultBashAnimationSpeed;
    internal float BashStamina = DefaultBashStamina;
    internal float BashCooldown = DefaultBashCooldown;
    internal float BashStagger = DefaultBashStagger;
    internal float BashStaggerLock = DefaultBashStaggerLock;
    internal float BashKnockback = DefaultBashKnockback;
    internal float BashRange = DefaultBashRange;
    internal float BashAngle = DefaultBashAngle;

    private TowerEntry[] _entries = Array.Empty<TowerEntry>();
    private string[] _listProblems = Array.Empty<string>();
    private string _key = "";

    // Towers parsed (prefab + bash damage), in list order, no duplicates. Unknown prefabs still here: catalog check
    // them against ObjectDB.
    internal IReadOnlyList<TowerEntry> Entries => _entries;

    // What was wrong in the Towers text (bad number, duplicate...), for one log line. Empty = fine.
    internal IReadOnlyList<string> ListProblems => _listProblems;

    // Every value as text (tower list normalized): same key = same rules, nothing to apply again.
    internal string Key => _key;

    // Rules in force (design 2.0): single player, host, server, main menu = own config; client of a server with me =
    // server's once they came, null (vanilla towers) while waiting. Debug: self test override.
    internal static TowerRules InForce => ServerRules.InForce;

#if DEBUG
    // Self test: play "own settings now say this" with no config write (test then call ServerRules.OwnChanged, what
    // the settings handler call). Null = the config. Read only here.
    internal static TowerRules DebugOwn;
#endif

    // This game's own config.
    internal static TowerRules Own()
    {
#if DEBUG
        if (DebugOwn != null)
        {
            return DebugOwn;
        }
#endif
        var r = new TowerRules
        {
            Towers = Plugin.Towers.Value,
            BlockArmorMultiplier = Plugin.BlockArmorMultiplier.Value,
            BlockForcePercent = Plugin.BlockForcePercent.Value,
            CarrySlowPercent = Plugin.CarrySlowPercent.Value,
            BraceSlowPercent = Plugin.BraceSlowPercent.Value,
            BraceStaggerResistPercent = Plugin.BraceStaggerResistPercent.Value,
            BraceKnockbackResistPercent = Plugin.BraceKnockbackResistPercent.Value,
            BlockUnblockableAttacks = Plugin.BlockUnblockableAttacks.Value,
            BashAnimation = ParseAnimation(Plugin.BashAnimation.Value),
            BashCustomTrigger = Plugin.BashCustomTrigger.Value,
            BashAnimationSpeed = Plugin.BashAnimationSpeed.Value,
            BashStamina = Plugin.BashStamina.Value,
            BashCooldown = Plugin.BashCooldown.Value,
            BashStagger = Plugin.BashStagger.Value,
            BashStaggerLock = Plugin.BashStaggerLock.Value,
            BashKnockback = Plugin.BashKnockback.Value,
            BashRange = Plugin.BashRange.Value,
            BashAngle = Plugin.BashAngle.Value,
        };
        r.Seal(); // config ranges already hold; seal still pull in odd file values (NaN) silently
        return r;
    }

    // Config text -> option, any case. Unknown name = default (config list already turn unknown names, the old
    // ShieldUp too, into ShieldPunch).
    internal static BashAnimationKind ParseAnimation(string name)
    {
        var canonical = AnimationName(name);
        return canonical != null ? (BashAnimationKind)Array.IndexOf(AnimationNames, canonical) : DefaultBashAnimation;
    }

    // Text -> its name as the list spell it (any case, spaces around ignored); null = not a name of the list.
    internal static string AnimationName(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var trimmed = text.Trim();
        foreach (var name in AnimationNames)
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }
        return null;
    }

    // Default rules, whatever the config (self tests, reference).
    internal static TowerRules Defaults()
    {
        var r = new TowerRules();
        r.Seal();
        return r;
    }

#if DEBUG
    // Self test: copy with some values changed, sealed again (never touch config file).
    internal TowerRules With(Action<TowerRules> change)
    {
        var r = (TowerRules)MemberwiseClone();
        change?.Invoke(r);
        r.Seal();
        return r;
    }
#endif

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(Towers ?? "");
        pkg.Write(BlockArmorMultiplier);
        pkg.Write(BlockForcePercent);
        pkg.Write(CarrySlowPercent);
        pkg.Write(BraceSlowPercent);
        pkg.Write(BraceStaggerResistPercent);
        pkg.Write(BraceKnockbackResistPercent);
        pkg.Write(BlockUnblockableAttacks);
        pkg.Write((int)BashAnimation);
        pkg.Write(BashCustomTrigger ?? "");
        pkg.Write(BashAnimationSpeed);
        pkg.Write(BashStamina);
        pkg.Write(BashCooldown);
        pkg.Write(BashStagger);
        pkg.Write(BashStaggerLock);
        pkg.Write(BashKnockback);
        pkg.Write(BashRange);
        pkg.Write(BashAngle);
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had); values outside the
    // config ranges are pulled in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out TowerRules rules, out bool clamped)
    {
        rules = null;
        clamped = false;
        try
        {
            if (pkg == null || pkg.ReadInt() != Layout)
            {
                return false;
            }
            var r = new TowerRules
            {
                Towers = pkg.ReadString(),
                BlockArmorMultiplier = pkg.ReadSingle(),
                BlockForcePercent = pkg.ReadInt(),
                CarrySlowPercent = pkg.ReadInt(),
                BraceSlowPercent = pkg.ReadInt(),
                BraceStaggerResistPercent = pkg.ReadInt(),
                BraceKnockbackResistPercent = pkg.ReadInt(),
                BlockUnblockableAttacks = pkg.ReadBool(),
                BashAnimation = (BashAnimationKind)pkg.ReadInt(),
                BashCustomTrigger = pkg.ReadString(),
                BashAnimationSpeed = pkg.ReadSingle(),
                BashStamina = pkg.ReadSingle(),
                BashCooldown = pkg.ReadSingle(),
                BashStagger = pkg.ReadSingle(),
                BashStaggerLock = pkg.ReadSingle(),
                BashKnockback = pkg.ReadSingle(),
                BashRange = pkg.ReadSingle(),
                BashAngle = pkg.ReadSingle(),
            };
            clamped = r.Seal();
            rules = r;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Pull every value into its range (NaN = default), parse the tower list, make the key. True = a value moved.
    private bool Seal()
    {
        var clamped = false;
        Towers ??= "";
        BashCustomTrigger = (BashCustomTrigger ?? "").Trim();
        BlockArmorMultiplier = Clamp(BlockArmorMultiplier, MinBlockArmorMultiplier, MaxBlockArmorMultiplier,
            DefaultBlockArmorMultiplier, ref clamped);
        BlockForcePercent = Clamp(BlockForcePercent, 0, MaxBlockForcePercent, ref clamped);
        CarrySlowPercent = Clamp(CarrySlowPercent, 0, MaxCarrySlowPercent, ref clamped);
        BraceSlowPercent = Clamp(BraceSlowPercent, 0, MaxBraceSlowPercent, ref clamped);
        BraceStaggerResistPercent = Clamp(BraceStaggerResistPercent, 0, MaxResistPercent, ref clamped);
        BraceKnockbackResistPercent = Clamp(BraceKnockbackResistPercent, 0, MaxResistPercent, ref clamped);
        if (BashAnimation < BashAnimationKind.ShieldPunch || BashAnimation > BashAnimationKind.Custom)
        {
            BashAnimation = DefaultBashAnimation;
            clamped = true;
        }
        BashAnimationSpeed = Clamp(BashAnimationSpeed, MinBashAnimationSpeed, MaxBashAnimationSpeed,
            DefaultBashAnimationSpeed, ref clamped);
        BashStamina = Clamp(BashStamina, MinBashStamina, MaxBashStamina, DefaultBashStamina, ref clamped);
        BashCooldown = Clamp(BashCooldown, 0f, MaxBashCooldown, DefaultBashCooldown, ref clamped);
        BashStagger = Clamp(BashStagger, MinBashStagger, MaxBashStagger, DefaultBashStagger, ref clamped);
        BashStaggerLock = Clamp(BashStaggerLock, 0f, MaxBashStaggerLock, DefaultBashStaggerLock, ref clamped);
        BashKnockback = Clamp(BashKnockback, 0f, MaxBashKnockback, DefaultBashKnockback, ref clamped);
        BashRange = Clamp(BashRange, MinBashRange, MaxBashRange, DefaultBashRange, ref clamped);
        BashAngle = Clamp(BashAngle, MinBashAngle, MaxBashAngle, DefaultBashAngle, ref clamped);

        var problems = new List<string>();
        _entries = ParseTowers(Towers, problems);
        _listProblems = problems.ToArray();
        _key = BuildKey();
        return clamped;
    }

    // Pure: "Name:12, Other, Third:0.5" -> entries. Separators , ; and new lines. No ":n" = DefaultBashDamage. Bad or
    // out-of-range number, empty name, duplicate = problem text (entry fixed or skipped). Case kept: prefab names are
    // case-sensitive in ObjectDB.
    internal static TowerEntry[] ParseTowers(string text, List<string> problems)
    {
        var list = new List<TowerEntry>();
        if (string.IsNullOrEmpty(text))
        {
            return list.ToArray();
        }
        foreach (var raw in text.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            if (part.Length == 0)
            {
                continue;
            }
            var name = part;
            var damage = DefaultBashDamage;
            var colon = part.IndexOf(':');
            if (colon >= 0)
            {
                name = part.Substring(0, colon).Trim();
                var number = part.Substring(colon + 1).Trim();
                if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out damage)
                    || float.IsNaN(damage) || float.IsInfinity(damage))
                {
                    problems?.Add($"'{part}': '{number}' is not a number, so its bash deals {F(DefaultBashDamage)} damage");
                    damage = DefaultBashDamage;
                }
                else if (damage < 0f || damage > MaxBashDamage)
                {
                    var fixedDamage = Mathf.Clamp(damage, 0f, MaxBashDamage);
                    problems?.Add($"'{part}': bash damage must be 0 to {F(MaxBashDamage)}, so {F(fixedDamage)} is used");
                    damage = fixedDamage;
                }
            }
            if (name.Length == 0)
            {
                problems?.Add($"'{part}' has no prefab name and is ignored");
                continue;
            }
            var duplicate = false;
            foreach (var e in list)
            {
                if (string.Equals(e.Prefab, name, StringComparison.Ordinal))
                {
                    duplicate = true;
                    break;
                }
            }
            if (duplicate)
            {
                problems?.Add($"{name} is listed more than once; the first entry is used");
                continue;
            }
            list.Add(new TowerEntry(name, damage));
        }
        return list.ToArray();
    }

    // One log line (tower list long: only say whether it is the default one).
    internal string Describe()
    {
        var sb = new StringBuilder();
        _defaultListKey ??= ListKey(ParseTowers(DefaultTowers, null), exact: true);
        if (ListKey(_entries, exact: true) == _defaultListKey)
        {
            sb.Append("default tower list");
        }
        else
        {
            var shown = ListKey(_entries, exact: false).TrimEnd(',').Replace(",", ", ");
            sb.Append("tower list ").Append(_entries.Length == 0 ? "(empty)" : shown);
        }
        sb.Append(", block armor x").Append(F(BlockArmorMultiplier))
            .Append(", block force ").Append(BlockForcePercent).Append('%')
            .Append(", carried slow ").Append(CarrySlowPercent).Append('%')
            .Append(", braced slow ").Append(BraceSlowPercent).Append('%')
            .Append(", braced stagger -").Append(BraceStaggerResistPercent).Append('%')
            .Append(", braced knockback -").Append(BraceKnockbackResistPercent).Append('%')
            .Append(BlockUnblockableAttacks ? ", blocks unblockable attacks" : ", unblockable attacks stay unblockable")
            .Append(", bash ").Append(BashAnimation);
        if (BashAnimation == BashAnimationKind.Custom)
        {
            sb.Append(" (").Append(BashCustomTrigger.Length == 0 ? "no trigger" : BashCustomTrigger).Append(')');
        }
        sb.Append(" at speed x").Append(F(BashAnimationSpeed))
            .Append(": stamina ").Append(F(BashStamina))
            .Append(", cooldown ").Append(F(BashCooldown)).Append(" s")
            .Append(", stagger x").Append(F(BashStagger))
            .Append(", stagger lock ").Append(F(BashStaggerLock)).Append(" s")
            .Append(", knockback ").Append(F(BashKnockback))
            .Append(", range ").Append(F(BashRange)).Append(" m")
            .Append(", arc ").Append(F(BashAngle)).Append(" degrees");
        return sb.ToString();
    }

    private string BuildKey()
    {
        var sb = new StringBuilder(ListKey(_entries, exact: true));
        sb.Append('|').Append(R(BlockArmorMultiplier))
            .Append('|').Append(BlockForcePercent)
            .Append('|').Append(CarrySlowPercent)
            .Append('|').Append(BraceSlowPercent)
            .Append('|').Append(BraceStaggerResistPercent)
            .Append('|').Append(BraceKnockbackResistPercent)
            .Append('|').Append(BlockUnblockableAttacks ? '1' : '0')
            .Append('|').Append((int)BashAnimation)
            .Append('|').Append(BashCustomTrigger)
            .Append('|').Append(R(BashAnimationSpeed))
            .Append('|').Append(R(BashStamina))
            .Append('|').Append(R(BashCooldown))
            .Append('|').Append(R(BashStagger))
            .Append('|').Append(R(BashStaggerLock))
            .Append('|').Append(R(BashKnockback))
            .Append('|').Append(R(BashRange))
            .Append('|').Append(R(BashAngle));
        return sb.ToString();
    }

    // "Name:dmg," per entry: same list whatever the spaces. Exact = full float text (key), else rounded (log).
    private static string ListKey(TowerEntry[] entries, bool exact)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            sb.Append(e.Prefab).Append(':').Append(exact ? R(e.BashDamage) : F(e.BashDamage)).Append(',');
        }
        return sb.ToString();
    }

    private static int Clamp(int value, int min, int max, ref bool clamped)
    {
        var c = Mathf.Clamp(value, min, max);
        clamped |= c != value;
        return c;
    }

    private static float Clamp(float value, float min, float max, float fallback, ref bool clamped)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            clamped = true;
            return fallback;
        }
        var c = Mathf.Clamp(value, min, max);
        clamped |= c != value;
        return c;
    }

    private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string R(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
