using System;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;

namespace MC.Exploration.SailingSkillMod;

// Me = every gameplay number of the mod in one snapshot. Server send its own to every player (ServerRules): same XP,
// same ship handling, same reveal, same damage cut for everybody. Server-only AllowPlayersWithoutMod not in here, and
// me have no personal setting. Snapshot never change after build: new rules = new object.
// "AtMax" = bonus at Sailing 100; bonus grow in a straight line with level (s = level / 100).
// Pending = client still wait for server rules: built in code, never from config, never sent. Feature act vanilla then
// (no XP, no effect): game code check IsPending first, never trust the neutral numbers.
internal sealed class SailingRules
{
    // Ranges. Config (Plugin) and wire clamp (TryRead) use same numbers.
    internal const float XpPerKmMax = 1000f;
    internal const float WindFloorMin = 0.7f;        // vanilla floor: below = worse than vanilla
    internal const float WindFloorMax = 1f;
    internal const float NoGoShiftMax = 0.15f;       // 0.15 = cone ~18 deg. 0.2 = no cone at all = "sail into wind" cheat; keep well under
    internal const float BonusMax = 2f;              // AccelerationBonusAtMax, TurnBonusAtMax, RudderBonusAtMax
    internal const float SailResponseMax = 5f;       // per second
    internal const float BrakeMax = 3f;              // per second
    internal const float RevealBonusMax = 3f;
    internal const float DamageReductionMax = 0.9f;  // never full immunity

    // Skill.
    internal float XpPerKm = 50f;                    // XP per km at the helm (flat distance of the ship)

    // Handling (owner physics, helmsman's level).
    internal float WindFloorAtMax = 0.9f;            // wind factor floor 0.7 -> this
    internal float NoGoShiftAtMax = 0.1f;            // headwind cutoff (dot 0.75-0.8) moved up by this
    internal float AccelerationBonusAtMax = 0.5f;    // sail push along the bow, paddle force and forward drag x (1 + this)
    internal float SailResponseAtMax = 1.5f;         // extra sail catch-up rate, per second
    internal float TurnBonusAtMax = 0.5f;            // steering forces x (1 + this)
    internal float RudderBonusAtMax = 0.5f;          // rudder travel speed x (1 + this) (helmsman's own game)
    internal float BrakeAtMax = 0.8f;                // forward speed lost per second at Stop

    // Map (local player's own level).
    internal float RevealBonusAtMax = 1f;            // explore radius x (1 + this)

    // Damage (best level aboard).
    internal float DamageReductionAtMax = 0.5f;      // hit damage x (1 - this)

    // True only on Pending: client wait for server rules, feature act vanilla.
    internal bool IsPending { get; private set; }

    // Defaults of the design. Config Bind read them. Never change it (shared object).
    internal static readonly SailingRules Default = new SailingRules();

    // Client of a server with me, before server rules came: everything neutral.
    internal static readonly SailingRules Pending = new SailingRules
    {
        IsPending = true,
        XpPerKm = 0f,
        WindFloorAtMax = WindFloorMin,
        NoGoShiftAtMax = 0f,
        AccelerationBonusAtMax = 0f,
        SailResponseAtMax = 0f,
        TurnBonusAtMax = 0f,
        RudderBonusAtMax = 0f,
        BrakeAtMax = 0f,
        RevealBonusAtMax = 0f,
        DamageReductionAtMax = 0f,
    };

