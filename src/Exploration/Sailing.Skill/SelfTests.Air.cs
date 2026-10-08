#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using MC.Exploration.SailingSkillMod.Patches;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.SailingSkillMod;

// Debug build only. Me = tests on the air rig of SelfTests.cs (ship held in the air above the spawn, player at helm):
//   sailing.xp           400 m at the helm from Sailing 0 (level-up messages, level, session Debug lines), nothing
//                        while still or as a passenger on a moving ship, Rested +50%
//   sailing.hud          HUD wind icon and no-go angle at Sailing 0 and 100 with a fixed wind, rudder full-lock time
//   sailing.sail         sail response through the real patch: never past the game's target, sooner up and down,
//                        wind flipped under a settled sail
//   sailing.map          map bits really revealed (pixels) at the helm and on the deck, at 0 and 100, and ashore
//   sailing.damage       every hit type a ship takes, the Debug line, a creature as attacker, player hits and the
//                        real capsized loop not reduced
//   sailing.ram          ship thrown at clear ground: real collision damage (vanilla ImpactEffect) at 0 and 100
//   sailing.toggle       feature turned off and on at the helm (Plugin.TestBlocked, never the config): all effects
//                        go and come back, nothing piles up, level kept while off, turned on mid-voyage
//   sailing.bug.wind-icon-foreign  HUD wind icon on a ship another game simulates without the mod (real bug, test
//                        list M13: this test fails until the mod is fixed; alone here so no other check waits on it)
//   sailing.bug.sail-overshoot  wind flipped while the sail still fills: the sail force goes a little past its
//                        target (real bug, small, test list T19: fails until the mod or the design claim changes)
//   sailing.cleanlog     no warning and no error from this mod since the game started
internal static partial class SelfTests
{
    private const string XpName = "sailing.xp";
    private const string HudName = "sailing.hud";
    private const string SailName = "sailing.sail";
    private const string MapName = "sailing.map";
    private const string DamageName = "sailing.damage";
    private const string RamName = "sailing.ram";
    private const string ToggleName = "sailing.toggle";
    private const string HudForeignName = "sailing.bug.wind-icon-foreign";
    private const string BugSailName = "sailing.bug.sail-overshoot";
    private const string CleanLogName = "sailing.cleanlog";

    // ---------- shared pieces ----------

    private sealed class Gain
    {
        internal bool Sampled;
        internal float Xp;
    }

    // One XP sample at the helm: me move ship <metres> along x, wait until the mod's 1 s sample paid (or 1.8 s).
    // restart = begin a new helm session first (first sample only take the position).
    private static IEnumerator SampleXp(Ship ship, Skills.Skill skill, float metres, Gain result, bool restart = true)
    {
        result.Sampled = false;
        result.Xp = 0f;
        if (restart)
        {
            SailingXp.Reset();
            yield return null;
            yield return null;
        }
        var before = skill.m_accumulator;
        var level = skill.m_level;
        MoveShip(ship, ship.transform.position + Vector3.right * metres);
        var until = Time.time + 1.8f;
        while (Time.time < until)
        {
            yield return null;
            if (skill.m_accumulator != before || skill.m_level != level)
            {
                result.Sampled = true;
                break;
            }
        }
        result.Xp = skill.m_accumulator - before;
    }

    private sealed class Reveal
    {
        internal bool Ok;
        internal int Reach = -1;     // pixels revealed from the player toward +x (0 = only its own pixel)
        internal float Used = -1f;   // radius my prefix saw (-1 = prefix did not run)
    }

