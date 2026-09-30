using System;
using System.Globalization;
using System.Text;

namespace MC.Combat.WeaponsMovesetMod;

// Me = multipliers of one move, put on the per-swing Attack clone (design 2.5).
internal sealed class MoveNumbers
{
    internal float Damage;
    internal float Stagger;
    internal float Push;
    internal float Stamina;
}

// Me = every synced setting in one snapshot (design section 5): move switches, cooldown, numbers, animations. Server
// send its own to every player (ServerRules): same moves for everybody. Game code read rules only through
// ServerRules.Current. Snapshot never change after it is in use: new settings = new snapshot, so trigger cache
// (MoveTriggers) follow by reference. AllowPlayersWithoutMod is not in here (only server read it).
internal sealed class MoveRules
{
    // Ranges: config bind (AcceptableValueRange) and wire clamp use same bounds.
    internal const float MinDamage = 0.1f;
    internal const float MaxDamage = 5f;
    internal const float MinStagger = 0f;
    internal const float MaxStagger = 10f;
    internal const float MinPush = 0f;
    internal const float MaxPush = 5f;
    internal const float MinStamina = 0f;
    internal const float MaxStamina = 5f;
    internal const float MinAimAngle = 0f;
    // Vanilla Attack.GetMeleeAttackDir aim = look height on body heading (unit length): pitch atan(sin look), never
    // above 45 even looking straight down. Higher angle do nothing more.
    internal const float MaxAimAngle = 45f;
    internal const float MinWindow = 0.05f;
    internal const float MaxWindow = 2f;
    internal const float MinCooldown = 0f;
    internal const float MaxCooldown = 10f;
    // Roll flow (user feedback 2026-09-30): cut point in the roll, blend into the attack. Vanilla roll = 0.92 s
    // dodge state, i-frames end 0.42 s in (runtime NOTE 1.0.16): cut never before i-frames end + MoveTracker's
    // IframeMargin, whatever the value.
    internal const float MinFlowStart = 0f;
    internal const float MaxFlowStart = 2f;
    internal const float MinFlowBlend = 0f;
    internal const float MaxFlowBlend = 0.5f;

    // Defaults (design section 5).
    internal const float DefaultCooldown = 1f;
    internal const float DefaultJumpDamage = 1.2f;
    internal const float DefaultJumpStagger = 2f;
    internal const float DefaultJumpPush = 1f;
    internal const float DefaultJumpStamina = 1f;
    internal const float DefaultAimAngle = 30f;
    internal const float DefaultRollDamage = 1.3f;
    internal const float DefaultRollStagger = 1.5f;
    internal const float DefaultRollPush = 1f;
    internal const float DefaultRollStamina = 1f;
    internal const float DefaultWindow = 0.4f;
    // Near the end of the 0.92 s dodge state: whole roll play, only the 0.2 s stand-up go (user asked only that).
    // 0.7 = snappier, cut end of roll too (design Decision 22).
    internal const float DefaultFlowStart = 0.85f;
    internal const float DefaultFlowBlend = 0.15f;

    // Wire: layout, then every value in fixed order. Layout bump = other layout (ModNetworkVersion too).
    // 2: FlowStart and FlowBlend after Window (roll flow).
    internal const int Layout = 2;

    internal bool JumpAttack;
    internal bool RollAttack;
    internal float Cooldown;
    internal readonly MoveNumbers Jump = new MoveNumbers();
    internal readonly MoveNumbers Roll = new MoveNumbers();
    internal float AimAngle;      // jump attack only
    internal float Window;        // roll attack only (after a roll with roll animation: also only while it blend out)
    internal float FlowStart;     // roll attack only: seconds after roll start it may cut in
    internal float FlowBlend;     // roll attack only: seconds of cross-fade out of the roll
    internal readonly string[] JumpTriggers = new string[Families.Count];
    internal readonly string[] RollTriggers = new string[Families.Count];

    // Client of a server whose rules not here yet (or unreadable, none before): both moves off, vanilla swings
    // (design Decision 20). Never own config on a server with the mod.
    internal static readonly MoveRules Off = MakeOff();

    internal bool On(MoveKind kind) =>
        kind == MoveKind.Jump ? JumpAttack : kind == MoveKind.Roll && RollAttack;

    // Null for None.
    internal MoveNumbers Numbers(MoveKind kind) =>
        kind == MoveKind.Jump ? Jump : kind == MoveKind.Roll ? Roll : null;