    // This game's own config. Entry not bound (BindConfig not run yet or blew up) = default.
    internal static SailingRules Own()
    {
        var d = Default;
        return new SailingRules
        {
            XpPerKm = V(Plugin.XpPerKm, d.XpPerKm),
            WindFloorAtMax = V(Plugin.WindFloorAtMax, d.WindFloorAtMax),
            NoGoShiftAtMax = V(Plugin.NoGoShiftAtMax, d.NoGoShiftAtMax),
            AccelerationBonusAtMax = V(Plugin.AccelerationBonusAtMax, d.AccelerationBonusAtMax),
            SailResponseAtMax = V(Plugin.SailResponseAtMax, d.SailResponseAtMax),
            TurnBonusAtMax = V(Plugin.TurnBonusAtMax, d.TurnBonusAtMax),
            RudderBonusAtMax = V(Plugin.RudderBonusAtMax, d.RudderBonusAtMax),
            BrakeAtMax = V(Plugin.BrakeAtMax, d.BrakeAtMax),
            RevealBonusAtMax = V(Plugin.RevealBonusAtMax, d.RevealBonusAtMax),
            DamageReductionAtMax = V(Plugin.DamageReductionAtMax, d.DamageReductionAtMax),
        };
    }

    // Wire: layout, then every value in fixed order. Layout bump = other order (ModNetworkVersion too).
    internal const int Layout = 1;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(XpPerKm);
        pkg.Write(WindFloorAtMax);
        pkg.Write(NoGoShiftAtMax);
        pkg.Write(AccelerationBonusAtMax);
        pkg.Write(SailResponseAtMax);
        pkg.Write(TurnBonusAtMax);
        pkg.Write(RudderBonusAtMax);
        pkg.Write(BrakeAtMax);
        pkg.Write(RevealBonusAtMax);
        pkg.Write(DamageReductionAtMax);
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had); values outside the
    // config ranges (or not a number) are pulled in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out SailingRules rules, out bool clamped)
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
            var r = new SailingRules();
            r.XpPerKm = Clamp(pkg.ReadSingle(), 0f, XpPerKmMax, d.XpPerKm, ref clamped);
            r.WindFloorAtMax = Clamp(pkg.ReadSingle(), WindFloorMin, WindFloorMax, d.WindFloorAtMax, ref clamped);
            r.NoGoShiftAtMax = Clamp(pkg.ReadSingle(), 0f, NoGoShiftMax, d.NoGoShiftAtMax, ref clamped);
            r.AccelerationBonusAtMax = Clamp(pkg.ReadSingle(), 0f, BonusMax, d.AccelerationBonusAtMax, ref clamped);
            r.SailResponseAtMax = Clamp(pkg.ReadSingle(), 0f, SailResponseMax, d.SailResponseAtMax, ref clamped);
            r.TurnBonusAtMax = Clamp(pkg.ReadSingle(), 0f, BonusMax, d.TurnBonusAtMax, ref clamped);
            r.RudderBonusAtMax = Clamp(pkg.ReadSingle(), 0f, BonusMax, d.RudderBonusAtMax, ref clamped);
            r.BrakeAtMax = Clamp(pkg.ReadSingle(), 0f, BrakeMax, d.BrakeAtMax, ref clamped);
            r.RevealBonusAtMax = Clamp(pkg.ReadSingle(), 0f, RevealBonusMax, d.RevealBonusAtMax, ref clamped);
            r.DamageReductionAtMax = Clamp(pkg.ReadSingle(), 0f, DamageReductionMax, d.DamageReductionAtMax, ref clamped);
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
        sb.Append(F(XpPerKm)).Append(" XP per km at the helm; at Sailing 100: wind floor ").Append(F(WindFloorAtMax))
            .Append(", no-go shift ").Append(F(NoGoShiftAtMax)).Append(", acceleration +").Append(Pct(AccelerationBonusAtMax))
            .Append(", sail response ").Append(F(SailResponseAtMax)).Append("/s, turning +").Append(Pct(TurnBonusAtMax))
            .Append(", rudder +").Append(Pct(RudderBonusAtMax)).Append(", brake ").Append(F(BrakeAtMax))
            .Append("/s, map reveal +").Append(Pct(RevealBonusAtMax)).Append(", ship damage -")
            .Append(Pct(DamageReductionAtMax));
        return sb.ToString();
    }

    private static T V<T>(ConfigEntry<T> entry, T fallback) => entry != null ? entry.Value : fallback;

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Pct(float value) => (value * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";

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
