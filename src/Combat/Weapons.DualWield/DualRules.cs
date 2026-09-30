using System;
using System.Text;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Which weapon strike each hit event of a pair swing. Config show member names: never rename.
internal enum HitPatternMode
{
    Alternate,   // table per fired trigger: main, off, or both (design 2.6)
    BothHands,   // every event: both weapons, each at BothHandsDamage percent
}

// Special attack of a pair. Config show member names: never rename.
internal enum SecondaryMovesMode
{
    PairMoves,   // template item special (Berserkir cleave, Skoll and Hati leap), both weapons strike
    MainWeapon,  // main weapon own vanilla special, untouched, main hand only
}

// Me = every combat rule of the mod in one snapshot. Server send its own to every player (ServerRules): everybody
// fight with same rules. Personal things (keys, trails) and AllowPlayersWithoutMod no live in here.
// Read only through ServerRules.Current. Object never change after made: new rules = new object, so code can
// spot a change with one reference compare (Player.Update postfix rebuild caches then).
internal sealed class DualRules
{
    internal const int DefaultOffHandDamage = 100;
    internal const int MinOffHandDamage = 10;
    internal const int MaxOffHandDamage = 200;
    internal const int DefaultSwingStamina = 100;
    internal const int MinSwingStamina = 25;
    internal const int MaxSwingStamina = 300;
    internal const int DefaultBothHandsDamage = 50;
    internal const int MinBothHandsDamage = 10;
    internal const int MaxBothHandsDamage = 100;
    internal const string DefaultPairMoves = "AxeBerzerkr";
    internal const string DefaultKnifePairMoves = "KnifeSkollAndHati";
    internal const string DefaultExcludedWeapons = "";
    // Wire string cap: long list from server cut here.
    internal const int MaxStringLength = 1000;

    internal readonly int OffHandDamage;              // percent MinOffHandDamage..MaxOffHandDamage
    internal readonly int SwingStamina;               // percent MinSwingStamina..MaxSwingStamina
    internal readonly SecondaryMovesMode SecondaryMoves;
    internal readonly HitPatternMode HitPattern;
    internal readonly int BothHandsDamage;            // percent MinBothHandsDamage..MaxBothHandsDamage
    internal readonly string PairMoves;               // item prefab name, never null
    internal readonly string KnifePairMoves;          // item prefab name, never null
    internal readonly string ExcludedWeapons;         // comma-separated prefab names, never null

    // Built-in defaults (design section 5, Default column). Client of a server with me use these while server
    // rules not come yet (design 4.3, D25): never own config there, and not vanilla (saved pair must load).
    internal static readonly DualRules Defaults = new DualRules(DefaultOffHandDamage, DefaultSwingStamina,
        SecondaryMovesMode.PairMoves, HitPatternMode.Alternate, DefaultBothHandsDamage, DefaultPairMoves,
        DefaultKnifePairMoves, DefaultExcludedWeapons);

    internal DualRules(int offHandDamage, int swingStamina, SecondaryMovesMode secondaryMoves, HitPatternMode hitPattern,
        int bothHandsDamage, string pairMoves, string knifePairMoves, string excludedWeapons)
    {
        OffHandDamage = offHandDamage;
        SwingStamina = swingStamina;
        SecondaryMoves = secondaryMoves;
        HitPattern = hitPattern;
        BothHandsDamage = bothHandsDamage;
        PairMoves = (pairMoves ?? "").Trim();
        KnifePairMoves = (knifePairMoves ?? "").Trim();
        ExcludedWeapons = (excludedWeapons ?? "").Trim();
    }

    // This game's own config. Config ranges already hold numbers in range (AcceptableValueRange), me clamp anyway:
    // hand-edited file can hold anything before BepInEx fix it.
    internal static DualRules Own()
    {
        var ignored = false;
        return new DualRules(
            Clamp(Plugin.OffHandDamage.Value, MinOffHandDamage, MaxOffHandDamage, ref ignored),
            Clamp(Plugin.SwingStamina.Value, MinSwingStamina, MaxSwingStamina, ref ignored),
            Plugin.SecondaryMoves.Value,
            Plugin.HitPattern.Value,
            Clamp(Plugin.BothHandsDamage.Value, MinBothHandsDamage, MaxBothHandsDamage, ref ignored),
            Plugin.PairMoves.Value,
            Plugin.KnifePairMoves.Value,
            Plugin.ExcludedWeapons.Value);
    }