    // Configured animation name (not checked: MoveTriggers.Resolve check it). Null for None or unknown family.
    internal string Trigger(MoveKind kind, WeaponFamily family)
    {
        var f = Families.Index(family);
        if (f < 0)
        {
            return null;
        }
        return kind == MoveKind.Jump ? JumpTriggers[f] : kind == MoveKind.Roll ? RollTriggers[f] : null;
    }

    // This game's own config.
    internal static MoveRules Own()
    {
        var r = new MoveRules
        {
            JumpAttack = Plugin.JumpAttack.Value,
            RollAttack = Plugin.RollAttack.Value,
            Cooldown = Plugin.Cooldown.Value,
            AimAngle = Plugin.AimAngle.Value,
            Window = Plugin.Window.Value,
            FlowStart = Plugin.FlowStart.Value,
            FlowBlend = Plugin.FlowBlend.Value,
        };
        CopyNumbers(Plugin.Jump, r.Jump);
        CopyNumbers(Plugin.Roll, r.Roll);
        for (var f = 0; f < Families.Count; f++)
        {
            r.JumpTriggers[f] = Plugin.JumpAnimation[f].Value;
            r.RollTriggers[f] = Plugin.RollAnimation[f].Value;
        }
        return r;
    }

    // Default values, no config (self tests start here, change what they need).
    internal static MoveRules Defaults()
    {
        var r = new MoveRules
        {
            JumpAttack = true,
            RollAttack = true,
            Cooldown = DefaultCooldown,
            AimAngle = DefaultAimAngle,
            Window = DefaultWindow,
            FlowStart = DefaultFlowStart,
            FlowBlend = DefaultFlowBlend,
        };
        r.Jump.Damage = DefaultJumpDamage;
        r.Jump.Stagger = DefaultJumpStagger;
        r.Jump.Push = DefaultJumpPush;
        r.Jump.Stamina = DefaultJumpStamina;
        r.Roll.Damage = DefaultRollDamage;
        r.Roll.Stagger = DefaultRollStagger;
        r.Roll.Push = DefaultRollPush;
        r.Roll.Stamina = DefaultRollStamina;
        foreach (var family in Families.All)
        {
            var f = Families.Index(family);
            r.JumpTriggers[f] = MoveTriggers.Default(MoveKind.Jump, family);
            r.RollTriggers[f] = MoveTriggers.Default(MoveKind.Roll, family);
        }
        return r;
    }

    private static MoveRules MakeOff()
    {
        var r = Defaults();
        r.JumpAttack = false;
        r.RollAttack = false;
        return r;
    }

    private static void CopyNumbers(Plugin.MoveConfig from, MoveNumbers to)
    {
        to.Damage = from.Damage.Value;
        to.Stagger = from.Stagger.Value;
        to.Push = from.Push.Value;
        to.Stamina = from.Stamina.Value;
    }