    // One vanilla Explore with map bits around the player wiped first: how far the revealed circle really reach.
    // Me put bits and fog pixels back like before.
    private static Reveal RevealReach(Player player)
    {
        const int w = 14;
        var result = new Reveal();
        var map = Minimap.instance;
        if (map == null || map.m_explored == null || map.m_fogTexture == null)
        {
            return result;
        }
        map.WorldToPixel(player.transform.position, out var px, out var py);
        var size = map.m_textureSize;
        if (px - w < 0 || py - w < 0 || px + w >= size || py + w >= size)
        {
            return result;
        }
        var side = 2 * w + 1;
        var bits = new bool[side * side];
        var fog = new Color[side * side];
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var index = (py - w + y) * size + px - w + x;
                bits[y * side + x] = map.m_explored[index];
                fog[y * side + x] = map.m_fogTexture.GetPixel(px - w + x, py - w + y);
                map.m_explored[index] = false;
            }
        }
        try
        {
            MinimapPatches.LastRadius = -1f;
            map.m_exploreTimer = map.m_exploreInterval + 1f;
            map.UpdateExplore(0f, player);
            result.Used = MinimapPatches.LastRadius;
            var reach = -1;
            for (var k = 0; k <= w; k++)
            {
                if (!map.m_explored[py * size + px + k])
                {
                    break;
                }
                reach = k;
            }
            result.Reach = reach;
            result.Ok = true;
        }
        finally
        {
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    map.m_explored[(py - w + y) * size + px - w + x] = bits[y * side + x];
                    map.m_fogTexture.SetPixel(px - w + x, py - w + y, fog[y * side + x]);
                }
            }
            map.m_fogTexture.Apply();
        }
        return result;
    }

    // Pixels a reveal of this radius reach (vanilla Minimap.Explore: radius rounded up to whole pixels).
    private static int ReachOf(Minimap map, float radius) => (int)Mathf.Ceil(radius / map.m_pixelSize);

    // My prefixes on the ship's physics step right now (1 while the feature is on, 0 while off, never more).
    private static int OwnShipPrefixes()
    {
        var method = AccessTools.Method(typeof(Ship), nameof(Ship.CustomFixedUpdate), new[] { typeof(float) });
        var info = method != null ? Harmony.GetPatchInfo(method) : null;
        return info != null ? info.Prefixes.Count(p => p.owner == ModInfo.Guid) : 0;
    }

    private static void SetBlocked(bool blocked)
    {
        Plugin.TestBlocked = blocked;
        FeatureRegistry.RefreshAll();
    }

    private static string DamageLine(string ship, HitData.HitType type, float before, float after, float level) =>
        $"{ship} hit ({type}): {before.ToString("0.#", CultureInfo.InvariantCulture)} -> "
        + $"{after.ToString("0.#", CultureInfo.InvariantCulture)} damage before resistances (best Sailing aboard {level:0}).";

    // Me carry player with a ship me move by hand (ship moved by position has no speed to carry who stand on it).
    private static void Carry(Player player, Vector3 delta)
    {
        player.transform.position += delta;
        if (player.m_body != null)
        {
            player.m_body.position += delta;
        }
    }

    // ---------- sailing.xp ----------

    private static IEnumerator RunXp()
    {
        var c = new Checks(XpName);
        var rig = ShipRig.Create(XpName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        var player = rig.Player;
        var seman = player.GetSEMan();
        var restedBefore = seman.GetStatusEffect(SEMan.s_statusEffectRested);
        var hadRested = restedBefore != null;
        var restedTtl = hadRested ? restedBefore.m_ttl : 0f;
        var restedTime = hadRested ? restedBefore.m_time : 0f;
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var ship = rig.Boat;
            var skills = player.GetSkills();
            var type = SailingSkill.Type;
            var loc = Localization.instance;
            var shipName = Utils.GetPrefabName(ship.gameObject);
            // Whole metres: the 20 m steps below are then exactly 20 m in float, like the numbers of the test list.
            var home = ship.transform.position;
            home = new Vector3(Mathf.Round(home.x), home.y, Mathf.Round(home.z));
            MoveShip(ship, home);
            // Me come here out of a physics-step wait (SetUpAirShip, WaitAboard): this frame's Player.Update, where
            // the mod's helm watch takes its first position, still comes, with no physics step since the move. Run
            // 2026-10-07: 20 samples paid, yet the session line read "19.98 Sailing XP", not 20: one sample 0.3 to
            // 0.5 m short. Every later sample is read a second after its move; only the first position is read right
            // after one, and the rounding above moves the ship by up to half a metre: the watch started from the
            // place before the move (the run before passed with the ship 3 cm from a whole metre). Two physics steps
            // and one frame first: the ship is at home for every game system before the watch looks; the check after
            // the session start reads what the watch really took.
            yield return FixedSteps(2);
            yield return null;
            c.Check(Flat(ship.transform.position - home).magnitude < 1e-3f,
                $"the ship is not where the test put it before the 400 m (off by {F(Flat(ship.transform.position - home).magnitude)} m): "
                + "the distances below would be wrong");
            var perKm = rules.XpPerKm.ToString("0.##", CultureInfo.InvariantCulture);
            HelmSkill.TestLocalLevel = null;
            rig.BackupSkills();
            if (hadRested)
            {
                seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
            }
            var plain = 1f;
            seman.ModifyRaiseSkill(type, ref plain);
            var perXp = plain * Game.m_skillGainRate;
            c.Note($"progress per XP x{F(perXp)} (status effects x{F(plain)}, world skill gain x{F(Game.m_skillGainRate)})");

            // The numbers of the test list, with the game's own level-up math: Sailing 5 after 400 m at any real ship
            // speed (5 to 20 m per 1 s sample), 6 when Rested.
            if (Near(perXp, 1f))
            {
                var levels = new[] { 5f, 8f, 10f, 20f }.Select(m => LevelAfter(m, 400f, rules.XpPerKm, 1f)).ToArray();
                c.Check(levels.All(l => l == 5),
                    $"400 m from Sailing 0 gives level {string.Join("/", levels.Select(l => l.ToString()).ToArray())} at 5/8/10/20 m per second, expected 5");
                c.Check(LevelAfter(10f, 400f, rules.XpPerKm, 1.5f) == 6,
                    $"400 m Rested gives Sailing {LevelAfter(10f, 400f, rules.XpPerKm, 1.5f)}, expected 6");
            }
            else
            {
                c.Note("this world or character does not give 1 progress per XP: the levels of the test list are not checked as numbers");
            }

            // ----- 400 m at the helm from Sailing 0: 20 real samples of 20 m (there and back, the ship stays here) -----
            skills.ResetSkill(type);
            HelmSkill.Invalidate();
            var spy = new MessageSpy();
            var session = LogMark();
            SailingXp.Reset();
            yield return null;
            yield return null;
            var helmLine = $"At the helm of {shipName}: earning Sailing XP ({perKm} per km).";
            c.Check(LoggedExact(session, LogLevel.Debug, helmLine),
                $"no Debug line '{helmLine}' (lines: {Join(MyLines(session, LogLevel.Debug))})");
            // Validity of the 400 m: the mod's watch started from home (its private first position, read here).
            var lastField = AccessTools.Field(typeof(SailingXp), "_last");
            if (c.Check(lastField != null, "SailingXp._last not found: the start of the helm watch cannot be checked"))
            {
                var watchFrom = (Vector3)lastField.GetValue(null);
                c.Check(Flat(watchFrom - home).magnitude < 1e-3f,
                    $"the mod's helm watch did not start where the test put the ship (off by {F(watchFrom.x - home.x)} m in x, "
                    + $"{F(watchFrom.z - home.z)} m in z): the first 20 m sample below is not 20 m");
            }
            const int samples = 20;
            const float step = 20f;
            var taken = 0;
            for (var i = 0; i < samples; i++)
            {
                var has = skills.m_skillData.TryGetValue(type, out var s0);
                var level = has ? s0.m_level : 0f;
                var progress = has ? s0.m_accumulator : 0f;
                MoveShip(ship, i % 2 == 0 ? home + Vector3.right * step : home);
                var until = Time.time + 1.8f;
                var sampled = false;
                while (Time.time < until && !sampled)
                {
                    yield return null;
                    spy.Poll();
                    sampled = skills.m_skillData.TryGetValue(type, out var s1) && (s1.m_level != level || s1.m_accumulator != progress);
                }
                if (!sampled)
                {
                    break;
                }
                taken++;
            }
            for (var i = 0; i < 5; i++)
            {
                yield return null;
                spy.Poll();
            }
            c.Check(taken == samples, $"only {taken} of {samples} one-second samples paid Sailing XP at the helm");
            var reached = skills.m_skillData.TryGetValue(type, out var after) ? (int)after.m_level : 0;
            var expectedLevel = LevelAfter(step, samples * step, rules.XpPerKm, plain);
            c.Check(reached == expectedLevel, $"Sailing {reached} after {F(taken * step)} m at the helm, expected {expectedLevel}");
            string LevelUp(int n) => loc.Localize("$msg_skillup $skill_" + type.ToString().ToLower() + ": " + n);
            if (reached >= 1)
            {
                c.Check(spy.Centre.Contains(LevelUp(1)) && !spy.SawTopLeft(LevelUp(1)),
                    $"the first level-up is not the centre message '{LevelUp(1)}' (centre: {Join(spy.Centre)}; top left: "
                    + $"{Join(spy.TopLeft.Select(p => p.Key).ToList())})");
            }
            var missing = new List<string>();
            for (var n = 2; n <= reached; n++)
            {
                if (!spy.SawTopLeft(LevelUp(n), SailingSkill.Def.m_icon) || spy.Centre.Contains(LevelUp(n)))
                {
                    missing.Add(LevelUp(n));
                }
            }
            c.Check(reached >= 2 && missing.Count == 0 && SailingSkill.Def.m_icon != null,
                $"later level-ups are not top-left messages with the Sailing icon: {Join(missing)} (top left: "
                + $"{Join(spy.TopLeft.Select(p => p.Key + (p.Value != null ? " [" + p.Value.name + "]" : " [no icon]")).ToList(), 8)})");

            // Let go: the session line.
            var leave = LogMark();
            rig.ReleaseHelm();
            var km = (taken * step / 1000f).ToString("0.##", CultureInfo.InvariantCulture);
            var raw = (taken * step / 1000f * rules.XpPerKm).ToString("0.##", CultureInfo.InvariantCulture);
            var leftLine = $"Left the helm of {shipName}: {km} km sailed, {raw} Sailing XP (before bonuses).";
            var left = new Box();
            yield return WaitFor(() => LoggedExact(leave, LogLevel.Debug, leftLine), 1.6f, left);
            c.Check(left.Ok, $"no Debug line '{leftLine}' (lines: {Join(MyLines(leave, LogLevel.Debug))})");

            // ----- nothing while the ship is still -----
            MoveShip(ship, home);
            var sailing = SetSailing(player, 50f, 0f);
            c.Check(rig.TakeHelm(), "could not take the helm again");
            SailingXp.Reset();
            yield return new WaitForSeconds(3.4f);
            c.Check(ReferenceEquals(player.GetControlledShip(), ship) && sailing.m_accumulator == 0f && sailing.m_level == 50f,
                $"3 s at the helm of a ship that does not move gave {F(sailing.m_accumulator)} Sailing XP");
            var control = new Gain();
            yield return SampleXp(ship, sailing, 30f, control, restart: false);
            var expected = 30f / 1000f * rules.XpPerKm * perXp;
            c.Check(control.Sampled && Near(control.Xp, expected, expected * 0.03f),
                $"30 m at the helm right after gave {F(control.Xp)} XP, expected {F(expected)} (the watch above was not live)");

            // ----- nothing as a passenger: helm let go the vanilla way, player on the deck, ship moves 5 m/s -----
            rig.ReleaseHelm();
            c.Check(ship.m_shipControlls.GetUser() == 0L && player.GetControlledShip() == null,
                "letting go of the helm did not free it");
            var standing = new Box();
            yield return WaitFor(() => player.GetStandingOnShip() == ship, 3f, standing);
            c.Check(standing.Ok, "the player who let go of the helm does not stand on the deck");
            var held = sailing.m_accumulator;
            var from = ship.transform.position;
            var t0 = Time.time;
            while (Time.time - t0 < 3.3f)
            {
                yield return new WaitForFixedUpdate();
                var delta = Vector3.right * (5f * Time.fixedDeltaTime);
                MoveShip(ship, ship.transform.position + delta);
                Carry(player, delta);
            }
            var moved = Flat(ship.transform.position - from).magnitude;
            c.Check(moved > 12f && ship.IsPlayerInBoat(player),
                $"passenger check not valid: the ship moved {F(moved)} m, player still aboard {ship.IsPlayerInBoat(player)}");
            c.Check(sailing.m_accumulator == held && sailing.m_level == 50f,
                $"a passenger on a moving ship earned {F(sailing.m_accumulator - held)} Sailing XP");

            // ----- Rested: +50% -----
            c.Check(rig.TakeHelm(), "could not take the helm again");
            sailing = SetSailing(player, 50f, 0f);
            var notRested = new Gain();
            yield return SampleXp(ship, sailing, -30f, notRested);
            var rested = seman.AddStatusEffect(SEMan.s_statusEffectRested, true);
            var withRested = 1f;
            seman.ModifyRaiseSkill(type, ref withRested);
            var whenRested = new Gain();
            yield return SampleXp(ship, sailing, 30f, whenRested);
            c.Check(rested != null && seman.HaveStatusEffect(SEMan.s_statusEffectRested), "could not make the player Rested");
            c.Check(Near(withRested - plain, 0.5f, 1e-3f), $"Rested changes Sailing progress by {F(withRested - plain)}, expected +0.5");
            c.Check(notRested.Sampled && whenRested.Sampled && notRested.Xp > 0f
                    && Near(whenRested.Xp / notRested.Xp, withRested / plain, 0.05f),
                $"30 m gave {F(notRested.Xp)} XP not Rested and {F(whenRested.Xp)} Rested, expected x{F(withRested / plain)} (50% more)");

            rig.RestoreSkills();
            yield return rig.Leave(rig.Origin);
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            try
            {
                if (!hadRested)
                {
                    seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
                }
                else
                {
                    var se = seman.GetStatusEffect(SEMan.s_statusEffectRested);
                    if (se == null)
                    {
                        se = seman.AddStatusEffect(SEMan.s_statusEffectRested, true);
                    }
                    if (se != null)
                    {
                        se.m_ttl = restedTtl;
                        se.m_time = restedTime;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"Sailing self test clean-up failed: {e}");
            }
            rig.Restore();
        }
    }

    // ---------- sailing.hud ----------

    private static IEnumerator RunHud()
    {
        var c = new Checks(HudName);
        var rig = ShipRig.Create(HudName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        var wind = new WindScope();
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var player = rig.Player;
            var ship = rig.Boat;
            var hud = Hud.instance;
            var env = EnvMan.instance;
            if (!c.Check(hud != null && hud.IsVisible() && hud.m_shipWindIcon != null && wind.Ok,
                    "no visible HUD (or no weather): the wind icon cannot be read"))
            {
                yield return rig.Leave(rig.Origin);
                c.Report();
                yield break;
            }
            // Full wind blowing to +z, like the console's "wind 0 1".
            wind.Set(0f, 1f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            c.Check((env.GetWindDir() - wind.Dir).magnitude < 1e-3f && Near(env.GetWindIntensity(), 1f, 1e-3f),
                "the fixed wind did not hold");

            var keep = ship.transform.rotation;
            var grey = Hud.s_shipWindIconColor;
            var angles = new[] { 34f, 90f, 180f, 0f };
            var factor = new float[2, 4];
            var colour = new Color[2, 4];
            var noGo = new float[2];
            for (var li = 0; li < 2; li++)
            {
                var s = li == 0 ? 0f : 1f;
                HelmSkill.TestLocalLevel = s * 100f;
                // Turn toward the wind until the sail gives nothing at all: the edge of the no-go zone.
                noGo[li] = -1f;
                for (var a = 15f; a <= 50f; a += 0.25f)
                {
                    SetRotation(ship, Quaternion.LookRotation(HeadingOffWind(wind.Dir, a), Vector3.up));
                    if (ship.GetWindAngleFactor() > 1e-4f)
                    {
                        noGo[li] = a;
                        break;
                    }
                }
                var edge = SailMath.NoGoHalfAngle(s, rules.NoGoShiftAtMax);
                c.Check(noGo[li] > 0f && noGo[li] >= edge && noGo[li] <= edge + 0.5f,
                    $"Sailing {F(s * 100f)}: the sail starts to pull {F(noGo[li])} degrees off the wind, expected just over {F(edge)}");
                for (var ai = 0; ai < angles.Length; ai++)
                {
                    SetRotation(ship, Quaternion.LookRotation(HeadingOffWind(wind.Dir, angles[ai]), Vector3.up));
                    var d = Vector3.Dot(env.GetWindDir(), -ship.transform.forward);
                    factor[li, ai] = ship.GetWindAngleFactor();
                    hud.UpdateShipHud(player, 0f);
                    colour[li, ai] = hud.m_shipWindIcon.color;
                    var expected = SailMath.WindFactor(d, s, rules.WindFloorAtMax, rules.NoGoShiftAtMax);
                    c.Check(Near(factor[li, ai], expected, 1e-3f) && SameColor(colour[li, ai], Color.Lerp(grey, Color.white, expected)),
                        $"Sailing {F(s * 100f)}, {F(angles[ai])} degrees off the wind: pull {F(factor[li, ai])} (expected {F(expected)}), "
                        + $"HUD wind icon {C(colour[li, ai])} (expected {C(Color.Lerp(grey, Color.white, expected))})");
                }
            }
            SetRotation(ship, keep);
            // The test list's words.
            c.Check(factor[0, 0] == 0f && SameColor(colour[0, 0], grey) && factor[1, 0] > 0.85f && colour[1, 0].r > 0.85f,
                $"34 degrees off the wind: pull {F(factor[0, 0])} with icon {C(colour[0, 0])} at Sailing 0 (expected none, grey) and "
                + $"{F(factor[1, 0])} with icon {C(colour[1, 0])} at Sailing 100 (expected a good pull, nearly white)");
            c.Check(Near(noGo[0], 36.9f, 0.6f) && Near(noGo[1], 25.9f, 0.6f),
                $"no-go zone {F(noGo[0])} degrees at Sailing 0 and {F(noGo[1])} at 100, expected about 37 and about 26");
            c.Check(Near(factor[0, 1], 1f, 1e-3f) && Near(factor[1, 1], 1f, 1e-3f),
                $"wind from the side: pull {F(factor[0, 1])} at Sailing 0 and {F(factor[1, 1])} at 100, expected the same (1)");
            c.Check(Near(factor[0, 2], 0.7f, 1e-3f) && factor[1, 2] > factor[0, 2] + 0.15f,
                $"wind from behind: pull {F(factor[0, 2])} at Sailing 0 and {F(factor[1, 2])} at 100, expected 0.7 and clearly more");
            c.Check(factor[0, 3] == 0f && factor[1, 3] == 0f && SameColor(colour[1, 3], grey),
                $"straight into the wind: pull {F(factor[0, 3])} / {F(factor[1, 3])}, expected none at any level");

            // Rudder: steps of holding the key from centre to full lock (the helmsman's game makes one per physics step).
            var steps = new int[2];
            for (var li = 0; li < 2; li++)
            {
                HelmSkill.TestLocalLevel = li * 100f;
                ship.m_rudderValue = 0f;
                var n = 0;
                while (ship.m_rudderValue < 1f && n < 2000)
                {
                    ship.ApplyControlls(new Vector3(1f, 0f, 0f));
                    n++;
                }
                steps[li] = n;
                hud.UpdateShipHud(player, 0f);
                var indicator = hud.m_shipRudderIndicator;
                c.Check(indicator != null && indicator.gameObject.activeSelf && indicator.fillClockwise && Near(indicator.fillAmount, 0.25f, 1e-3f),
                    $"Sailing {li * 100}: the HUD rudder indicator does not show full lock");
            }
            ship.m_rudderValue = 0f;
            ship.m_rudder = 0f;
            var ratio = steps[0] > 0 ? steps[1] / (float)steps[0] : 0f;
            var wanted = 1f / SailMath.Scale(rules.RudderBonusAtMax, 1f);
            c.Check(steps[0] > 20 && steps[0] < 2000 && Near(ratio, wanted, 0.04f),
                $"rudder from centre to full lock: {F(steps[0] * Time.fixedDeltaTime)} s at Sailing 0, {F(steps[1] * Time.fixedDeltaTime)} s "
                + $"at 100 (x{F(ratio)}), expected about a third less (x{F(wanted)})");
            c.Check(FieldsLikePrefab(ship, rig.PrefabShip, out var fields), "ship fields not put back: " + fields);

            yield return rig.Leave(rig.Origin);
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            wind.Restore();
            rig.Restore();
        }
    }

    // ---------- sailing.sail ----------

    // Test list T18 (own test: its checks wait on no other item's). Sail response through the real patch, wind from the
    // side: the stored sail force never goes past where the game wants it, at the default and at the largest setting,
    // it gets there sooner both up and down, and a wind flip under a settled sail never pulls beyond the full pull.
    private static IEnumerator RunSail()
    {
        var c = new Checks(SailName);
        var rig = ShipRig.Create(SailName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        var wind = new WindScope();
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            if (!ok.Ok || !c.Check(wind.Ok, "no weather (EnvMan): the wind cannot be fixed"))
            {
                c.Report();
                yield break;
            }
            var ship = rig.Boat;
            HelmSkill.TestLocalLevel = 100f;
            // Full wind blowing to +z, like the console's "wind 0 1".
            wind.Set(0f, 1f);
            // After these the code below run inside the physics step: Time.deltaTime is the fixed step, like for the
            // game's own GetSailForce call.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var keep = ship.transform.rotation;
            SetRotation(ship, Quaternion.LookRotation(HeadingOffWind(wind.Dir, 90f), Vector3.up));
            c.Check(Near(ship.GetWindAngleFactor(), 1f, 1e-3f) && ship.m_sailForceFactor > 0f,
                $"wind from the side: the sail's pull is {F(ship.GetWindAngleFactor())} (expected 1), sail force factor {F(ship.m_sailForceFactor)}");
            c.Note($"sail response measured with a step of {F(Time.deltaTime)} s (physics step {F(Time.fixedDeltaTime)} s)");
            CheckSailResponse(c, ship, rules, rules.SailResponseAtMax, wind);
            CheckSailResponse(c, ship, rules, SailingRules.SailResponseMax, wind);
            SetRotation(ship, keep);
            c.Check(FieldsLikePrefab(ship, rig.PrefabShip, out var fields), "ship fields not put back: " + fields);

            yield return rig.Leave(rig.Origin);
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            wind.Restore();
            rig.Restore();
        }
    }

    // Owner step's GetSailForce (vanilla SmoothDamp, then my catch-up in the postfix), called half a second or more per
    // sail setting: Full, Stop, Full, Half, Stop. Target = vanilla's own (Ship.GetSailForce). Never past it (so never
    // the other way when the sail empties). Half a second after the sail went up from empty, and half a second after it
    // came down from full (both passes start from the same state), the force is closer to its target than without the
    // sail response. Then the wind flipped under a settled full sail and flipped back (MeasureWindFlip): the pull never
    // beyond the full pull for that heading, and 3 s later it sits on the game's own target.
    private static void CheckSailResponse(Checks c, Ship ship, SailingRules rules, float rate, WindScope wind)
    {
        var env = EnvMan.instance;
        if (env == null)
        {
            c.Note("no EnvMan: sail response not checked");
            return;
        }
        var keepForce = ship.m_sailForce;
        var keepVelocity = ship.m_windChangeVelocity;
        // Vanilla SmoothDamp inside GetSailForce step by Time.deltaTime on its own: my catch-up get the same step.
        // (Called after WaitForFixedUpdate with no frame in between = the physics step, like the game's own call.)
        var dt = Time.deltaTime > 0f ? Time.deltaTime : 0.02f;
        var steps = Mathf.Max(1, Mathf.RoundToInt(0.5f / dt));
        // Calls per sail setting: always more than the half second me measure (a small step need more calls).
        var calls = Mathf.Max(60, steps + 5);
        var plan = new[] { 1f, 0f, 1f, 0.5f, 0f };
        var left = new float[2];
        var down = new float[2];
        var past = 0;
        var total = 0;
        // Vanilla target of the sail force for this sail size, with the wind and heading of right now.
        Vector3 Target(float size)
        {
            var pull = ship.GetWindAngleFactor() * Mathf.Lerp(0.25f, 1f, env.GetWindIntensity());
            return Vector3.Normalize(env.GetWindDir() + ship.transform.forward) * (pull * ship.m_sailForceFactor * size);
        }
        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                var tested = new SailingRules
                {
                    WindFloorAtMax = rules.WindFloorAtMax,
                    NoGoShiftAtMax = rules.NoGoShiftAtMax,
                    SailResponseAtMax = pass == 0 ? 0f : rate,
                };
                ship.m_sailForce = Vector3.zero;
                ship.m_windChangeVelocity = Vector3.zero;
                ShipTick.Begin(ship, 1f, tested, 1f);
                try
                {
                    for (var k = 0; k < plan.Length; k++)
                    {
                        var size = plan[k];
                        var target = Target(size);
                        var approach = target - ship.m_sailForce;
                        for (var i = 0; i < calls; i++)
                        {
                            ship.GetSailForce(size, dt);
                            if (pass == 1)
                            {
                                total++;
                                if (Vector3.Dot(target - ship.m_sailForce, approach) < -1e-9f)
                                {
                                    past++;
                                }
                            }
                            if (k == 0 && i == steps - 1)
                            {
                                left[pass] = (target - ship.m_sailForce).magnitude;
                            }
                        }
                    }
                    // Sail down from a full, settled sail (same start in both passes): pull left half a second later.
                    ship.m_sailForce = Target(1f);
                    ship.m_windChangeVelocity = Vector3.zero;
                    for (var i = 0; i < steps; i++)
                    {
                        ship.GetSailForce(0f, dt);
                    }
                    down[pass] = ship.m_sailForce.magnitude;
                }
                finally
                {
                    ShipTick.End();
                }
            }
        }
        finally
        {
            ship.m_sailForce = keepForce;
            ship.m_windChangeVelocity = keepVelocity;
        }
        c.Check(past == 0 && total > 0,
            $"sail response {F(rate)}/s at Sailing 100: the sail force went past its target on {past} of {total} steps (sail up, down, half)");
        c.Check(left[0] > 0f && left[1] < left[0] * 0.9f,
            $"sail response {F(rate)}/s at Sailing 100: {F(left[1] * 1000f)} (x0.001) short of full pull half a second after the sail went up, "
            + $"against {F(left[0] * 1000f)} without it, expected clearly less");
        c.Check(down[0] > 0f && down[1] < down[0] * 0.9f,
            $"sail response {F(rate)}/s at Sailing 100: {F(down[1] * 1000f)} (x0.001) of pull left half a second after the sail came down, "
            + $"against {F(down[0] * 1000f)} without it, expected clearly less");
        // Test list T18 flips the wind "a few seconds" after the sail went up: 3 s, the sail is settled by then.
        MeasureWindFlip(ship, rules, rate, wind, 3f, out var flips, out var strongest, out var astray);
        c.Check(flips == 2 && strongest <= 1.001f && astray <= 0.02f,
            $"sail response {F(rate)}/s at Sailing 100, wind flipped to the other side and back under a settled full sail ({flips} of 2 "
            + $"flips measured): strongest pull {F(strongest * 100f)}% of the full pull for that heading (expected no more than 100%), "
            + $"{F(astray * 100f)}% of the way to the game's own target left 3 s after a flip (expected under 2%)");
    }

    // Wind flipped to the other side under full sail (console "wind 180 1") and flipped back, 3 s each, through the real
    // patch at Sailing 100 (owner step context set by hand, like CheckSailResponse). fill = seconds at Full, from an
    // empty sail, before the first flip. With the wind from the side the target turns a quarter at each flip.
    // Out: flips measured (2 = all), the largest pull seen as a share of the full pull for that heading, and the way
    // left to the game's own target 3 s after a flip as a share of the way it had to go. rate 0 = no sail response
    // (the normal game's own SmoothDamp).
    private static void MeasureWindFlip(Ship ship, SailingRules rules, float rate, WindScope wind, float fill,
        out int flips, out float strongest, out float astray)
    {
        flips = 0;
        strongest = 0f;
        astray = 0f;
        var env = EnvMan.instance;
        if (env == null)
        {
            return;
        }
        var keepForce = ship.m_sailForce;
        var keepVelocity = ship.m_windChangeVelocity;
        var dt = Time.deltaTime > 0f ? Time.deltaTime : 0.02f;
        var settle = Mathf.Max(1, Mathf.CeilToInt(3f / dt));
        var tested = new SailingRules
        {
            WindFloorAtMax = rules.WindFloorAtMax,
            NoGoShiftAtMax = rules.NoGoShiftAtMax,
            SailResponseAtMax = rate,
        };
        ship.m_sailForce = Vector3.zero;
        ship.m_windChangeVelocity = Vector3.zero;
        ShipTick.Begin(ship, 1f, tested, 1f);
        try
        {
            wind.Set(0f, 1f);
            for (var i = Mathf.CeilToInt(fill / dt); i > 0; i--)
            {
                ship.GetSailForce(1f, dt);
            }
            foreach (var angle in new[] { 180f, 0f })
            {
                wind.Set(angle, 1f);
                var pull = ship.GetWindAngleFactor() * Mathf.Lerp(0.25f, 1f, env.GetWindIntensity());
                var target = Vector3.Normalize(env.GetWindDir() + ship.transform.forward) * (pull * ship.m_sailForceFactor);
                var way = (target - ship.m_sailForce).magnitude;
                var full = target.magnitude;
                if (way < 1e-6f || full < 1e-6f)
                {
                    continue;   // no pull on this heading, or nothing to do: flips stay under 2, the caller's check fail
                }
                for (var i = 0; i < settle; i++)
                {
                    ship.GetSailForce(1f, dt);
                    strongest = Mathf.Max(strongest, ship.m_sailForce.magnitude / full);
                }
                astray = Mathf.Max(astray, (target - ship.m_sailForce).magnitude / way);
                flips++;
            }
        }
        finally
        {
            ShipTick.End();
            wind.Set(0f, 1f);
            ship.m_sailForce = keepForce;
            ship.m_windChangeVelocity = keepVelocity;
        }
    }

    // ---------- sailing.bug.sail-overshoot ----------

    // Real bug, small (test list T19). Design doc say the sail response is "bounded by the target". That hold while the
    // force, its SmoothDamp speed and the target sit on one line (sail up, sail down, wind flip under a settled sail):
    // Unity's own overshoot guard in SmoothDamp catch the step that would cross. Wind flipped while the sail still
    // fill (1.2 s after Full, wind from the side): the target turn a quarter, the guard see no crossing on the new
    // line, and my catch-up (it move the force, not its SmoothDamp speed) let the force swing a little past: about 0.5%
    // over the full pull with the default setting, about 2 s after the flip (computed outside the game from Unity's
    // SmoothDamp code and my catch-up; this test measure it in the game). Without the sail response the normal game
    // stay under the full pull. Alone here: this test fail until the mod (or the claim) change.
    private static IEnumerator RunBugSailOvershoot()
    {
        var c = new Checks(BugSailName);
        var rig = ShipRig.Create(BugSailName);
        if (rig == null)
        {
            yield break;
        }
        var wind = new WindScope();
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            if (!ok.Ok || !c.Check(wind.Ok, "no weather (EnvMan): the wind cannot be fixed"))
            {
                c.Report();
                yield break;
            }
            var ship = rig.Boat;
            HelmSkill.TestLocalLevel = 100f;
            wind.Set(0f, 1f);
            // After these the code below run inside the physics step: Time.deltaTime is the fixed step, like for the
            // game's own GetSailForce call.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var keep = ship.transform.rotation;
            SetRotation(ship, Quaternion.LookRotation(HeadingOffWind(wind.Dir, 90f), Vector3.up));
            MeasureWindFlip(ship, rules, rules.SailResponseAtMax, wind, 1.2f, out var flips, out var strongest, out var astray);
            MeasureWindFlip(ship, rules, 0f, wind, 1.2f, out var plainFlips, out var plain, out _);
            SetRotation(ship, keep);
            c.Note($"wind flipped 1.2 s after Full sail (wind from the side, step {F(Time.deltaTime)} s): strongest pull "
                   + $"{F(strongest * 100f)}% of the full pull with the sail response ({F(rules.SailResponseAtMax)}/s), {F(plain * 100f)}% "
                   + $"without it; {F(astray * 100f)}% of the way to the target left 3 s after a flip");
            c.Check(flips == 2 && plainFlips == 2 && plain <= 1.001f,
                $"check not valid: {flips} and {plainFlips} of 2 flips measured, the normal game's own pull reached {F(plain * 100f)}% of the full pull");
            c.Check(strongest <= 1.001f,
                $"wind flipped while the sail was still filling (Sailing 100, sail response {F(rules.SailResponseAtMax)}/s): the pull went "
                + $"up to {F(strongest * 100f)}% of the full pull for that heading, expected never beyond it (the normal game: {F(plain * 100f)}%)");
            yield return rig.Leave(rig.Origin);
            c.Report();
        }
        finally
        {
            wind.Restore();
            rig.Restore();
        }
    }

    // ---------- sailing.bug.wind-icon-foreign ----------

    // Real bug (test list M13): the helmsman's HUD wind icon must match what the ship really gets. A ship simulated by
    // a game that does not run the mod (owner published no Sailing level) sails vanilla, so the icon must be vanilla
    // too. Stand-in for that other game: the ship's owner set to a session that is not here, for one HUD update.
    private static IEnumerator RunHudForeign()
    {
        var c = new Checks(HudForeignName);
        var rig = ShipRig.Create(HudForeignName);
        if (rig == null)
        {
            yield break;
        }
        var wind = new WindScope();
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var player = rig.Player;
            var ship = rig.Boat;
            var hud = Hud.instance;
            if (!c.Check(hud != null && hud.IsVisible() && hud.m_shipWindIcon != null && wind.Ok,
                    "no visible HUD (or no weather): the wind icon cannot be read"))
            {
                yield return rig.Leave(rig.Origin);
                c.Report();
                yield break;
            }
            wind.Set(0f, 1f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            HelmSkill.TestLocalLevel = 100f;
            var keep = ship.transform.rotation;
            var grey = Hud.s_shipWindIconColor;
            SetRotation(ship, Quaternion.LookRotation(HeadingOffWind(wind.Dir, 34f), Vector3.up));
            var d = Vector3.Dot(EnvMan.instance.GetWindDir(), -ship.transform.forward);
            hud.UpdateShipHud(player, 0f);
            var own = hud.m_shipWindIcon.color;
            c.Check(own.r > 0.85f, $"own ship at Sailing 100, 34 degrees off the wind: icon {C(own)}, expected nearly white");
            var me = ZDOMan.GetSessionID();
            var other = me == long.MaxValue ? me - 1L : me + 1L;
            Color foreign;
            try
            {
                rig.Zdo.SetOwner(other);
                c.Check(!ship.IsOwner() && ReferenceEquals(player.GetControlledShip(), ship), "the stand-in for another game's ship did not hold");
                hud.UpdateShipHud(player, 0f);
                foreign = hud.m_shipWindIcon.color;
            }
            finally
            {
                rig.Zdo.SetOwner(me);
                SetRotation(ship, keep);
            }
            var vanilla = Color.Lerp(grey, Color.white, SailMath.VanillaWindFactor(d));
            c.Check(SameColor(foreign, vanilla),
                $"at the helm of a ship that a game without the mod simulates (34 degrees off the wind, Sailing 100): HUD wind "
                + $"icon {C(foreign)}, expected the normal game's {C(vanilla)}: the icon promises a pull the ship does not get");

            yield return rig.Leave(rig.Origin);
            c.Report();
        }
        finally
        {
            wind.Restore();
            rig.Restore();
        }
    }

    // ---------- sailing.map ----------

    private static IEnumerator RunMap()
    {
        var c = new Checks(MapName);
        var rig = ShipRig.Create(MapName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var player = rig.Player;
            var ship = rig.Boat;
            var map = Minimap.instance;
            if (!c.Check(map != null && map.m_explored != null, "no minimap: the map reveal cannot be read"))
            {
                yield return rig.Leave(rig.Origin);
                c.Report();
                yield break;
            }
            var radius = map.m_exploreRadius;
            var wide = radius * SailMath.Scale(rules.RevealBonusAtMax, 1f);
            var metres = new float[2];

            // At the helm.
            for (var li = 0; li < 2; li++)
            {
                HelmSkill.TestLocalLevel = li * 100f;
                var want = li == 0 ? radius : wide;
                var r = RevealReach(player);
                metres[li] = r.Reach * map.m_pixelSize;
                c.Check(r.Ok && r.Reach == ReachOf(map, want) && Near(r.Used, want, 0.01f),
                    $"at the helm, Sailing {li * 100}: map revealed {F(r.Reach * map.m_pixelSize)} m around the player "
                    + $"(radius used {F(r.Used)}), expected {F(ReachOf(map, want) * map.m_pixelSize)} m (radius {F(want)})");
            }
            c.Check(metres[1] >= 2f * radius && metres[1] < 2f * radius + map.m_pixelSize && metres[0] >= radius
                    && metres[0] < radius + map.m_pixelSize,
                $"revealed circle {F(metres[0])} m at Sailing 0 and {F(metres[1])} m at 100, expected about {F(radius)} and about "
                + $"{F(2f * radius)} (map pixels are {F(map.m_pixelSize)} m)");

            // On the deck, helm let go.
            rig.ReleaseHelm();
            var standing = new Box();
            yield return WaitFor(() => player.GetStandingOnShip() == ship, 3f, standing);
            if (c.Check(standing.Ok && !player.IsAttached() && player.GetControlledShip() == null,
                    "the player who let go of the helm does not stand on the deck"))
            {
                for (var li = 0; li < 2; li++)
                {
                    HelmSkill.TestLocalLevel = li * 100f;
                    var want = li == 0 ? radius : wide;
                    var r = RevealReach(player);
                    c.Check(r.Ok && r.Reach == ReachOf(map, want) && Near(r.Used, want, 0.01f),
                        $"standing on the deck, Sailing {li * 100}: map revealed {F(r.Reach * map.m_pixelSize)} m, expected "
                        + $"{F(ReachOf(map, want) * map.m_pixelSize)} m");
                }
            }
            c.Check(map.m_exploreRadius == radius, $"the map's reveal radius was left at {F(map.m_exploreRadius)}, not {F(radius)}");

            // Ashore.
            yield return rig.Leave(rig.Origin);
            HelmSkill.TestLocalLevel = 100f;
            var ashore = RevealReach(player);
            c.Check(ashore.Ok && ashore.Reach == ReachOf(map, radius) && Near(ashore.Used, radius, 0.01f),
                $"ashore at Sailing 100: map revealed {F(ashore.Reach * map.m_pixelSize)} m, expected the normal "
                + $"{F(ReachOf(map, radius) * map.m_pixelSize)} m");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sailing.damage ----------

    private static IEnumerator RunDamage()
    {
        var c = new Checks(DamageName);
        var rig = ShipRig.Create(DamageName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        GameObject creature = null;
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            if (!ok.Ok || !c.Check(rig.Wear != null, "the ship has no WearNTear"))
            {
                c.Report();
                yield break;
            }
            var player = rig.Player;
            var ship = rig.Boat;
            var wear = rig.Wear;
            var zdo = rig.Zdo;
            var shipName = Utils.GetPrefabName(ship.gameObject);
            var cut = SailMath.DamageFactor(rules.DamageReductionAtMax, 1f);

            // Every way a ship gets hurt that is no player's doing: creature, collision (Boat), Ashlands sea, cinder
            // fire, and a hit without a type (water slam). 10 blunt each (fire damage would set the ship alight).
            HelmSkill.TestLocalLevel = 0f;
            var full = DamageTaken(rig, HitData.HitType.EnemyHit, null);
            c.Check(full > 0f, "a 10 blunt hit did no damage to the ship");
            foreach (var type in new[]
                     {
                         HitData.HitType.EnemyHit, HitData.HitType.Boat, HitData.HitType.AshlandsOcean,
                         HitData.HitType.CinderFire, HitData.HitType.Undefined,
                     })
            {
                HelmSkill.TestLocalLevel = 0f;
                var silent = LogMark();
                var plain = DamageTaken(rig, type, null);
                var spoke = CountLogged(silent, LogLevel.Debug, "best Sailing aboard");
                HelmSkill.TestLocalLevel = 100f;
                var mark = LogMark();
                var reduced = DamageTaken(rig, type, null);
                var line = DamageLine(shipName, type, 10f, 10f * cut, 100f);
                c.Check(Near(plain, full, 0.01f) && spoke == 0 && Near(reduced, full * cut, 0.01f),
                    $"{type} hit: {F(plain)} at Sailing 0, {F(reduced)} at 100, expected {F(full)} and {F(full * cut)}");
                c.Check(LoggedExact(mark, LogLevel.Debug, line), $"no Debug line '{line}' (lines: {Join(MyLines(mark, LogLevel.Debug))})");
            }

            // A creature as the attacker (the Serpent of the test list: name not in the game code, so checked here).
            var prefab = ZNetScene.instance.GetPrefab("Serpent");
            var body = prefab != null ? prefab.GetComponent<Character>() : null;
            if (c.Check(body != null && !body.IsPlayer(), "no creature prefab named 'Serpent'"))
            {
                creature = Object.Instantiate(prefab, ship.transform.position + ship.transform.right * 12f, Quaternion.identity);
                yield return null;
                yield return null;
                var attacker = creature != null ? creature.GetComponent<Character>() : null;
                if (c.Check(attacker != null, "the spawned Serpent is gone"))
                {
                    HelmSkill.TestLocalLevel = 100f;
                    var mark = LogMark();
                    var bite = DamageTaken(rig, HitData.HitType.EnemyHit, attacker);
                    c.Check(Near(bite, full * cut, 0.01f) && LoggedExact(mark, LogLevel.Debug, DamageLine(shipName, HitData.HitType.EnemyHit, 10f, 10f * cut, 100f)),
                        $"hit with a Serpent as attacker at Sailing 100 took {F(bite)}, expected {F(full * cut)} and the Debug line");
                }
                if (creature != null)
                {
                    ZNetScene.instance.Destroy(creature);
                    creature = null;
                }
            }

            // Not reduced: a player's own hit (vanilla melee: hit type PlayerHit and the player as attacker).
            HelmSkill.TestLocalLevel = 100f;
            var quiet = LogMark();
            var axe = DamageTaken(rig, HitData.HitType.PlayerHit, player);
            var byType = DamageTaken(rig, HitData.HitType.PlayerHit, null);
            var byAttacker = DamageTaken(rig, HitData.HitType.EnemyHit, player);
            var catapult = DamageTaken(rig, HitData.HitType.Catapult, null);
            c.Check(Near(axe, full, 0.01f) && Near(byType, full, 0.01f) && Near(byAttacker, full, 0.01f) && Near(catapult, full, 0.01f),
                $"player hits at Sailing 100 took {F(axe)} (own weapon), {F(byType)} (PlayerHit), {F(byAttacker)} (player attacker), "
                + $"{F(catapult)} (catapult), expected the full {F(full)}");
            c.Check(CountLogged(quiet, LogLevel.Debug, "best Sailing aboard") == 0, "a player's hit logged the 'best Sailing aboard' Debug line");

            // Not reduced: the game's own capsized damage (Ship.UpdateUpsideDmg, every m_upsideDownDmgInterval).
            var upright = ship.transform.rotation;
            var tick = full * ship.m_upsideDownDmg / 10f;
            var capsized = LogMark();
            var first = -1f;
            var second = -1f;
            try
            {
                zdo.Set(ZDOVars.s_health, wear.m_health);
                SetRotation(ship, upright * Quaternion.Euler(0f, 0f, 180f));
                ship.m_upsideDownDmgTimer = 0f;
                var t0 = Time.time;
                while (Time.time - t0 < 3.2f && second < 0f)
                {
                    yield return new WaitForFixedUpdate();
                    var lost = wear.m_health - zdo.GetFloat(ZDOVars.s_health, wear.m_health);
                    if (first < 0f && lost > 0.01f)
                    {
                        first = Time.time - t0;
                        c.Check(Near(lost, tick, 0.05f), $"capsized at Sailing 100: the first tick took {F(lost)}, expected the full {F(tick)}");
                    }
                    else if (first >= 0f && lost > tick + 0.05f)
                    {
                        second = Time.time - t0;
                        c.Check(Near(lost, 2f * tick, 0.1f), $"capsized at Sailing 100: two ticks took {F(lost)}, expected {F(2f * tick)}");
                    }
                }
            }
            finally
            {
                SetRotation(ship, upright);
                zdo.Set(ZDOVars.s_health, wear.m_health);
            }
            c.Check(first >= 0f && second >= 0f && Near(second - first, ship.m_upsideDownDmgInterval, 0.2f),
                $"capsized ship lost health at {F(first)} s and {F(second)} s, expected every {F(ship.m_upsideDownDmgInterval)} s");
            c.Check(CountLogged(capsized, LogLevel.Debug, "best Sailing aboard") == 0, "capsized damage logged the 'best Sailing aboard' Debug line");

            yield return rig.Leave(rig.Origin);
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            if (creature != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(creature);
            }
            rig.Restore();
        }
    }

    // ---------- sailing.ram ----------

    // Me look for flat ground with nothing on it within 7.5 m (only terrain): thrown ship hit no tree, rock or building.
    private static bool FindClearGround(Vector3 origin, out Vector3 ground)
    {
        ground = origin;
        var zones = ZoneSystem.instance;
        if (zones == null)
        {
            return false;
        }
        for (var r = 14f; r <= 44f; r += 6f)
        {
            for (var a = 0; a < 360; a += 30)
            {
                var p = origin + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                if (!zones.GetGroundHeight(p, out var h) || h < zones.m_waterLevel + 0.5f)
                {
                    continue;
                }
                var flat = true;
                for (var b = 0; b < 360 && flat; b += 90)
                {
                    var q = p + Quaternion.Euler(0f, b, 0f) * Vector3.forward * 5f;
                    flat = zones.GetGroundHeight(q, out var hq) && Mathf.Abs(hq - h) < 1.5f;
                }
                if (!flat)
                {
                    continue;
                }
                var clear = true;
                foreach (var hit in Physics.OverlapSphere(new Vector3(p.x, h + 2f, p.z), 7.5f))
                {
                    if (hit == null || hit.isTrigger || hit.GetComponentInParent<Heightmap>() != null)
                    {
                        continue;
                    }
                    clear = false;
                    break;
                }
                if (clear)
                {
                    ground = new Vector3(p.x, h, p.z);
                    return true;
                }
            }
        }
        return false;
    }

    private static IEnumerator RunRam()
    {
        var c = new Checks(RamName);
        var rig = ShipRig.Create(RamName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            // No such ground = FAIL, not a quiet pass: this test stands for the collision part of the test list.
            if (!c.Check(FindClearGround(rig.Origin, out var ground),
                    "no flat ground free of trees, rocks and buildings within 44 m of the player: collision not checked"))
            {
                c.Report();
                yield break;
            }
            if (!c.Check(rig.Spawn(ground + Vector3.up * 6f, Quaternion.identity, kinematic: true), "no ship prefab (Karve) or it did not spawn"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            yield return null;
            var ship = rig.Boat;
            var wear = rig.Wear;
            var zdo = rig.Zdo;
            var body = ship.m_body;
            var impact = rig.Go.GetComponentInChildren<ImpactEffect>(true);
            if (!c.Check(wear != null && body != null && impact != null && impact.m_damageToSelf && impact.m_hitType == HitData.HitType.Boat
                         && impact.m_damages.m_blunt > 0f,
                    "the ship has no collision damage to itself (ImpactEffect, hit type Boat, blunt)")
                || !c.Check(rig.TakeHelm(), "the ship has no helm attach point"))
            {
                c.Report();
                yield break;
            }
            var aboard = new Box();
            yield return rig.WaitAboard(aboard);
            if (!c.Check(aboard.Ok, "the player at the helm never entered the ship's volume"))
            {
                c.Report();
                yield break;
            }
            var shipName = Utils.GetPrefabName(ship.gameObject);
            var cut = SailMath.DamageFactor(rules.DamageReductionAtMax, 1f);
            HelmSkill.TestLocalLevel = 0f;
            var perPoint = DamageTaken(rig, HitData.HitType.Boat, null) / 10f;
            var blunt = impact.m_damages.m_blunt;
            var allBlunt = Near(impact.m_damages.GetTotalDamage(), blunt, 0.001f);
            var lost = new float[2];
            var marks = new int[2];
            for (var li = 0; li < 2; li++)
            {
                HelmSkill.TestLocalLevel = li * 100f;
                body.isKinematic = true;
                MoveShip(ship, ground + Vector3.up * 3.5f);
                SetRotation(ship, Quaternion.identity);
                // Vanilla pause between two collision hits (ImpactEffect.m_interval), and the ship at rest.
                yield return new WaitForSeconds(Mathf.Max(0.7f, impact.m_interval + 0.3f));
                zdo.Set(ZDOVars.s_health, wear.m_health);
                marks[li] = LogMark();
                // Down faster than the speed of full collision damage (m_maxVelocity).
                body.isKinematic = false;
                body.linearVelocity = Vector3.down * (impact.m_maxVelocity + 2f);
                body.angularVelocity = Vector3.zero;
                var t0 = Time.time;
                while (Time.time - t0 < 2.5f)
                {
                    yield return new WaitForFixedUpdate();
                    lost[li] = wear.m_health - zdo.GetFloat(ZDOVars.s_health, wear.m_health);
                    if (lost[li] > 0.01f)
                    {
                        break;
                    }
                }
                body.isKinematic = true;
            }
            zdo.Set(ZDOVars.s_health, wear.m_health);
            c.Note($"{shipName} thrown at the ground at {F(impact.m_maxVelocity + 2f)} m/s: lost {F(lost[0])} health at Sailing 0, "
                   + $"{F(lost[1])} at 100 (collision damage {F(blunt)} blunt from {F(impact.m_minVelocity)} m/s, full from {F(impact.m_maxVelocity)} m/s)");
            if (c.Check(lost[0] > 0.01f && lost[1] > 0.01f, "the ship thrown at the ground took no collision damage"))
            {
                if (allBlunt)
                {
                    c.Check(Near(lost[0], blunt * perPoint, blunt * perPoint * 0.02f),
                        $"collision at Sailing 0 took {F(lost[0])}, expected the full {F(blunt * perPoint)}");
                }
                else
                {
                    c.Note("the ship's collision damage is not blunt only: the full hit is not checked as a number");
                }
                c.Check(Near(lost[1], lost[0] * cut, lost[0] * 0.02f),
                    $"collision at Sailing 100 took {F(lost[1])}, expected {F(lost[0] * cut)} (half of {F(lost[0])})");
                var total = impact.m_damages.GetTotalDamage();
                var line = DamageLine(shipName, HitData.HitType.Boat, total, total * cut, 100f);
                c.Check(LoggedExact(marks[1], LogLevel.Debug, line), $"no Debug line '{line}' (lines: {Join(MyLines(marks[1], LogLevel.Debug))})");
                c.Check(CountLogged(marks[0], LogLevel.Debug, "best Sailing aboard") == CountLogged(marks[1], LogLevel.Debug, "best Sailing aboard"),
                    "the collision at Sailing 0 logged the 'best Sailing aboard' Debug line");
            }

            yield return rig.Leave(rig.Origin);
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sailing.toggle ----------

    private static IEnumerator RunToggle()
    {
        var c = new Checks(ToggleName);
        var rig = ShipRig.Create(ToggleName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        Skills fresh = null;
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            var view = FeatureRegistry.Find(ModInfo.Guid);
            if (!ok.Ok || !c.Check(view != null && view.Value.IsActive && !Plugin.TestBlocked, "the mod is not active at the start"))
            {
                c.Report();
                yield break;
            }
            var feature = view.Value;
            var player = rig.Player;
            var ship = rig.Boat;
            var skills = player.GetSkills();
            var type = SailingSkill.Type;
            var shipName = Utils.GetPrefabName(ship.gameObject);
            var home = ship.transform.position;
            var map = Minimap.instance;
            c.Check(map != null && rig.Wear != null, "no minimap or the ship has no WearNTear: map reveal or ship damage not checked");
            var radius = map != null ? map.m_exploreRadius : 0f;
            var vanillaStep = 0.5f * ship.m_rudderSpeed * Time.fixedDeltaTime;
            var accel = SailMath.Scale(rules.AccelerationBonusAtMax, 1f);
            var turn = SailMath.Scale(rules.TurnBonusAtMax, 1f);
            var rudder = SailMath.Scale(rules.RudderBonusAtMax, 1f);
            var wide = radius * SailMath.Scale(rules.RevealBonusAtMax, 1f);
            var cut = SailMath.DamageFactor(rules.DamageReductionAtMax, 1f);
            var perKm = rules.XpPerKm.ToString("0.##", CultureInfo.InvariantCulture);

            // The character's own Sailing 100 (my overrides are wiped each time the feature goes off).
            HelmSkill.TestLocalLevel = 0f;
            var full = rig.Wear != null ? DamageTaken(rig, HitData.HitType.EnemyHit, null) : 0f;
            HelmSkill.TestLocalLevel = null;
            rig.BackupSkills();
            var sailing = SetSailing(player, 100f, 0f);
            LevelPublisher.PublishNow();
            ShipFixedUpdatePatches.LastShip = null;
            yield return FixedSteps(3);
            c.Check(ReferenceEquals(ShipFixedUpdatePatches.LastShip, ship) && Near(ShipFixedUpdatePatches.LastAccel, accel)
                    && Near(HelmSkill.Published(player), 100f) && OwnShipPrefixes() == 1,
                "before the toggle: the bonuses of Sailing 100 are not there");

            // ----- off -----
            var off = LogMark();
            SetBlocked(true);
            c.Check(!feature.IsActive && feature.Status == Plugin.TestBlockedText, $"turned off: the mod is {feature.State} '{feature.Status}'");
            c.Check(LoggedExact(off, LogLevel.Debug, "Withdrew the published Sailing level (mod turned off).")
                    && HelmSkill.Published(player) == HelmSkill.NotPublished,
                $"turned off: the published level was not withdrawn (now {F(HelmSkill.Published(player))})");
            ShipFixedUpdatePatches.LastShip = null;
            yield return FixedSteps(3);
            var like = FieldsLikePrefab(ship, rig.PrefabShip, out var fields);
            c.Check(OwnShipPrefixes() == 0 && ShipFixedUpdatePatches.LastShip == null && like,
                "turned off: the ship's physics step still uses the skill: " + fields);
            c.Check(Near(RudderStep(ship), vanillaStep, 1e-5f), "turned off: the rudder still swings faster");
            CheckWind(c, ship, rules, 0f, "turned off");
            if (map != null)
            {
                var r = RevealReach(player);
                c.Check(r.Ok && r.Reach == ReachOf(map, radius) && r.Used < 0f && map.m_exploreRadius == radius,
                    $"turned off: map revealed {F(r.Reach * map.m_pixelSize)} m at the helm, expected the normal {F(ReachOf(map, radius) * map.m_pixelSize)} m");
            }
            if (rig.Wear != null)
            {
                var taken = DamageTaken(rig, HitData.HitType.EnemyHit, null);
                c.Check(Near(taken, full, 0.01f), $"turned off: the ship took {F(taken)}, expected the full {F(full)}");
            }
            sailing = SetSailing(player, 50f, 0f);   // under 100 so XP would show
            MoveShip(ship, home + Vector3.right * 30f);
            yield return new WaitForSeconds(1.3f);
            MoveShip(ship, home);
            c.Check(sailing.m_accumulator == 0f && sailing.m_level == 50f, $"turned off: 30 m at the helm gave {F(sailing.m_accumulator)} Sailing XP");
            sailing = SetSailing(player, 100f, 0f);
            // Level kept and shown while off; a character loaded while off keeps it (always-on registration).
            c.Check(skills.m_skillData.TryGetValue(type, out var kept) && kept.m_level == 100f
                    && ReferenceEquals(skills.GetSkillDef(type), SailingSkill.Def) && Skills.IsSkillValid(type),
                "turned off: the character lost Sailing 100, or the skill is no longer known to the game");
            yield return ShowInventory(c);
            var rows = OpenPanel();
            var row = FindRow(rows, SailingSkill.DisplayName);
            c.Check(row != null && row.Level == "100" && row.Icon != null && row.Icon == SailingSkill.Def.m_icon,
                $"turned off: the Skills panel shows Sailing '{(row != null ? row.Level : "no row")}', expected 100 with its icon");
            ClosePanel();
            fresh = FreshSkills(player);
            var pkg = new ZPackage();
            skills.Save(pkg);
            pkg.SetPos(0);
            fresh.Load(pkg);
            c.Check(fresh.m_skillData.TryGetValue(type, out var loaded) && loaded.m_level == 100f && ReferenceEquals(loaded.m_info, SailingSkill.Def),
                "turned off: a character loaded now loses its Sailing level");

            // ----- on again -----
            var on = LogMark();
            SetBlocked(false);
            ServerRules.TestRules = rules;
            c.Check(feature.IsActive, $"turned on again: the mod is {feature.State} '{feature.Status}'");
            c.Check(LoggedExact(on, LogLevel.Debug, "Published Sailing level 100 for the games that simulate ships.")
                    && Near(HelmSkill.Published(player), 100f),
                $"turned on again: Sailing 100 not published (now {F(HelmSkill.Published(player))})");
            ShipFixedUpdatePatches.LastShip = null;
            yield return FixedSteps(3);
            c.Check(OwnShipPrefixes() == 1 && ReferenceEquals(ShipFixedUpdatePatches.LastShip, ship)
                    && Near(ShipFixedUpdatePatches.LastAccel, accel) && Near(ShipFixedUpdatePatches.LastTurn, turn),
                $"turned on again: acceleration x{F(ShipFixedUpdatePatches.LastAccel)}, turning x{F(ShipFixedUpdatePatches.LastTurn)}");
            c.Check(Near(RudderStep(ship), vanillaStep * rudder, 1e-5f), "turned on again: the rudder does not swing faster");
            if (map != null)
            {
                var r = RevealReach(player);
                c.Check(r.Ok && r.Reach == ReachOf(map, wide) && Near(r.Used, wide, 0.01f),
                    $"turned on again: map revealed {F(r.Reach * map.m_pixelSize)} m, expected {F(ReachOf(map, wide) * map.m_pixelSize)} m");
            }
            if (rig.Wear != null)
            {
                var taken = DamageTaken(rig, HitData.HitType.EnemyHit, null);
                c.Check(Near(taken, full * cut, 0.01f), $"turned on again: the ship took {F(taken)}, expected {F(full * cut)}");
            }
            sailing = SetSailing(player, 50f, 0f);
            var back = new Gain();
            yield return SampleXp(ship, sailing, 30f, back);
            c.Check(back.Sampled && back.Xp > 0f, "turned on again: no Sailing XP at the helm");
            MoveShip(ship, home);
            sailing = SetSailing(player, 100f, 0f);

            // ----- many times: nothing piles up -----
            for (var i = 0; i < 5; i++)
            {
                SetBlocked(true);
                yield return FixedSteps(2);
                SetBlocked(false);
                ServerRules.TestRules = rules;
                yield return FixedSteps(2);
            }
            like = FieldsLikePrefab(ship, rig.PrefabShip, out fields);
            c.Check(feature.IsActive && OwnShipPrefixes() == 1 && like
                    && Near(ShipFixedUpdatePatches.LastAccel, accel) && Near(ShipFixedUpdatePatches.LastTurn, turn),
                $"after 5 more toggles: {OwnShipPrefixes()} patch(es) on the ship step, acceleration x{F(ShipFixedUpdatePatches.LastAccel)}, "
                + $"turning x{F(ShipFixedUpdatePatches.LastTurn)}; fields: {fields}");
            c.Check(Near(RudderStep(ship), vanillaStep * rudder, 1e-5f) && ship.m_rudderSpeed == rig.PrefabShip.m_rudderSpeed
                    && (map == null || map.m_exploreRadius == radius),
                "after 5 more toggles: the rudder or the map reveal piled up");

            // ----- turned on mid-voyage: helm taken while the mod is off -----
            rig.ReleaseHelm();
            SetBlocked(true);
            c.Check(rig.TakeHelm(), "could not take the helm while the mod is off");
            sailing = SetSailing(player, 50f, 0f);
            // Two frames before the move: an XP watch still running while off has taken the ship's place by now, so the
            // 30 m below would pay (moved in the same frame, a watch that just started would see no distance).
            yield return null;
            yield return null;
            MoveShip(ship, home + Vector3.right * 30f);
            yield return new WaitForSeconds(1.3f);
            ShipFixedUpdatePatches.LastShip = null;
            yield return FixedSteps(2);
            c.Check(sailing.m_accumulator == 0f && ShipFixedUpdatePatches.LastShip == null,
                "helm taken while off: Sailing XP or handling bonuses anyway");
            sailing = SetSailing(player, 100f, 0f);
            var late = LogMark();
            var switched = Time.time;
            SetBlocked(false);
            ServerRules.TestRules = rules;
            ShipFixedUpdatePatches.LastShip = null;
            yield return FixedSteps(3);
            c.Check(ReferenceEquals(ShipFixedUpdatePatches.LastShip, ship) && Near(ShipFixedUpdatePatches.LastAccel, accel)
                    && Near(ShipFixedUpdatePatches.LastTurn, turn),
                "turned on mid-voyage: the handling did not change within three physics steps");
            var helmLine = $"At the helm of {shipName}: earning Sailing XP ({perKm} per km).";
            var seen = new Box();
            yield return WaitFor(() => LoggedExact(late, LogLevel.Debug, helmLine), 1.2f, seen);
            c.Check(seen.Ok && Time.time - switched <= 1.3f,
                $"turned on mid-voyage: no Debug line '{helmLine}' within a second (lines: {Join(MyLines(late, LogLevel.Debug))})");
            sailing = SetSailing(player, 50f, 0f);
            var voyage = new Gain();
            yield return SampleXp(ship, sailing, -30f, voyage, restart: false);
            c.Check(voyage.Sampled && voyage.Xp > 0f, "turned on mid-voyage: no Sailing XP at the helm");

            rig.RestoreSkills();
            yield return rig.Leave(rig.Origin);
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            if (Plugin.TestBlocked)
            {
                Plugin.TestBlocked = false;
                try
                {
                    FeatureRegistry.RefreshAll();
                }
                catch (Exception e)
                {
                    Log.Error($"Sailing self test clean-up failed: {e}");
                }
            }
            if (fresh != null)
            {
                DestroyNow(fresh.gameObject);
            }
            if (InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                ClosePanel();
            }
            rig.Restore();
        }
    }

    // ---------- sailing.cleanlog ----------

    // Test list L03. Watcher listen from plugin start: what this mod logged as warning or error in whole session so
    // far (every sailing test before this one too), whatever the log file level.
    private static IEnumerator RunCleanLog()
    {
        var c = new Checks(CleanLogName);
        if (c.Check(_watch != null, "the log watcher is not running"))
        {
            var count = _watch.MyProblemCount;
            c.Check(count == 0, $"{count} warning(s) or error(s) from {ModInfo.Name} since the game started: {Join(_watch.MyProblems(), 5)}");
            c.Note($"{MyLines(0, LogLevel.Debug).Count} Debug and {MyLines(0, LogLevel.Info).Count} Info lines of {ModInfo.Name} seen (latest part of the session)");
        }
        c.Report();
        yield break;
    }
}
#endif
