using System;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;

namespace MC.Combat.TrinketsOnDemandMod;

// Me = every gameplay number of the mod in one snapshot. Server send its own to every player (ServerRules): same
// income, trigger rule and ranged pay for everybody. Personal choices (Controls and Feedback sections: keys, message,
// flash) and server-only AllowPlayersWithoutMod are not in here. Snapshot never change after build: new rules = new
// object.
// Pending = client still wait for server rules: built in code, never from config, never sent. Game code must check
// IsPending first and play vanilla then (decay, auto pop, no key, no income, no ranged bonus); numbers are neutral too.
internal sealed class TrinketRules
{
    // Ranges. Config (Plugin) and wire clamp (TryRead) use same numbers.
    internal const float IncomeMax = 10f;
    internal const float LingerMin = 1f;
    internal const float LingerMax = 30f;
    internal const float ReferenceMin = 0.2f;
    internal const float ReferenceMax = 5f;
    internal const float MultiplierMin = 1f;
    internal const float MultiplierMax = 10f;

    // Income in fight: adrenaline per second (before world rate, gain curve and status effects), 0 = off.
    internal float IncomePerSecond = 1f;

    // Fight lasts this long after last hit given or taken (seconds).
    internal float CombatLingerSeconds = 6f;

    // Key refused while an equipped trinket's effect still runs (else vanilla refresh, bar spent).
    internal bool RefuseWhileActive;

    // Ranged pay: attack cycle (seconds) that earn the vanilla amount per hit; slower cycles earn more.
    // 1.5 s: arrow or bolt pay 2 per 1.5 s = about 1.33 per second, like melee (measured 1.0.16: one-hand swing pay 1
    // per enemy hit, about 1.1 per second; two-hand pay 2 per slower swing, about 1.4 per second). 1 s = ranged twice.
    internal float RangedReferenceSeconds = 1.5f;

    // Cap of ranged pay multiplier; 1 = ranged bonus off.
    internal float RangedMaxMultiplier = 4f;

    internal bool IsPending { get; private set; }

    // Defaults of the design. Config Bind read them. Never change it (shared object).
    internal static readonly TrinketRules Default = new TrinketRules();

    // Client of a server with me, before server rules came: vanilla (code check IsPending), numbers neutral.
    internal static readonly TrinketRules Pending = new TrinketRules
    {
        IsPending = true,
        IncomePerSecond = 0f,
        RefuseWhileActive = false,
        RangedMaxMultiplier = MultiplierMin,
    };

    // This game's own config. Entry not bound (BindConfig not run or blew up) = default.
    internal static TrinketRules Own()
    {
        var d = Default;
        return new TrinketRules
        {
            IncomePerSecond = V(Plugin.IncomePerSecond, d.IncomePerSecond),
            CombatLingerSeconds = V(Plugin.CombatLingerSeconds, d.CombatLingerSeconds),
            RefuseWhileActive = V(Plugin.RefuseWhileActive, d.RefuseWhileActive),
            RangedReferenceSeconds = V(Plugin.RangedReferenceSeconds, d.RangedReferenceSeconds),
            RangedMaxMultiplier = V(Plugin.RangedMaxMultiplier, d.RangedMaxMultiplier),
        };
    }

    // Ranged bonus can pay anything (not pending, cap above 1).
    internal bool RangedBonusOn => !IsPending && RangedMaxMultiplier > MultiplierMin;

    // Wire: layout, then every value in fixed order. Layout bump = other order (ModNetworkVersion too).
    internal const int Layout = 1;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(IncomePerSecond);
        pkg.Write(CombatLingerSeconds);
        pkg.Write(RefuseWhileActive);
        pkg.Write(RangedReferenceSeconds);
        pkg.Write(RangedMaxMultiplier);
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had); values outside the
    // config ranges (or not a number) are pulled in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out TrinketRules rules, out bool clamped)
    {
        rules = null;
        clamped = false;
        try
        {
            if (pkg == null || pkg.ReadInt() != Layout)
            {
                return false;
            }
            var d = Default;
            var r = new TrinketRules();
            r.IncomePerSecond = Clamp(pkg.ReadSingle(), 0f, IncomeMax, d.IncomePerSecond, ref clamped);
            r.CombatLingerSeconds = Clamp(pkg.ReadSingle(), LingerMin, LingerMax, d.CombatLingerSeconds, ref clamped);
            r.RefuseWhileActive = pkg.ReadBool();
            r.RangedReferenceSeconds = Clamp(pkg.ReadSingle(), ReferenceMin, ReferenceMax, d.RangedReferenceSeconds,
                ref clamped);
            r.RangedMaxMultiplier = Clamp(pkg.ReadSingle(), MultiplierMin, MultiplierMax, d.RangedMaxMultiplier,
                ref clamped);
            rules = r;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // One log line with every value (also the "did rules change" test of ServerRules: same text = same rules).
    internal string Describe()
    {
        if (IsPending)
        {
            return "waiting for the server's rules (normal game until they arrive)";
        }
        var sb = new StringBuilder(256);
        sb.Append("income ").Append(F(IncomePerSecond)).Append(" adrenaline per second in a fight (fight lasts ")
            .Append(F(CombatLingerSeconds)).Append(" s after the last hit), trigger ")
            .Append(RefuseWhileActive ? "refused" : "allowed").Append(" while the effect runs, ranged pay ");
        if (RangedMaxMultiplier > MultiplierMin)
        {
            sb.Append("scaled against a ").Append(F(RangedReferenceSeconds)).Append(" s cycle, up to x")
                .Append(F(RangedMaxMultiplier));
        }
        else
        {
            sb.Append("as in the normal game");
        }
        return sb.ToString();
    }

    private static T V<T>(ConfigEntry<T> entry, T fallback) => entry != null ? entry.Value : fallback;

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // Not a number (NaN, infinity) = default value.
    private static float Clamp(float value, float min, float max, float fallback, ref bool clamped)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            clamped = true;
            return fallback;
        }
        var c = value < min ? min : value > max ? max : value;
        clamped |= c != value;
        return c;
    }
}