    // Wire: layout, then every value in fixed order. Layout bump = other format (ModNetworkVersion too).
    //   int layout, int OffHandDamage, int SwingStamina, int SecondaryMoves, int HitPattern, int BothHandsDamage,
    //   string PairMoves, string KnifePairMoves, string ExcludedWeapons
    internal const int Layout = 1;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(OffHandDamage);
        pkg.Write(SwingStamina);
        pkg.Write((int)SecondaryMoves);
        pkg.Write((int)HitPattern);
        pkg.Write(BothHandsDamage);
        pkg.Write(PairMoves);
        pkg.Write(KnifePairMoves);
        pkg.Write(ExcludedWeapons);
    }

    // Never trust the wire: unknown layout, missing field, extra field or broken package = false (caller keep what it
    // had). Number outside config range, unknown enum value, too long string: me pull it in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out DualRules rules, out bool clamped)
    {
        rules = null;
        clamped = false;
        try
        {
            if (pkg == null || pkg.ReadInt() != Layout)
            {
                return false;
            }
            var offHand = Clamp(pkg.ReadInt(), MinOffHandDamage, MaxOffHandDamage, ref clamped);
            var swing = Clamp(pkg.ReadInt(), MinSwingStamina, MaxSwingStamina, ref clamped);
            var secondary = pkg.ReadInt();
            if (!Enum.IsDefined(typeof(SecondaryMovesMode), secondary))
            {
                clamped = true;
                secondary = (int)SecondaryMovesMode.PairMoves;
            }
            var pattern = pkg.ReadInt();
            if (!Enum.IsDefined(typeof(HitPatternMode), pattern))
            {
                clamped = true;
                pattern = (int)HitPatternMode.Alternate;
            }
            var both = Clamp(pkg.ReadInt(), MinBothHandsDamage, MaxBothHandsDamage, ref clamped);
            var pair = Cap(pkg.ReadString(), ref clamped);
            var knife = Cap(pkg.ReadString(), ref clamped);
            var excluded = Cap(pkg.ReadString(), ref clamped);
            // Field more than layout 1 know = other format with same layout number: refuse, no guess.
            if (pkg.GetPos() != pkg.Size())
            {
                return false;
            }
            rules = new DualRules(offHand, swing, (SecondaryMovesMode)secondary, (HitPatternMode)pattern, both, pair,
                knife, excluded);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Same values = same rules (server resend same rules: me keep old object, caches stay).
    internal bool SameAs(DualRules other)
    {
        return other != null
               && OffHandDamage == other.OffHandDamage
               && SwingStamina == other.SwingStamina
               && SecondaryMoves == other.SecondaryMoves
               && HitPattern == other.HitPattern
               && BothHandsDamage == other.BothHandsDamage
               && PairMoves == other.PairMoves
               && KnifePairMoves == other.KnifePairMoves
               && ExcludedWeapons == other.ExcludedWeapons;
    }

    // One log line, player-facing English.
    internal string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("off-hand damage ").Append(OffHandDamage).Append("%, swing stamina ").Append(SwingStamina)
            .Append("%, hit pattern ").Append(HitPattern == HitPatternMode.BothHands
                ? "both hands (" + BothHandsDamage + "% each)"
                : "alternate (both hands " + BothHandsDamage + "% each)")
            .Append(", moves ").Append(PairMoves.Length > 0 ? PairMoves : "(none)")
            .Append(" / knives ").Append(KnifePairMoves.Length > 0 ? KnifePairMoves : "(none)")
            .Append(", special ").Append(SecondaryMoves == SecondaryMovesMode.MainWeapon ? "main weapon's own" : "pair moves")
            .Append(", excluded weapons ").Append(ExcludedWeapons.Length > 0 ? ExcludedWeapons : "none");
        return sb.ToString();
    }

    private static int Clamp(int value, int min, int max, ref bool clamped)
    {
        var c = Mathf.Clamp(value, min, max);
        clamped |= c != value;
        return c;
    }

    private static string Cap(string value, ref bool clamped)
    {
        if (value == null)
        {
            return "";
        }
        if (value.Length <= MaxStringLength)
        {
            return value;
        }
        clamped = true;
        return value.Substring(0, MaxStringLength);
    }
}
