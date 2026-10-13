#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.SailingSkillMod;

// Debug build only. Me = the real thing in place of a test shortcut (same class as SelfTests.cs).
//   sailing.real.serpent  TESTING.md T09 / M05: a real Serpent with its AI on, told to hunt players (the state raid
//                         creatures have), next to a Karve at sea at night; the player (Sailing 100, real skill) at
//                         the helm. Its own bite reaches the ship through the game's own damage road: the Debug line
//                         of the ship's owner says "<before> -> <half> ... (best Sailing aboard 100)" and the ship lost
//                         health. sailing.damage sends a made-up hit instead.
//                         The Serpent's mood decides when it bites: no bite on the ship within 45 s = FAIL (never a
//                         silent pass), run again.
internal static partial class SelfTests
{
    private const string RealSerpentName = "sailing.real.serpent";

    // Not in .ref (prefab data). Name from the 1.0.16 runtime dump (Swim Dive's TESTING.md, "Names checked").
    private const string SerpentPrefab = "Serpent";

    // "<ship> hit (EnemyHit): <before> -> <after> damage before resistances (best Sailing aboard <level>)."
    private static bool ParseHitLine(string line, string head, out float before, out float after, out string best)
    {
        before = 0f;
        after = 0f;
        best = "";
        const string arrow = " -> ";
        const string middle = " damage before resistances (best Sailing aboard ";
        if (line == null || !line.StartsWith(head, StringComparison.Ordinal))
        {
            return false;
        }
        var rest = line.Substring(head.Length);
        var a = rest.IndexOf(arrow, StringComparison.Ordinal);
        var m = rest.IndexOf(middle, StringComparison.Ordinal);
        var end = rest.LastIndexOf(").", StringComparison.Ordinal);
        if (a <= 0 || m <= a || end <= m)
        {
            return false;
        }
        best = rest.Substring(m + middle.Length, end - m - middle.Length);
        return float.TryParse(rest.Substring(0, a), NumberStyles.Float, CultureInfo.InvariantCulture, out before)
               && float.TryParse(rest.Substring(a + arrow.Length, m - a - arrow.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out after);
    }

    private static IEnumerator RunRealSerpent()
    {
        var c = new Checks(RealSerpentName);
        var rig = ShipRig.Create(RealSerpentName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        var sea = new Sea { Rig = rig };
        var started = Time.realtimeSinceStartup;
        var player = rig.Player;
        var env = EnvMan.instance;
        var timeForced = env != null && env.m_debugTimeOfDay;
        var timeWas = env != null ? env.m_debugTime : 0.5f;
        var health = player.GetHealth();
        var seman = player.GetSEMan();
        var wasWet = seman != null && seman.HaveStatusEffect(SEMan.s_statusEffectWet);
        GameObject serpentGo = null;
        try
        {
            var prefab = ZNetScene.instance.GetPrefab(SerpentPrefab);
            if (!c.Check(prefab != null && prefab.GetComponent<Character>() != null && prefab.GetComponent<MonsterAI>() != null,
                    $"the game has no creature '{SerpentPrefab}' with a MonsterAI: nothing checked"))
            {
                c.Report();
                yield break;
            }
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            yield return SetUpSea(sea, c, 0f, 0.3f, 90f);
            if (!sea.Ok || !c.Check(rig.Wear != null && rig.Wear.m_nview != null && env != null, "the ship has no WearNTear, or there is no weather (EnvMan): nothing checked"))
            {
                yield return LeaveSea(sea, c);
                c.Report();
                yield break;
            }
            var ship = rig.Boat;
            var wear = rig.Wear;
            var zdo = wear.m_nview.GetZDO();
            var shipName = Utils.GetPrefabName(ship.gameObject);
            var cut = SailMath.DamageFactor(rules.DamageReductionAtMax, 1f);

            // Real skill, no test level: Sailing 100 on the character at the helm (he cannot be knocked off it).
            HelmSkill.TestLocalLevel = null;
            rig.BackupSkills();
            SetSailing(player, 100f, 0f);
            c.Check(cut < 0.99f && Near(HelmSkill.Damage01(ship), 1f, 1e-3f) && ship.IsOwner(),
                $"best Sailing aboard reads {F(HelmSkill.Damage01(ship) * 100f)} (expected 100), damage factor {F(cut)}, ship simulated here {ship.IsOwner()}");

            // Night (sea serpents leave by day), ship whole, then the Serpent 14 m off the beam, told to hunt players.
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0f;
            zdo.Set(ZDOVars.s_health, wear.m_health);
            var water = ZoneSystem.instance.m_waterLevel;
            var side = Flat(ship.transform.right).normalized;
            var at = ship.transform.position + side * 14f;
            var surface = Floating.GetLiquidLevel(new Vector3(at.x, water - 0.5f, at.z));
            at.y = (surface > water - 5f ? surface : water) - 0.5f;
            var mark = LogMark();
            serpentGo = Object.Instantiate(prefab, at, Quaternion.LookRotation(-side, Vector3.up));
            var serpent = serpentGo.GetComponent<Character>();
            var ai = serpentGo.GetComponent<MonsterAI>();
            // What the game sets on raid creatures (BaseAI.SetHuntPlayer). The AI itself picks target and attack.
            ai.SetHuntPlayer(true);

            var head = shipName + " hit (" + HitData.HitType.EnemyHit + "): ";
            string line = null;
            var targeted = false;
            var attacks = 0;
            var inAttack = false;
            var nearest = float.MaxValue;
            var t0 = Time.time;
            while (Time.time - t0 < 45f && line == null)
            {
                yield return new WaitForFixedUpdate();
                if (serpentGo == null || serpent == null || ai == null || ship == null)
                {
                    break;
                }
                targeted |= ReferenceEquals(ai.m_targetCreature, player) || ai.m_targetStatic != null;
                var attacking = serpent.InAttack();
                if (attacking && !inAttack)
                {
                    attacks++;
                }
                inAttack = attacking;
                nearest = Mathf.Min(nearest, Flat(serpent.transform.position - ship.transform.position).magnitude);
                line = MyLines(mark, LogLevel.Debug).FirstOrDefault(l => l.StartsWith(head, StringComparison.Ordinal));
            }
            var waited = Time.time - t0;
            if (c.Check(line != null,
                    $"a hunting Serpent did not bite the ship within {F(waited)} s (Serpent still there {serpentGo != null}, it took a target {targeted}, "
                    + $"attacks started {attacks}, nearest {F(nearest)} m from the ship, player still at the helm {ship != null && ReferenceEquals(player.GetControlledShip(), ship)}): "
                    + "nothing of the damage was checked. Its mood decides: run the test again"))
            {
                var parsed = ParseHitLine(line, head, out var before, out var after, out var best);
                c.Note($"after {F(waited)} s: {line}");
                c.Check(parsed && best == "100", $"the ship owner's Debug line does not say 'best Sailing aboard 100': {line}");
                // Both numbers are printed with one decimal.
                c.Check(parsed && before > 0f && Mathf.Abs(after - before * cut) <= 0.11f,
                    $"a real Serpent bite at Sailing 100: {F(before)} -> {F(after)} damage before resistances, expected {F(before * cut)} (x{F(cut)})");
                c.Check(attacks > 0, "the ship was hit as by a creature, but the Serpent was never seen attacking: the hit may not be its bite");
                var left = zdo.GetFloat(ZDOVars.s_health, wear.m_health);
                c.Check(left < wear.m_health, $"the Serpent's bite took no health off the ship ({F(wear.m_health)} -> {F(left)})");
            }

            // Serpent gone before the trip home (destroyed, not killed: no drops).
            if (serpentGo != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(serpentGo);
            }
            serpentGo = null;
            env.m_debugTimeOfDay = timeForced;
            env.m_debugTime = timeWas;
            rig.RestoreSkills();
            yield return LeaveSea(sea, c);
            c.Note($"took {F(Time.realtimeSinceStartup - started)} s");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            try
            {
                if (serpentGo != null && ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(serpentGo);
                }
                if (env != null)
                {
                    env.m_debugTimeOfDay = timeForced;
                    env.m_debugTime = timeWas;
                }
                // God mode keeps the player alive, a bite can still cost health.
                if (player.GetHealth() < health)
                {
                    player.SetHealth(health);
                }
                if (!wasWet && seman != null)
                {
                    seman.RemoveStatusEffect(SEMan.s_statusEffectWet, true);
                }
            }
            catch (Exception e)
            {
                Log.Error($"Sailing self test clean-up failed: {e}");
            }
            sea.Wind.Restore();
            rig.Restore();
        }
    }
}
#endif
