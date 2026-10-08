using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;
#endif

namespace MC.Farming.HarpoonHooksTamesMod;

#if DEBUG
// Debug build only. Switches the self tests flip in memory (never config). Patch code read them first thing.
//   ThrowerSideOff  thrower-side patches do nothing (Projectile.IsValidTarget postfix, Character.Damage prefix):
//                   a thrower without the mod, while the owner side (RPC_Damage patches) stay on
//   OwnerSideOff    owner-side patches do nothing (Character.RPC_Damage prefix/postfix): a tame run by a game
//                   without the mod (friend, vanilla server), while the thrower side stay on
//   Blocker         Plugin.LocalBlocker text: framework turn the mod off live (patches gone), like the MC Mods tick
internal static class TestSwitches
{
    internal static bool ThrowerSideOff;
    internal static bool OwnerSideOff;
    internal static string Blocker;
}
#endif

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod Harpoon.HooksTames) and by tools/Test-Multiplayer.ps1 (harpoon.mp.*). Files: SelfTests.cs (list, small
// helpers), SelfTestsRig.cs (stage, harpoon, hits), SelfTestsSingle.cs, SelfTestsCompat.cs, SelfTestsMulti.cs.
// Single player (TESTING.md item in brackets):
//   harpoon.hook            hook a tame with the harpoon projectile's own hit [T01], too far [T04], PvP on [T07]
//   harpoon.throw           real throw (Attack.FireProjectileBurst, flight) hook a tame [T01]
//   harpoon.rehook          second throw at a tame already hooked [T24]
//   harpoon.wild            wild boar and half-tamed boar get the vanilla hit [T05, T16]
//   harpoon.drag            walk away = drag, stamina drain, stamina out, block release, line break [T02]
//   harpoon.heavy           lox against boar: stamina drain at the same stretch, distance dragged [T03]
//   harpoon.others          arrow, flint spear, club swing on a tame stay vanilla [T06]
//   harpoon.rider           saddled lox: ridden = fly past (PvP off) / hooked (PvP on) [M03], after the ride [T08]
//   harpoon.summon          friendly skeleton: hooked, also while it fight, also in the line of fire [T09, T13]
//   harpoon.stay            stay command: hook leave stay spot alone, tame walk back, new stay spot [T10]
//   harpoon.xp              no Spears skill, no adrenaline from a tame; both from a wild boar [T11]
//   harpoon.line            tamed wolf between player and greyling catch the flying harpoon [T12]
//   harpoon.busy            tame with a target it cannot reach [T21]
//   harpoon.chase           wolf chasing a deer: hooked, held on the line, pulled back by walking away [T22]
//   harpoon.credit          hook leave no kill credit; knife kill count once [T14]
//   harpoon.credit-kept     earlier real hit keep its credit and its Ranged modifier after a hook [T23]
//   harpoon.shield          tame under a protection bubble: bubble not weakened [T15]
//   harpoon.ship            tame on a Karve deck: hooked, not pulled [T17]
//   harpoon.toggle          mod off live = harpoon fly past, on again = hook [T18, T19]
//   harpoon.handoff         single-player stand-ins for the hand-off cases (switches above) [M01, M02, M05, M06]
//   harpoon.compat.crossbow loaded crossbow, real harpoon throw, crossbow still loaded [C01]
//   harpoon.compat.counts   Creature Kill and Tame Counts line of the Boar [C02]
//   harpoon.compat.nohook   other mod take the hook hash away (HarpoonExtended way) [C03]
//   harpoon.compat.immortal other mod empty the hit first (ValheimPlus way) [C04]
//   harpoon.compat.procs    other mod hit the tame again inside the harpoon hit (EpicLoot way) [C05]
//   harpoon.compat.order    damage added by an EpicLoot-named prefix is zeroed too (HarmonyAfter) [C05]
//   harpoon.log             no error line of this mod since it started [T20]
// Multiplayer (client joined to a dedicated server; this mod is client-only, so the server never runs it):
//   harpoon.mp.hook         tame this client simulate [M01]
//   harpoon.mp.handoff      tame held by the server at the throw: hit sent away, tame come back whole, hook again
//                           (server simulate no creature: hook / drag on another game's tame NOT played) [M01]
//   harpoon.mp.toggle       Enabled off / on for real (throwaway config) [T18, T19]
// Me never write config in single player: TestSwitches only. Rig put back player, items, skills, PvP, stamina rate.
internal static partial class SelfTests
{
#if DEBUG
    private const string HookName = "harpoon.hook";
    private const string ThrowName = "harpoon.throw";
    private const string RehookName = "harpoon.rehook";
    private const string WildName = "harpoon.wild";
    private const string DragName = "harpoon.drag";
    private const string HeavyName = "harpoon.heavy";
    private const string OthersName = "harpoon.others";
    private const string RiderName = "harpoon.rider";
    private const string SummonName = "harpoon.summon";
    private const string StayName = "harpoon.stay";
    private const string XpName = "harpoon.xp";
    private const string LineName = "harpoon.line";
    private const string BusyName = "harpoon.busy";
    private const string ChaseName = "harpoon.chase";
    private const string CreditName = "harpoon.credit";
    private const string CreditKeptName = "harpoon.credit-kept";
    private const string ShieldName = "harpoon.shield";
    private const string ShipName = "harpoon.ship";
    private const string ToggleName = "harpoon.toggle";
    private const string HandoffName = "harpoon.handoff";
    private const string CrossbowName = "harpoon.compat.crossbow";
    private const string CountsName = "harpoon.compat.counts";
    private const string NoHookName = "harpoon.compat.nohook";
    private const string ImmortalName = "harpoon.compat.immortal";
    private const string ProcsName = "harpoon.compat.procs";
    private const string OrderName = "harpoon.compat.order";
    private const string LogName = "harpoon.log";
    private const string MpHookName = "harpoon.mp.hook";
    private const string MpHandoffName = "harpoon.mp.handoff";
    private const string MpToggleName = "harpoon.mp.toggle";
#endif

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        Tap.Install();
        SelfTest.Register(HookName, RunHook);
        SelfTest.Register(ThrowName, RunThrow);
        SelfTest.Register(RehookName, RunRehook);
        SelfTest.Register(WildName, RunWild);
        SelfTest.Register(DragName, RunDrag);
        SelfTest.Register(HeavyName, RunHeavy);
        SelfTest.Register(OthersName, RunOthers);
        SelfTest.Register(RiderName, RunRider);
        SelfTest.Register(SummonName, RunSummon);
        SelfTest.Register(StayName, RunStay);
        SelfTest.Register(XpName, RunXp);
        SelfTest.Register(LineName, RunLine);
        SelfTest.Register(BusyName, RunBusy);
        SelfTest.Register(ChaseName, RunChase);
        SelfTest.Register(CreditName, RunCredit);
        SelfTest.Register(CreditKeptName, RunCreditKept);
        SelfTest.Register(ShieldName, RunShield);
        SelfTest.Register(ShipName, RunShip);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(HandoffName, RunHandoff);
        SelfTest.Register(CrossbowName, RunCrossbow);
        SelfTest.Register(CountsName, RunCounts);
        SelfTest.Register(NoHookName, RunNoHook);
        SelfTest.Register(ImmortalName, RunImmortal);
        SelfTest.Register(ProcsName, RunProcs);
        SelfTest.Register(OrderName, RunOrder);
        SelfTest.Register(LogName, RunLog); // last: it look back on every test before it
        SelfTest.RegisterMultiplayer(MpHookName, SelfTest.Modded, RunMpHook);
        SelfTest.RegisterMultiplayer(MpHandoffName, SelfTest.Modded, RunMpHandoff);
        SelfTest.RegisterMultiplayer(MpToggleName, SelfTest.Modded, RunMpToggle);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        foreach (var name in new[]
                 {
                     HookName, ThrowName, RehookName, WildName, DragName, HeavyName, OthersName, RiderName, SummonName,
                     StayName, XpName, LineName, BusyName, ChaseName, CreditName, CreditKeptName, ShieldName, ShipName, ToggleName,
                     HandoffName, CrossbowName, CountsName, NoHookName, ImmortalName, ProcsName, OrderName, LogName,
                 })
        {
            SelfTest.Unregister(name);
        }
        SelfTest.UnregisterMultiplayer(MpHookName);
        SelfTest.UnregisterMultiplayer(MpHandoffName);
        SelfTest.UnregisterMultiplayer(MpToggleName);
        // Blocker stay as is: harpoon.toggle turn me off on purpose and clear it itself (clear here = mod come back
        // at next refresh, in the middle of that test). Probe keep its own list copy, so running test go on.
        TestSwitches.ThrowerSideOff = false;
        TestSwitches.OwnerSideOff = false;
#endif
    }

#if DEBUG
    private static readonly WaitForFixedUpdate Fixed = new WaitForFixedUpdate();
    private static readonly LogTap Tap = new LogTap();

    // ---------- small helpers ----------

    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        internal Checks(string name) => _name = name;

        internal bool Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
            return ok;
        }

        internal void Note(string detail) => SelfTest.Note(_name, detail);

        internal void Report(string extra = "")
        {
            if (_failures.Count == 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK{extra}");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    // Result of a nested coroutine.
    private sealed class Box
    {
        internal bool Ok;
        internal float Seconds;
    }

    // Me listen to BepInEx log (every source, every level, also Debug lines the console hide): keep lines of this mod
    // so tests can count Debug lines TESTING.md name, and count error lines that name this mod (harpoon.log).
    // Log events can come from any thread: me lock. Never throw inside logger.
    private sealed class LogTap : ILogListener
    {
        private const int MaxLines = 20000;

        private readonly object _gate = new object();
        private readonly List<string> _lines = new List<string>();
        private int _errors;
        private string _firstError;
        private bool _installed;

        internal void Install()
        {
            lock (_gate)
            {
                if (_installed)
                {
                    return;
                }
                _installed = true;
            }
            BepInEx.Logging.Logger.Listeners.Add(this);
        }

        // Index for CountSince: lines of this mod seen so far.
        internal int Mark()
        {
            lock (_gate)
            {
                return _lines.Count;
            }
        }

        internal int CountSince(int mark, string part)
        {
            var count = 0;
            lock (_gate)
            {
                for (var i = Mathf.Max(0, mark); i < _lines.Count; i++)
                {
                    if (_lines[i].IndexOf(part, StringComparison.Ordinal) >= 0)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        internal int Errors(out string first, out int lines)
        {
            lock (_gate)
            {
                first = _firstError ?? "";
                lines = _lines.Count;
                return _errors;
            }
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                if (text == null)
                {
                    return;
                }
                var mine = eventArgs.Source != null && eventArgs.Source.SourceName == ModInfo.Name;
                var isError = (eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) != 0;
                if (!mine && !isError)
                {
                    return;
                }
                lock (_gate)
                {
                    if (mine && _lines.Count < MaxLines)
                    {
                        _lines.Add(text);
                    }
                    // Error of this mod, or error of anybody that name this mod (exception trace from a patch).
                    // Own "[selftest] FAIL" lines are test results, not mod errors.
                    if (isError && !text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal)
                        && (mine || text.IndexOf("HarpoonHooksTames", StringComparison.Ordinal) >= 0
                            || text.IndexOf(ModInfo.Guid, StringComparison.Ordinal) >= 0
                            || text.IndexOf(ModInfo.Name, StringComparison.Ordinal) >= 0))
                    {
                        _errors++;
                        if (_firstError == null)
                        {
                            var cut = text.IndexOf('\n');
                            _firstError = (eventArgs.Source?.SourceName ?? "?") + ": " + (cut > 0 ? text.Substring(0, cut).TrimEnd() : text);
                        }
                    }
                }
            }
            catch
            {
                // Me never throw inside logger.
            }
        }

        public void Dispose()
        {
        }
    }

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string F(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

    private static bool Near(float a, float b, float tolerance = 0.01f) => Mathf.Abs(a - b) <= tolerance;

    private static string Loc(string text) => Localization.instance != null ? Localization.instance.Localize(text) : text;

    private static float HDist(Vector3 a, Vector3 b) => Utils.DistanceXZ(a, b);

    // Center message on screen now (vanilla MessageHud put Center text there at once).
    private static string Center()
    {
        var hud = MessageHud.instance;
        return hud != null && hud.m_messageCenterText != null ? hud.m_messageCenterText.text ?? "" : "";
    }

    private static void ClearCenter()
    {
        var hud = MessageHud.instance;
        if (hud != null && hud.m_messageCenterText != null)
        {
            hud.m_messageCenterText.text = "";
        }
    }

    private static string HarpoonedText(Character c) => Loc(c.m_name + " $msg_harpoon_harpooned");

    private static string ReleasedText(Character c) => Loc(c.m_name + " $msg_harpoon_released");

    // Game's own statistics, raw block (index 0 count even for a god mode player).
    private static float Stat(PlayerStatType type)
    {
        var stats = Game.instance.GetPlayerProfile().m_playerStats[0].m_stats;
        return stats.TryGetValue(type, out var v) ? v : 0f;
    }

    // Kills of one creature name: index 0 = all, else (int)KillModifiers.
    private static float EnemyStat(int index, string name)
    {
        var stats = Game.instance.GetPlayerProfile().m_playerStats[0].m_enemyStats[index];
        return stats != null && stats.TryGetValue(name, out var v) ? v : 0f;
    }

    // Same text vanilla Character.RPC_Damage use for the attacker mark (int + name, hashed by ZDO).
    private static string AttackerKey(Player p)
    {
        int attackers = ZDOVars.s_attackers;
        return attackers + p.GetPlayerName();
    }

    private static string Marks(ZDO zdo, string key) =>
        $"mark {zdo.GetBool(key)}, attackers {zdo.GetInt(ZDOVars.s_attackers)}, modifier {(KillModifiers)zdo.GetInt(ZDOVars.s_modifiers, (int)KillModifiers.CountNone)}";

    private static IEnumerator Wait(float seconds, Action each = null)
    {
        var end = Time.time + seconds;
        while (Time.time < end)
        {
            each?.Invoke();
            yield return null;
        }
    }

    private static IEnumerator FixedFor(float seconds, Action each = null)
    {
        var end = Time.time + seconds;
        while (Time.time < end)
        {
            each?.Invoke();
            yield return Fixed;
        }
    }

    private static IEnumerator Until(Func<bool> condition, float timeout, Box box, Action each = null)
    {
        var start = Time.time;
        box.Ok = false;
        while (true)
        {
            each?.Invoke();
            if (condition())
            {
                box.Ok = true;
                break;
            }
            if (Time.time - start >= timeout)
            {
                break;
            }
            yield return null;
        }
        box.Seconds = Time.time - start;
    }

    // Me set transform and body (else physics put old place back). Zero facing = rotation stay.
    private static void Place(Character c, Vector3 pos, Vector3 facing)
    {
        if (c == null)
        {
            return;
        }
        var t = c.transform;
        t.position = pos;
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.0001f)
        {
            t.rotation = Quaternion.LookRotation(facing.normalized);
        }
        var body = c.m_body;
        if (body != null)
        {
            body.position = pos;
            body.rotation = t.rotation;
            body.linearVelocity = Vector3.zero;
        }
    }

    private static Vector3 Ground(Vector3 p)
    {
        var zones = ZoneSystem.instance;
        if (zones != null && zones.GetGroundHeight(p, out var height))
        {
            p.y = height;
        }
        return p;
    }
#endif
}