    // Layout 2: int layout, bool JumpAttack, bool RollAttack, float Cooldown, 5 float jump (damage, stagger, push,
    // stamina, aim angle), 7 float roll (damage, stagger, push, stamina, window, flow start, flow blend), int family
    // count, family-count string jump triggers, family-count string roll triggers (family order of WeaponFamily).
    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(JumpAttack);
        pkg.Write(RollAttack);
        pkg.Write(Cooldown);
        pkg.Write(Jump.Damage);
        pkg.Write(Jump.Stagger);
        pkg.Write(Jump.Push);
        pkg.Write(Jump.Stamina);
        pkg.Write(AimAngle);
        pkg.Write(Roll.Damage);
        pkg.Write(Roll.Stagger);
        pkg.Write(Roll.Push);
        pkg.Write(Roll.Stamina);
        pkg.Write(Window);
        pkg.Write(FlowStart);
        pkg.Write(FlowBlend);
        pkg.Write(Families.Count);
        foreach (var t in JumpTriggers)
        {
            pkg.Write(t ?? "");
        }
        foreach (var t in RollTriggers)
        {
            pkg.Write(t ?? "");
        }
    }

    // Never trust the wire: unknown layout, other family count or broken package = false (caller keep what it had);
    // number outside config range (or not a number) me pull in (clamped = true). Trigger names me check later,
    // against Animator (MoveTriggers.Resolve).
    internal static bool TryRead(ZPackage pkg, out MoveRules rules, out bool clamped)
    {
        rules = null;
        clamped = false;
        try
        {
            if (pkg == null || pkg.ReadInt() != Layout)
            {
                return false;
            }
            var r = new MoveRules
            {
                JumpAttack = pkg.ReadBool(),
                RollAttack = pkg.ReadBool(),
            };
            r.Cooldown = Clamp(pkg.ReadSingle(), MinCooldown, MaxCooldown, DefaultCooldown, ref clamped);
            r.Jump.Damage = Clamp(pkg.ReadSingle(), MinDamage, MaxDamage, DefaultJumpDamage, ref clamped);
            r.Jump.Stagger = Clamp(pkg.ReadSingle(), MinStagger, MaxStagger, DefaultJumpStagger, ref clamped);
            r.Jump.Push = Clamp(pkg.ReadSingle(), MinPush, MaxPush, DefaultJumpPush, ref clamped);
            r.Jump.Stamina = Clamp(pkg.ReadSingle(), MinStamina, MaxStamina, DefaultJumpStamina, ref clamped);
            r.AimAngle = Clamp(pkg.ReadSingle(), MinAimAngle, MaxAimAngle, DefaultAimAngle, ref clamped);
            r.Roll.Damage = Clamp(pkg.ReadSingle(), MinDamage, MaxDamage, DefaultRollDamage, ref clamped);
            r.Roll.Stagger = Clamp(pkg.ReadSingle(), MinStagger, MaxStagger, DefaultRollStagger, ref clamped);
            r.Roll.Push = Clamp(pkg.ReadSingle(), MinPush, MaxPush, DefaultRollPush, ref clamped);
            r.Roll.Stamina = Clamp(pkg.ReadSingle(), MinStamina, MaxStamina, DefaultRollStamina, ref clamped);
            r.Window = Clamp(pkg.ReadSingle(), MinWindow, MaxWindow, DefaultWindow, ref clamped);
            r.FlowStart = Clamp(pkg.ReadSingle(), MinFlowStart, MaxFlowStart, DefaultFlowStart, ref clamped);
            r.FlowBlend = Clamp(pkg.ReadSingle(), MinFlowBlend, MaxFlowBlend, DefaultFlowBlend, ref clamped);
            if (pkg.ReadInt() != Families.Count)
            {
                return false;
            }
            for (var f = 0; f < Families.Count; f++)
            {
                r.JumpTriggers[f] = pkg.ReadString();
            }
            for (var f = 0; f < Families.Count; f++)
            {
                r.RollTriggers[f] = pkg.ReadString();
            }
            rules = r;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // One log line: switches, numbers, animations not default.
    internal string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("jump attack ");
        if (JumpAttack)
        {
            AppendNumbers(sb, Jump);
            sb.Append(", aim ").Append(F(AimAngle)).Append("°)");
        }
        else
        {
            sb.Append("off");
        }
        sb.Append("; roll attack ");
        if (RollAttack)
        {
            AppendNumbers(sb, Roll);
            sb.Append(", window ").Append(F(Window)).Append(" s, cut in from ").Append(F(FlowStart))
                .Append(" s, blend ").Append(F(FlowBlend)).Append(" s)");
        }
        else
        {
            sb.Append("off");
        }
        sb.Append("; cooldown ").Append(F(Cooldown)).Append(" s; ");
        var custom = 0;
        foreach (var kind in Moves.All)
        {
            foreach (var family in Families.All)
            {
                var value = Trigger(kind, family);
                if (value == MoveTriggers.Default(kind, family))
                {
                    continue;
                }
                sb.Append(custom == 0 ? "animations: " : ", ");
                sb.Append(Families.Key(family)).Append(kind == MoveKind.Jump ? " jump " : " roll ").Append(value);
                custom++;
            }
        }
        if (custom == 0)
        {
            sb.Append("default animations");
        }
        return sb.ToString();
    }

    private static void AppendNumbers(StringBuilder sb, MoveNumbers n)
    {
        sb.Append("(damage x").Append(F(n.Damage)).Append(", stagger x").Append(F(n.Stagger)).Append(", push x")
            .Append(F(n.Push)).Append(", stamina x").Append(F(n.Stamina));
    }

    // Log number, same on every machine (no "1,2").
    internal static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // NaN from the wire = default; outside the range (infinity too) = nearest bound.
    private static float Clamp(float value, float min, float max, float fallback, ref bool clamped)
    {
        if (float.IsNaN(value))
        {
            clamped = true;
            return fallback;
        }
        if (value < min)
        {
            clamped = true;
            return min;
        }
        if (value > max)
        {
            clamped = true;
            return max;
        }
        return value;
    }
}
