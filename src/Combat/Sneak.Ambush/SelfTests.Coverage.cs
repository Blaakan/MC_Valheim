#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.SneakAmbushMod;

// Debug build only (whole file). More in-world self tests, one per group of items of TESTING.md that the first twelve
// tests left to a human:
//   sneak.attack        T01 real KnifeFlint swing on unaware Greydwarf at Sneak 0 (backstab, effect, messages, level
//                       0 -> 1, Debug lines); T02 real bare-hands sneak attack, chase, smoke give-up, second hit pay nothing
//   sneak.xp-sources    T03 real arrow against real knife at Sneak 5 (half), T04 Troll 33 and Boar 4, T05 tame (console
//                       tame path) and dummy pay and say nothing, X08 real bolt, X10 stand-down (hook)
//   sneak.x-harpoon     X05 harpoon hook on tamed Boar pay nothing (need Harpoon Hooks Tames active, else FAIL)
//   sneak.still-notice  T06 bar value at noon Sneak 0 and 100, T07 icon after ~1 s, bar to a quarter, camera turn,
//                       creature 9 m in front not notice (and notice without the bonus)
//   sneak.still-ends    T08 step, stand, roll, hit, moving Karve, bow draw (noted); X02 raised shield
//   sneak.foliage       T09 icon at RaspberryBush and Bush01, tooltip in Active effects text, tree crown, icon linger
//   sneak.bush-sight    T10 creature behind bush not see, beside bush see
//   sneak.fog           T11 Misty night -30%, noon -4%, Clear none, standing none, swamp weathers 4-9%, interior none;
//                       T12 In the mist icon (mist input forced)
//   sneak.recipe        T13 recipe appear after three materials, real craft at workbench, tooltip; T30 odd recipe
//                       settings
// Smoke, death and real off/on tests: SelfTests.Smoke.cs. Dedicated server tests: SelfTests.Multiplayer.cs.
// Tests that need full sun, or more room than the start spot of the test world give (standing stones, trees), choose
// their ground by checks: FindOpenSpot (vanilla's own shade rays, free lanes, what the test itself ask).
// Me listen to log lines of this mod and HUD messages with Tap (test-only listener and Harmony prefixes, own id), force
// "other sneak-XP mod" with Compat.TestOtherModPays: never config.
internal static partial class SelfTests
{
    private const string AttackName = "sneak.attack";
    private const string XpSourcesName = "sneak.xp-sources";
    private const string HarpoonName = "sneak.x-harpoon";
    private const string StillNoticeName = "sneak.still-notice";
    private const string StillEndsName = "sneak.still-ends";
    private const string FoliageName = "sneak.foliage";
    private const string BushSightName = "sneak.bush-sight";
    private const string FogName = "sneak.fog";
    private const string RecipeName = "sneak.recipe";

    private const string HarpoonGuid = "MC.Farming.Harpoon.HooksTames";
    private const string ForgeGuid = "MC.Crafting.Forge.IdolUpgrades";

    private static void RegisterCoverage()
    {
        SelfTest.Register(AttackName, RunAttack);
        SelfTest.Register(XpSourcesName, RunXpSources);
        SelfTest.Register(HarpoonName, RunHarpoon);
        SelfTest.Register(StillNoticeName, RunStillNotice);
        SelfTest.Register(StillEndsName, RunStillEnds);
        SelfTest.Register(FoliageName, RunFoliage);
        SelfTest.Register(BushSightName, RunBushSight);
        SelfTest.Register(FogName, RunFog);
        SelfTest.Register(RecipeName, RunRecipe);
        RegisterSmoke();
    }

    private static void UnregisterCoverage()
    {
        SelfTest.Unregister(AttackName);
        SelfTest.Unregister(XpSourcesName);
        SelfTest.Unregister(HarpoonName);
        SelfTest.Unregister(StillNoticeName);
        SelfTest.Unregister(StillEndsName);
        SelfTest.Unregister(FoliageName);
        SelfTest.Unregister(BushSightName);
        SelfTest.Unregister(FogName);
        SelfTest.Unregister(RecipeName);
        UnregisterSmoke();
    }

    // ---------- ears: log lines and HUD messages ----------

    // Me = what tests hear. BepInEx hand every log line (Debug too, whatever the log file keep) to every listener: me
    // keep lines of this mod. HUD messages: test-only prefix on MessageHud.ShowMessage (own Harmony id, never a patch
    // class, so framework patching never see it). Also forced "in the mist" input for the mist icon. Installed by a
    // test at first use, removed by Rig.Restore and when feature go off.
    private static class Tap
    {
        private sealed class Listener : ILogListener
        {
            public void LogEvent(object sender, LogEventArgs eventArgs)
            {
                try
                {
                    if (eventArgs == null || eventArgs.Source == null || eventArgs.Source.SourceName != ModInfo.Name)
                    {
                        return;
                    }
                    var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                    if (text == null)
                    {
                        return;
                    }
                    lock (Gate)
                    {
                        Lines.Add(new KeyValuePair<LogLevel, string>(eventArgs.Level, text));
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

        private static readonly object Gate = new object();
        private static readonly List<KeyValuePair<LogLevel, string>> Lines = new List<KeyValuePair<LogLevel, string>>();
        private static readonly List<KeyValuePair<MessageHud.MessageType, string>> Shown =
            new List<KeyValuePair<MessageHud.MessageType, string>>();
        private static Listener _listener;
        private static Harmony _harmony;

        // ParticleMist.IsInMist say true (the mod's only mist input). Rig.Restore clear it.
        internal static bool ForceMist;

        // Test that turn the feature off and on itself: feature going off must not take the ears away (lines logged
        // during the switch are what the test want). Release clear it.
        internal static bool Hold;

        // On (idempotent) and empty. hooks false = log lines only (dedicated server: no HUD to listen to).
        internal static void Install(bool hooks = true)
        {
            if (_listener == null)
            {
                _listener = new Listener();
                BepInEx.Logging.Logger.Listeners.Add(_listener);
            }
            if (hooks && _harmony == null)
            {
                var harmony = new Harmony(ModInfo.Guid + ".selftest");
                try
                {
                    harmony.Patch(AccessTools.Method(typeof(MessageHud), nameof(MessageHud.ShowMessage)),
                        prefix: new HarmonyMethod(typeof(Tap), nameof(ShowMessagePrefix)));
                    harmony.Patch(AccessTools.Method(typeof(ParticleMist), nameof(ParticleMist.IsInMist), new[] { typeof(Vector3) }),
                        prefix: new HarmonyMethod(typeof(Tap), nameof(IsInMistPrefix)));
                }
                catch
                {
                    harmony.UnpatchSelf();
                    throw;
                }
                _harmony = harmony;
            }
            Clear();
        }

        // End of a test (Rig.Restore): off, whatever a test asked to hold.
        internal static void Release()
        {
            Hold = false;
            Remove();
        }

        internal static void Remove()
        {
            ForceMist = false;
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
            if (_listener != null)
            {
                BepInEx.Logging.Logger.Listeners.Remove(_listener);
                _listener = null;
            }
            Clear();
        }

        internal static void Clear()
        {
            lock (Gate)
            {
                Lines.Clear();
            }
            Shown.Clear();
        }

        // Log lines of this mod holding that text.
        internal static int Logged(string part)
        {
            lock (Gate)
            {
                return Lines.Count(l => l.Value.Contains(part));
            }
        }

        internal static int Logged(LogLevel level, string part)
        {
            lock (Gate)
            {
                return Lines.Count(l => (l.Key & level) != 0 && l.Value.Contains(part));
            }
        }

        internal static string FirstLogged(string part)
        {
            lock (Gate)
            {
                return Lines.Where(l => l.Value.Contains(part)).Select(l => l.Value).FirstOrDefault() ?? "";
            }
        }

        // Warning and error lines of this mod that are no self test line ("" = clean).
        internal static string Problems()
        {
            lock (Gate)
            {
                return string.Join(" | ", Lines
                    .Where(l => (l.Key & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0
                                && !l.Value.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                    .Select(l => $"[{l.Key}] {l.Value}").ToArray());
            }
        }

        // HUD messages holding that text (text as the caller gave it, before translation).
        internal static int Messages(string part, MessageHud.MessageType? type = null) =>
            Shown.Count(m => (!type.HasValue || m.Key == type.Value) && m.Value.Contains(part));

        internal static string MessageTexts(string part) =>
            string.Join(" | ", Shown.Where(m => m.Value.Contains(part)).Select(m => $"{m.Key}: {m.Value}").ToArray());

        private static void ShowMessagePrefix(MessageHud.MessageType type, string text)
        {
            if (text != null)
            {
                Shown.Add(new KeyValuePair<MessageHud.MessageType, string>(type, text));
            }
        }

        private static bool IsInMistPrefix(ref bool __result)
        {
            if (!ForceMist)
            {
                return true;
            }
            __result = true;
            return false;
        }
    }

    // ---------- helpers ----------

    // Last check of a test that listened: this mod logged no warning and no error meanwhile.
    private static void NoProblems(Checks c)
    {
        var problems = Tap.Problems();
        c.Check(problems.Length == 0, $"{ModInfo.Name} logged a warning or an error during the test: {problems}");
    }

    private static IEnumerator WaitFor(Func<bool> done, float seconds, Box result = null)
    {
        var until = Time.time + seconds;
        while (Time.time < until && !done())
        {
            yield return null;
        }
        if (result != null)
        {
            result.Ok = done();
        }
    }

    private static bool ModActive(string guid)
    {
        var view = FeatureRegistry.Find(guid);
        return view != null && view.Value.IsActive;
    }

    private static ItemDrop.ItemData.SharedData Shared(string prefabName)
    {
        var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        return drop != null ? drop.m_itemData.m_shared : null;
    }

    // Body and look that way (melee swing go where the body face).
    private static void Face(Rig rig, Vector3 dir)
    {
        var player = rig.Player;
        var rotation = Quaternion.LookRotation(Flat(dir));
        player.transform.rotation = rotation;
        if (player.m_body != null)
        {
            player.m_body.rotation = rotation;
        }
        rig.Aim(dir, 0f);
    }

    // Player right behind the creature (it look away), crouched, facing its back. Ok = crouched.
    private static IEnumerator GetBehind(Rig rig, Character victim, Box result)
    {
        var player = rig.Player;
        var facing = Flat(victim.transform.forward);
        var reach = player.GetRadius() + victim.GetRadius() + 0.3f;
        var spot = victim.transform.position - facing * reach;
        if (ZoneSystem.instance.GetSolidHeight(spot, out var solid, 2))
        {
            spot.y = Mathf.Max(spot.y, solid);
        }
        MovePlayer(player, spot + Vector3.up * 0.05f);
        rig.MarkMoved();
        Face(rig, facing);
        yield return Crouch(player, result);
        // Body settled on the ground (attack ask for it).
        yield return new WaitForSeconds(0.4f);
        Face(rig, facing);
    }

    // Real attack with what the player hold: vanilla StartAttack, animation event do the hit. Ok = victim lost health.
    private static IEnumerator Swing(Rig rig, Character victim, Box result, float seconds = 3f)
    {
        var player = rig.Player;
        result.Ok = false;
        result.Detail = "";
        var wait = Time.time + 2f;
        while (Time.time < wait && player.InAttack())
        {
            yield return null;
        }
        Face(rig, victim.transform.position - player.transform.position);
        var health = victim.GetHealth();
        if (!player.StartAttack(null, false))
        {
            result.Detail = "StartAttack refused";
            yield break;
        }
        var until = Time.time + seconds;
        while (Time.time < until)
        {
            yield return null;
            if (victim == null || victim.IsDead() || victim.GetHealth() < health - 0.001f)
            {
                result.Ok = true;
                break;
            }
        }
        if (!result.Ok)
        {
            result.Detail = $"no hit within {F(seconds)} s (victim {F(Vector3.Distance(victim.transform.position, player.transform.position))} m away)";
        }
    }

    // A shot as vanilla build it (Attack.FireProjectileBurst): projectile of the ammo (or of the weapon), weapon's
    // backstab bonus, skill and status effect, attacker = player. Damage chosen by the test (so it choose if it kill),
    // no skill raise. Projectile set m_ranged itself. Ok = victim lost health (or what "landed" say, for hits that
    // do no damage).
    private static IEnumerator Shoot(Rig rig, string weaponName, string ammoName, Character victim, float pierce, float noise,
        Box result, Func<bool> landed = null)
    {
        result.Ok = false;
        result.Detail = "";
        var weapon = Shared(weaponName);
        var ammo = ammoName != null ? Shared(ammoName) : null;
        GameObject prefab = null;
        if (ammo != null && ammo.m_attack != null && ammo.m_attack.m_attackProjectile != null)
        {
            prefab = ammo.m_attack.m_attackProjectile;
        }
        else if (weapon != null && weapon.m_attack != null && weapon.m_attack.m_attackProjectile != null)
        {
            prefab = weapon.m_attack.m_attackProjectile;
        }
        else if (weapon != null && weapon.m_secondaryAttack != null && weapon.m_secondaryAttack.m_attackProjectile != null)
        {
            // Thrown weapon whose throw is the second attack.
            prefab = weapon.m_secondaryAttack.m_attackProjectile;
        }
        if (weapon == null || prefab == null || (ammoName != null && ammo == null))
        {
            result.Detail = $"{weaponName} / {ammoName ?? "no ammo"}: item or projectile not found";
            yield break;
        }
        var player = rig.Player;
        var target = victim.GetCenterPoint();
        var start = player.m_eye.position;
        var dir = (target - start).normalized;
        start += dir * 0.7f;
        var go = Object.Instantiate(prefab, start, Quaternion.LookRotation(dir));
        rig.Spawned.Add(go);
        var hit = new HitData
        {
            m_backstabBonus = weapon.m_backstabBonus,
            m_skill = weapon.m_skillType,
            m_skillRaiseAmount = 0f,
            m_blockable = true,
            m_dodgeable = true,
            m_hitType = HitData.HitType.PlayerHit,
            m_statusEffectHash = weapon.m_attackStatusEffect != null ? weapon.m_attackStatusEffect.NameHash() : 0,
        };
        hit.m_damage.m_pierce = pierce;
        hit.SetAttacker(player);
        var projectile = go.GetComponent<IProjectile>();
        if (projectile == null)
        {
            result.Detail = $"{prefab.name} is no projectile";
            yield break;
        }
        projectile.Setup(player, dir * 60f, noise, hit, null, null);
        var health = victim.GetHealth();
        // What the shot meet on its way, seen now (victim may walk after): said when the shot fail, so the log name
        // the thing in the way (round 2: sneak.toggle arrow never reached a Greydwarf placed blind, log said nothing).
        var inTheWay = ShotBlocker(player, target, victim);
        var distance = Vector3.Distance(start, target);
        var until = Time.time + 2.5f;
        while (Time.time < until)
        {
            yield return null;
            if (landed != null ? landed() : victim == null || victim.IsDead() || victim.GetHealth() < health - 0.001f)
            {
                result.Ok = true;
                break;
            }
        }
        if (!result.Ok)
        {
            result.Detail = $"the {prefab.name} did not hurt its target within 2.5 s; target {F(distance)} m from the shot's start, "
                            + $"on the shot's path: {inTheWay ?? "nothing but the target"}";
        }
    }

    // What stand in the way of a shot from the player's eye to that point: first thing the game's own projectile ray
    // (Projectile.s_rayMaskSolids: solids, pieces, characters) meet that is neither the shooter nor the victim. Null =
    // free path (or victim reached first). Ray go FROM the shooter like the projectile: a creature standing inside a
    // rock see out of it (its sight ray leave the rock from inside, no hit), but nothing coming in reach it.
    private static string ShotBlocker(Player player, Vector3 target, Character victim)
    {
        var mask = Projectile.s_rayMaskSolids != 0
            ? Projectile.s_rayMaskSolids
            : LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character",
                "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
        var from = player.m_eye.position;
        var to = target - from;
        if (to.sqrMagnitude < 1e-4f)
        {
            return null;
        }
        var hits = Physics.RaycastAll(from, to.normalized, to.magnitude, mask);
        Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var hit in hits)
        {
            var collider = hit.collider;
            if (collider == null)
            {
                continue;
            }
            var owner = collider.GetComponentInParent<Character>();
            if (owner != null && ReferenceEquals(owner, player))
            {
                continue;
            }
            if (owner != null && victim != null && ReferenceEquals(owner, victim))
            {
                return null;
            }
            var root = collider.transform.root != null ? collider.transform.root.name : collider.name;
            return $"{collider.name} of {root} (layer {LayerMask.LayerToName(collider.gameObject.layer)}) {F(hit.distance)} m from the eye";
        }
        return null;
    }

    // Names the game give to spawned copies of an effect list's prefabs.
    private static HashSet<string> CloneNames(EffectList list)
    {
        var names = new HashSet<string>();
        if (list != null && list.m_effectPrefabs != null)
        {
            foreach (var effect in list.m_effectPrefabs)
            {
                if (effect != null && effect.m_enabled && effect.m_prefab != null)
                {
                    names.Add(effect.m_prefab.name + "(Clone)");
                }
            }
        }
        return names;
    }

    // Live objects with one of these names (slow: whole scene; a test call it twice).
    private static int CountNamed(HashSet<string> names)
    {
        if (names.Count == 0)
        {
            return 0;
        }
        var n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (names.Contains(t.name))
            {
                n++;
            }
        }
        return n;
    }

    // Crouched, not moving, until the Holding still bonus and icon are on. Ok = both on.
    private static IEnumerator HoldStill(Rig rig, Box result, float seconds = 4f)
    {
        var player = rig.Player;
        rig.Drive(Vector3.zero);
        player.SetCrouch(true);
        var until = Time.time + seconds;
        while (Time.time < until && !(StealthState.StillActive && StealthCues.Has(player, CueKind.Still)))
        {
            yield return null;
        }
        result.Ok = StealthState.StillActive && StealthCues.Has(player, CueKind.Still);
    }

    // After something that should end Holding still: bonus off and icon gone within the time. Ok = both.
    private static IEnumerator StillGone(Rig rig, Box result, float seconds = 1.5f)
    {
        var player = rig.Player;
        var start = Time.time;
        var until = start + seconds;
        while (Time.time < until && (StealthState.StillActive || StealthCues.Has(player, CueKind.Still)))
        {
            yield return new WaitForFixedUpdate();
        }
        result.Ok = !StealthState.StillActive && !StealthCues.Has(player, CueKind.Still);
        result.Detail = F(Time.time - start);
    }

    // Stealth factor (the bar) arrived at its target. Ok = arrived.
    private static IEnumerator Settle(Player player, Box result, float seconds = 6f)
    {
        yield return ForceRefresh(player);
        var until = Time.time + seconds;
        while (Time.time < until && Mathf.Abs(player.m_stealthFactor - player.m_stealthFactorTarget) > 0.004f)
        {
            yield return new WaitForFixedUpdate();
        }
        result.Ok = Mathf.Abs(player.m_stealthFactor - player.m_stealthFactorTarget) <= 0.004f;
    }

    // Default rules for XP tests. Another sneak-XP mod on this game: said, and our stand-down switched off for the
    // test (it would pay too, so sums could differ: test say so).
    private static AmbushRules XpRules(Checks c)
    {
        if (Compat.SecondaryAttacks || Compat.SmartSkills)
        {
            c.Note("SecondaryAttacks or SmartSkills is installed: it may pay Sneak XP too, sums in this test can be off");
        }
        Compat.TestOtherModPays = false;
        return new AmbushRules();
    }

    // ---------- open ground ----------

    // Where a test stand: feet on the terrain, direction with a free lane. Found false = nothing good near.
    private sealed class Spot
    {
        internal bool Found;
        internal Vector3 Feet;
        internal Vector3 Dir;
    }

    // Free lane from that spot: nothing solid for that distance (body and head height), ground along it about level
    // with the spot and dry. Same rule as ClearDirection, from any spot.
    private static bool Lane(Vector3 feet, Vector3 dir, float distance)
    {
        var mask = ViewMask;
        var center = feet + Vector3.up * 0.9f;
        if (Physics.Raycast(center, dir, distance, mask) || Physics.Raycast(center + Vector3.up * 0.8f, dir, distance, mask))
        {
            return false;
        }
        var water = ZoneSystem.instance.m_waterLevel;
        for (var k = 1; k <= 4; k++)
        {
            if (!ZoneSystem.instance.GetGroundHeight(feet + dir * (distance * k / 4f), out var ground)
                || Mathf.Abs(ground - feet.y) > 1.5f || ground < water + 0.3f)
            {
                return false;
            }
        }
        return true;
    }

    // Bare terrain at that ground point: a ray from 40 m up come down to the terrain itself (no rock, piece or roof on
    // the way; openSky = no tree crown or bush either).
    private static bool BareGround(Vector3 ground, bool openSky)
    {
        var mask = ViewMask;
        if (openSky)
        {
            if (StealthSystem.instance != null)
            {
                mask |= StealthSystem.instance.m_shadowTestMask.value;
            }
        }
        else
        {
            mask &= ~ViewblockLayerMask;
        }
        // Not inside a bush either (a sight ray that start inside a bush's sphere does not see it, one coming in does).
        return Physics.Raycast(ground + Vector3.up * 40f, Vector3.down, out var hit, 41f, mask)
               && hit.collider != null && hit.collider.gameObject.layer == LayerMask.NameToLayer("terrain")
               && Mathf.Abs(hit.point.y - ground.y) < 0.4f
               && !Physics.CheckSphere(ground + Vector3.up * 0.9f, 1f, ViewblockLayerMask);
    }

    // Sun (or moon) reach that point now: the two rays of vanilla's own shade test (StealthSystem.GetLightLevel) hit
    // nothing. Sun direction = EnvMan's light as the weather and hour in force left it.
    private static bool NoShade(Vector3 point)
    {
        var stealth = StealthSystem.instance;
        var sun = EnvMan.instance != null ? EnvMan.instance.m_dirLight : null;
        if (stealth == null || sun == null)
        {
            return false;
        }
        var forward = sun.transform.forward;
        var mask = stealth.m_shadowTestMask;
        return !Physics.Raycast(point - forward * 1000f, forward, 1000f, mask) && !Physics.Raycast(point, -forward, 1000f, mask);
    }

    // Sun as vanilla's light rule read it (for the log, when a light value surprise).
    private static string SunText()
    {
        var sun = EnvMan.instance != null ? EnvMan.instance.m_dirLight : null;
        if (sun == null)
        {
            return "no sun light";
        }
        return $"sun {F(sun.intensity)} x grey {F(sun.color.grayscale)}, shadows {sun.shadows} (strength {F(sun.shadowStrength)}), "
               + $"{F(Vector3.Angle(-sun.transform.forward, Vector3.up))} degrees from straight up, ambient "
               + $"{F(RenderSettings.ambientIntensity * RenderSettings.ambientLight.grayscale)}";
    }

    // No shade where a crouched or standing body is, and half a metre around (no shadow edge under the player).
    private static bool Sunlit(Vector3 ground)
    {
        var body = ground + Vector3.up * 0.93f;
        return NoShade(body) && NoShade(ground + Vector3.up * 0.4f) && NoShade(ground + Vector3.up * 1.5f)
               && NoShade(body + Vector3.right * 0.5f) && NoShade(body - Vector3.right * 0.5f)
               && NoShade(body + Vector3.forward * 0.5f) && NoShade(body - Vector3.forward * 0.5f);
    }

    // Open ground for a test, chosen by checks (the start spot of the test world lie between standing stones and
    // trees): spot within 48 m of the player (his own spot first) and a direction from it, where
    //   - the ground is bare terrain and dry; sun true = nothing above it and no shade there now (weather and hour of
    //     the test must be in place before the call),
    //   - a lane of that length is free (creatures, bushes and trees of the test go there),
    //   - more(feet, dir) say yes (what only that test need; null = nothing more).
    // Player moved there, standing (Rig put him back at the end). Found false = player stay, Dir = ClearDirection.
    private static IEnumerator FindOpenSpot(Rig rig, float lane, bool sun, Func<Vector3, Vector3, bool> more, Spot spot)
    {
        var player = rig.Player;
        var home = player.transform.position;
        var cam = Utils.GetMainCamera();
        var start = Flat(cam != null ? cam.transform.forward : player.transform.forward);
        var zones = ZoneSystem.instance;
        var water = zones.m_waterLevel;
        spot.Found = false;
        // The game turn its sun in its physics step (EnvMan.FixedUpdate): two of them, so the shade rays below use
        // the hour and weather the test just forced.
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        for (var ring = 0; ring <= 6 && !spot.Found; ring++)
        {
            var steps = ring == 0 ? 1 : 12;
            for (var a = 0; a < steps && !spot.Found; a++)
            {
                var p = home + Quaternion.Euler(0f, a * 30f + ring * 11f, 0f) * start * (ring * 8f);
                if (!zones.GetGroundHeight(p, out var height) || height < water + 0.5f)
                {
                    continue;
                }
                var feet = new Vector3(p.x, height, p.z);
                if (!BareGround(feet, sun) || (sun && !Sunlit(feet)))
                {
                    continue;
                }
                for (var i = 0; i < 16; i++)
                {
                    var dir = Quaternion.Euler(0f, i * 22.5f, 0f) * start;
                    if (Lane(feet, dir, lane) && (more == null || more(feet, dir)))
                    {
                        spot.Found = true;
                        spot.Feet = feet;
                        spot.Dir = dir;
                        break;
                    }
                }
            }
            // One frame per ring: search never freeze the game.
            yield return null;
        }
        if (!spot.Found)
        {
            spot.Feet = home;
            spot.Dir = ClearDirection(player, lane, out _);
            yield break;
        }
        if (Utils.DistanceXZ(spot.Feet, home) > 0.05f)
        {
            player.SetCrouch(false);
            MovePlayer(player, spot.Feet + Vector3.up * 0.05f);
            player.m_maxAirAltitude = player.transform.position.y;
            rig.MarkMoved();
            yield return WaitFor(() => player.IsOnGround(), 2f);
            yield return new WaitForSeconds(0.3f);
            player.m_maxAirAltitude = player.transform.position.y;
        }
    }

    // ---------- sneak.attack (T01, T02) ----------

    private static IEnumerator RunAttack()
    {
        var c = new Checks(AttackName);
        var rig = Rig.Create(AttackName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = XpRules(c);
            ServerRules.TestRules = rules;
            SneakXp.TestShowMessage = true;
            Tap.Install();
            rig.SaveAllSkills();
            rig.SetSneak(0f);
            yield return Stand(player);
            var dir = ClearDirection(player, 8f, out _);
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var origin = player.transform.position;
            rig.TakeControls();
            var skill = player.GetSkills().GetSkill(Skills.SkillType.Sneak);

            // T01. KnifeFlint in hand, unaware level-1 Greydwarf (AI off: it stand, never notice), from behind.
            var knife = rig.Give("KnifeFlint");
            var a = rig.Creature("Greydwarf", Ground(origin + dir * 4f), dir, false);
            if (!c.Check(a != null && rig.Equip(knife), "could not spawn a Greydwarf or equip a KnifeFlint"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var behind = new Box();
            yield return GetBehind(rig, a, behind);
            c.Check(behind.Ok, "the player did not crouch behind the Greydwarf");
            c.Check(a.GetLevel() == 1 && Near(a.m_health * Mathf.Max(1, a.GetLevel()), 40f),
                $"level-1 Greydwarf expected (40 health), got level {a.GetLevel()}, {F(a.m_health)} base health");
            c.Check(!a.GetBaseAI().IsAlerted(), "the Greydwarf is alerted before the swing");
            rig.SetSneak(0f);
            var effectNames = CloneNames(a.m_backstabHitEffects);
            var effectsBefore = CountNamed(effectNames);
            var backstabTime = a.m_backstabTime;
            var zdo = a.m_nview.GetZDO();
            Tap.Clear();
            var swing = new Box();
            yield return Swing(rig, a, swing);
            if (c.Check(swing.Ok, $"the knife swing did not hit the Greydwarf ({swing.Detail})"))
            {
                c.Check(a.m_backstabTime != backstabTime, "the knife hit on the unaware Greydwarf was no vanilla backstab");
                if (effectNames.Count > 0)
                {
                    var effectsAfter = CountNamed(effectNames);
                    c.Check(effectsAfter > effectsBefore,
                        $"no backstab effect spawned ({string.Join(", ", effectNames.ToArray())}: {effectsBefore} -> {effectsAfter})");
                }
                else
                {
                    c.Note("the Greydwarf has no backstab effect prefab: effect not checked");
                }
                c.Check(Near(skill.m_level, 1f) && Near(skill.m_accumulator, 0f),
                    $"Sneak 0 -> {F(skill.m_level)} (progress {F(skill.m_accumulator)}), expected level 1");
                c.Check(Tap.Messages("Sneak attack!", MessageHud.MessageType.TopLeft) == 1
                        && Tap.Messages("Sneak attack! +") == 0,
                    $"expected one top-left \"Sneak attack!\" without a percentage, got: {Tap.MessageTexts("Sneak attack")}");
                c.Check(Tap.Messages("$msg_skillup $skill_sneak: 1") == 1,
                    $"the game's level-up message for Sneak 1 was not shown once ({Tap.MessageTexts("$msg_skillup")})");
                c.Check(Tap.Logged("sent 40 health to their game for Sneak XP.") == 1,
                    $"Debug \"sent 40 health\" line: {Tap.Logged("to their game for Sneak XP")} sent line(s), {Tap.FirstLogged("to their game")}");
                c.Check(Tap.Logged("Sneak attack: 7 Sneak XP, Sneak level up.") == 1,
                    $"Debug \"Sneak attack: 7 Sneak XP, Sneak level up.\" not logged once ({Tap.FirstLogged("Sneak attack: ")})");
                // A killed Greydwarf is gone the next physics step (its ZDO with it): stamp read only from a live one.
                if (a != null && a.m_nview.IsValid())
                {
                    c.Check(zdo != null && zdo.GetLong(SneakXp.LastXpKey, 0L) != 0L, "LastXp not written on the Greydwarf");
                }
                else
                {
                    c.Note("the knife killed the Greydwarf: its XP cooldown stamp could not be read");
                }
            }
            if (a != null)
            {
                rig.Remove(a.gameObject);
            }
            yield return Frames(3);

            // T02. Bare hands, the real loop: sneak attack, it chase, smoke at the feet, it give up, sneak again.
            foreach (var held in new[] { player.m_rightItem, player.m_leftItem })
            {
                if (held != null)
                {
                    player.UnequipItem(held, false);
                }
            }
            yield return Stand(player);
            var b = rig.Creature("Greydwarf", Ground(origin + side * 5f + dir * 2f), dir, false);
            if (c.Check(b != null, "could not spawn the second Greydwarf"))
            {
                yield return Frames(3);
                var ai = (MonsterAI)b.GetBaseAI();
                yield return GetBehind(rig, b, behind);
                rig.SetSneak(0f);
                skill.m_level = 20f; // no level up here: progress stay readable
                backstabTime = b.m_backstabTime;
                Tap.Clear();
                yield return Swing(rig, b, swing);
                var paid = Tap.Logged("Sneak attack: ") > 0 && Tap.Logged("Sneak XP") > 0;
                if (c.Check(swing.Ok, $"the bare-hands swing did not hit ({swing.Detail})")
                    && c.Check(b != null && !b.IsDead(), "the bare-hands hit killed the Greydwarf (T02 needs a survivor)"))
                {
                    c.Check(b.m_backstabTime != backstabTime && paid && skill.m_accumulator > 0f,
                        $"first bare-hands hit: backstab {b.m_backstabTime != backstabTime}, XP line {paid}, progress {F(skill.m_accumulator)}");
                    c.Check(ai.IsAlerted() && ReferenceEquals(ai.m_targetCreature, player),
                        "the Greydwarf does not chase the player after the hit");
                    // It chase and fight; smoke at the feet, player a few metres aside inside the cloud.
                    ai.enabled = true;
                    yield return new WaitForSeconds(0.5f);
                    var feet = player.transform.position;
                    var slot = new CloudSlot();
                    yield return rig.PutCloud(slot, feet);
                    player.SetCrouch(false);
                    MovePlayer(player, Ground(feet - Flat(b.transform.position - feet) * 3f) + Vector3.up * 0.05f);
                    var gaveUp = new Box();
                    yield return WaitFor(() => ai.m_targetCreature == null && !ai.IsAlerted(),
                        SmokeCloud.BuildUp(rules.CloudDuration, rules) + rules.ForgetSeconds + 2.5f, gaveUp);
                    if (c.Check(gaveUp.Ok, $"it did not give up in the smoke (target {ai.m_targetCreature != null}, alerted {ai.IsAlerted()})"))
                    {
                        // Unaware again: stand still (AI off) and look away, player behind it, same bare hands.
                        ai.enabled = false;
                        Place(b, b.transform.position, Flat(b.transform.position - player.transform.position));
                        yield return Frames(2);
                        yield return GetBehind(rig, b, behind);
                        c.Check(!ai.IsAlerted(), "the Greydwarf is alerted again before the second sneak attack");
                        backstabTime = b.m_backstabTime;
                        var progress = skill.m_accumulator;
                        var lastXp = b.m_nview.GetZDO().GetLong(SneakXp.LastXpKey, 0L);
                        Tap.Clear();
                        yield return Swing(rig, b, swing);
                        if (c.Check(swing.Ok, $"the second bare-hands swing did not hit ({swing.Detail})"))
                        {
                            c.Check(b.m_backstabTime == backstabTime, "second sneak attack within 5 minutes was a backstab");
                            c.Check(Near(skill.m_accumulator, progress), $"second sneak attack paid XP ({F(progress)} -> {F(skill.m_accumulator)})");
                            c.Check(Tap.Messages("Sneak attack") == 0, $"second sneak attack showed a message: {Tap.MessageTexts("Sneak attack")}");
                            c.Check(Tap.Logged("Sneak XP") == 0, $"second sneak attack logged an XP line: {Tap.FirstLogged("Sneak XP")}");
                            c.Check(b == null || !b.m_nview.IsValid() || b.m_nview.GetZDO().GetLong(SneakXp.LastXpKey, 0L) == lastXp,
                                "second sneak attack started the creature's XP cooldown again");
                        }
                    }
                }
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.xp-sources (T03, T04, T05, X08, X10) ----------

    private static IEnumerator RunXpSources()
    {
        var c = new Checks(XpSourcesName);
        var rig = Rig.Create(XpSourcesName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = XpRules(c);
            ServerRules.TestRules = rules;
            SneakXp.TestShowMessage = true;
            Tap.Install();
            rig.SaveAllSkills();
            rig.SetSneak(5f);
            yield return Stand(player);
            var dir = ClearDirection(player, 10f, out _);
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var origin = player.transform.position;
            rig.TakeControls();
            var skill = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
            var step = skill.m_info.m_increseStep;
            var multiplier = 1f;
            player.GetSEMan().ModifyRaiseSkill(Skills.SkillType.Sneak, ref multiplier);
            float Gain(float xp) => step * xp * Game.m_skillGainRate * multiplier;
            var plain = Near(multiplier, 1f) && Near(Game.m_skillGainRate, 1f) && Near(step, 0.5f);

            // T03. Real arrow (Bow + ArrowWood) on one fresh level-1 Greydwarf, real KnifeFlint swing on another, Sneak 5.
            var a = rig.Creature("Greydwarf", Ground(origin + dir * 7f), dir, false);
            var b = rig.Creature("Greydwarf", Ground(origin + side * 4f), dir, false);
            var knife = rig.Give("KnifeFlint");
            if (!c.Check(a != null && b != null && knife != null, "could not spawn two Greydwarfs or give a KnifeFlint"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            rig.Aim(dir, 0f);
            Tap.Clear();
            var need = skill.GetNextLevelRequirement();
            var backstabTime = a.m_backstabTime;
            var shot = new Box();
            yield return Shoot(rig, "Bow", "ArrowWood", a, 1f, 0f, shot);
            var bowGain = skill.m_accumulator;
            var bowText = "";
            if (c.Check(shot.Ok, $"the arrow did not hit ({shot.Detail})"))
            {
                bowText = SneakXp.MessageText(false, bowGain / need, false);
                c.Check(a.m_backstabTime != backstabTime, "the arrow on the unaware Greydwarf was no backstab");
                c.Check(Near(bowGain, Gain(3.5f), 0.001f), $"arrow sneak attack: progress +{F(bowGain)}, expected +{F(Gain(3.5f))} (3.5 XP)");
                c.Check(Tap.Messages(bowText, MessageHud.MessageType.TopLeft) == 1 && Tap.Messages("Sneak attack") == 1,
                    $"arrow: expected one \"{bowText}\", got: {Tap.MessageTexts("Sneak attack")}");
                c.Check(Tap.Logged("Sneak attack: 3.5 Sneak XP (ranged).") == 1, $"arrow: Debug \"3.5 Sneak XP (ranged)\" missing ({Tap.FirstLogged("Sneak attack: ")})");
                c.Check(Tap.Logged("sent 40 health (ranged) to their game") == 1, $"arrow: Debug \"sent 40 health (ranged)\" missing ({Tap.FirstLogged("to their game")})");
            }
            rig.SetSneak(5f);
            var behind = new Box();
            var swing = new Box();
            if (c.Check(rig.Equip(knife), "could not equip the KnifeFlint"))
            {
                yield return GetBehind(rig, b, behind);
                rig.SetSneak(5f);
                Tap.Clear();
                backstabTime = b.m_backstabTime;
                yield return Swing(rig, b, swing);
                var knifeGain = skill.m_accumulator;
                if (c.Check(swing.Ok, $"the knife swing did not hit ({swing.Detail})"))
                {
                    var knifeText = SneakXp.MessageText(false, knifeGain / need, false);
                    c.Check(b.m_backstabTime != backstabTime, "the knife hit was no backstab");
                    c.Check(Near(knifeGain, Gain(7f), 0.001f), $"knife sneak attack: progress +{F(knifeGain)}, expected +{F(Gain(7f))} (7 XP)");
                    c.Check(Tap.Messages(knifeText, MessageHud.MessageType.TopLeft) == 1 && Tap.Messages("Sneak attack") == 1,
                        $"knife: expected one \"{knifeText}\", got: {Tap.MessageTexts("Sneak attack")}");
                    c.Check(Tap.Logged("Sneak attack: 7 Sneak XP.") == 1, $"knife: Debug \"7 Sneak XP.\" missing ({Tap.FirstLogged("Sneak attack: ")})");
                    c.Check(Near(bowGain * 2f, knifeGain, 0.002f), $"the arrow paid {F(bowGain)}, the knife {F(knifeGain)}: not half");
                    if (plain)
                    {
                        c.Check(bowText == "Sneak attack! +22% Sneak" && knifeText == "Sneak attack! +45% Sneak",
                            $"Sneak 5, not Rested: expected +22% and +45%, got \"{bowText}\" and \"{knifeText}\"");
                    }
                    else
                    {
                        c.Note($"raise multiplier {F(multiplier)}, gain rate {F(Game.m_skillGainRate)}, step {F(step)}: messages "
                               + $"\"{bowText}\" and \"{knifeText}\" (22% and 45% only without Rested at the normal rate)");
                    }
                }
                player.UnequipItem(knife, false);
            }
            rig.Remove(a.gameObject);
            rig.Remove(b.gameObject);
            yield return Stand(player);
            MovePlayer(player, origin + Vector3.up * 0.05f);
            yield return Frames(3);

            // T04. Bigger pay more: level-1 Troll 600 health = 33 XP, level-1 Boar 10 health = 4 XP.
            rig.SetSneak(50f);
            foreach (var big in new[]
                     {
                         new KeyValuePair<string, float[]>("Troll", new[] { 600f, 33f, 12f }),
                         new KeyValuePair<string, float[]>("Boar", new[] { 10f, 4f, 5f }),
                     })
            {
                var v = rig.Creature(big.Key, Ground(origin + dir * big.Value[2]), dir, false);
                if (!c.Check(v != null, $"could not spawn a {big.Key}"))
                {
                    continue;
                }
                yield return Frames(3);
                var health = v.m_health * Mathf.Max(1, v.GetLevel());
                c.Check(v.GetLevel() == 1 && Near(health, big.Value[0]), $"level-1 {big.Key}: {F(health)} health, expected {F(big.Value[0])}");
                Tap.Clear();
                var acc = skill.m_accumulator;
                Hit(v, player, Slash(1f), 3f);
                var xpText = big.Value[1].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                c.Check(Near(skill.m_accumulator - acc, Gain(big.Value[1]), 0.001f),
                    $"{big.Key}: progress +{F(skill.m_accumulator - acc)}, expected +{F(Gain(big.Value[1]))} ({xpText} XP)");
                c.Check(Tap.Logged($"sent {F(big.Value[0])} health to their game") == 1, $"{big.Key}: Debug \"sent {F(big.Value[0])} health\" missing ({Tap.FirstLogged("to their game")})");
                c.Check(Tap.Logged($"Sneak attack: {xpText} Sneak XP.") == 1, $"{big.Key}: Debug \"Sneak attack: {xpText} Sneak XP.\" missing ({Tap.FirstLogged("Sneak attack: ")})");
                rig.Remove(v.gameObject);
                yield return Frames(2);
            }

            // T05. Tamed Boar (what console "tame" call) and training dummy: backstab may happen, nothing paid or said.
            var boar = rig.Creature("Boar", Ground(origin + dir * 4f), dir, false);
            var dummy = rig.Creature("piece_TrainingDummy", Ground(origin - dir * 3f), -dir, false);
            yield return Frames(3);
            if (c.Check(boar != null && dummy != null, "could not spawn a Boar or a training dummy"))
            {
                var tameable = boar.GetComponent<Tameable>();
                if (c.Check(tameable != null, "the Boar has no Tameable"))
                {
                    tameable.Tame();
                }
                c.Check(boar.IsTamed(), "the Boar is not tamed");
                foreach (var quiet in new[] { boar, dummy })
                {
                    Tap.Clear();
                    var acc = skill.m_accumulator;
                    backstabTime = quiet.m_backstabTime;
                    Hit(quiet, player, Slash(1f), 3f);
                    var who = quiet == boar ? "tamed Boar" : "training dummy";
                    c.Check(Near(skill.m_accumulator, acc), $"{who} paid sneak-attack XP");
                    c.Check(quiet.m_nview.GetZDO().GetLong(SneakXp.LastXpKey, 0L) == 0L, $"LastXp written on a {who}");
                    c.Check(Tap.Messages("Sneak attack") == 0, $"{who}: message shown ({Tap.MessageTexts("Sneak attack")})");
                    c.Check(Tap.Logged("Sneak attack") == 0, $"{who}: Debug line logged ({Tap.FirstLogged("Sneak attack")})");
                    c.Note($"{who}: vanilla backstab {(quiet.m_backstabTime != backstabTime ? "happened" : "did not happen")}");
                }
            }
            if (boar != null)
            {
                rig.Remove(boar.gameObject);
            }
            if (dummy != null)
            {
                rig.Remove(dummy.gameObject);
            }
            yield return Frames(2);

            // X08. Real bolt (CrossbowArbalest + BoltBone): ranged, half.
            var x = rig.Creature("Greydwarf", Ground(origin + dir * 7f), dir, false);
            if (c.Check(x != null, "could not spawn a Greydwarf for the bolt"))
            {
                yield return Frames(3);
                rig.SetSneak(50f);
                Tap.Clear();
                yield return Shoot(rig, "CrossbowArbalest", "BoltBone", x, 1f, 0f, shot);
                if (c.Check(shot.Ok, $"the bolt did not hit ({shot.Detail})"))
                {
                    c.Check(Near(skill.m_accumulator, Gain(3.5f), 0.001f), $"bolt sneak attack: progress +{F(skill.m_accumulator)}, expected +{F(Gain(3.5f))}");
                    c.Check(Tap.Logged("Sneak attack: 3.5 Sneak XP (ranged).") == 1, $"bolt: Debug \"3.5 Sneak XP (ranged)\" missing ({Tap.FirstLogged("Sneak attack: ")})");
                }
                rig.Remove(x.gameObject);
                yield return Frames(2);
            }

            // X10. Another sneak-XP mod on this game (forced): our XP stand down and say so, creature cooldown still
            // start; with the rule on, both pay.
            var y = rig.Creature("Greydwarf", Ground(origin + dir * 4f), dir, false);
            if (c.Check(y != null, "could not spawn a Greydwarf for the stand-down"))
            {
                yield return Frames(3);
                Compat.TestOtherModPays = true;
                rig.SetSneak(50f);
                Tap.Clear();
                Hit(y, player, Slash(1f), 3f);
                c.Check(Near(skill.m_accumulator, 0f), $"another sneak-XP mod pays: {ModInfo.Name} paid too (+{F(skill.m_accumulator)})");
                c.Check(Tap.Logged("no Sneak XP from " + ModInfo.Name + ", another installed mod pays it (PayAlongsideOtherSneakXpMods is off).") == 1,
                    "the stand-down Debug line was not logged once");
                c.Check(y.m_nview.GetZDO().GetLong(SneakXp.LastXpKey, 0L) != 0L, "stand-down: the creature's XP cooldown did not start");
                c.Check(Tap.Messages("Sneak attack") == 0, "stand-down: a Sneak attack message was shown");
                ServerRules.TestRules = new AmbushRules { PayAlongsideOtherSneakXpMods = true };
                player.m_nview.InvokeRPC(SneakXp.Rpc, 40f, false);
                c.Check(Near(skill.m_accumulator, Gain(7f), 0.001f), "PayAlongsideOtherSneakXpMods on: 40 health did not pay 7 XP");
                ServerRules.TestRules = rules;
                Compat.TestOtherModPays = false;
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.x-harpoon (X05) ----------

    // Harpoon Hooks Tames must be here and on: without it a harpoon fly through a tame, nothing to check. Then test
    // FAIL (never pass on nothing).
    private static IEnumerator RunHarpoon()
    {
        var c = new Checks(HarpoonName);
        if (!ModActive(HarpoonGuid))
        {
            SelfTest.Fail(HarpoonName, "Harpoon Hooks Tames is not active on this game: hooking a tame cannot be checked");
            yield break;
        }
        var rig = Rig.Create(HarpoonName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            ServerRules.TestRules = XpRules(c);
            SneakXp.TestShowMessage = true;
            Tap.Install();
            rig.SaveAllSkills();
            rig.SetSneak(20f);
            yield return Stand(player);
            var dir = ClearDirection(player, 8f, out _);
            var origin = player.transform.position;
            var skill = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
            var spear = Shared("SpearChitin");
            var boar = rig.Creature("Boar", Ground(origin + dir * 6f), dir, false);
            if (!c.Check(boar != null && spear != null && spear.m_attackStatusEffect != null,
                    "could not spawn a Boar, or SpearChitin has no hook effect"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var tameable = boar.GetComponent<Tameable>();
            if (tameable != null)
            {
                tameable.Tame();
            }
            c.Check(boar.IsTamed(), "the Boar is not tamed");
            rig.Aim(dir, 0f);
            var hook = spear.m_attackStatusEffect.NameHash();
            var health = boar.GetHealth();
            Tap.Clear();
            // Harpoon throw as vanilla launch it: SpearChitin's projectile with its hook effect, player = owner.
            var shot = new Box();
            yield return Shoot(rig, "SpearChitin", null, boar, 20f, 0f, shot,
                () => boar == null || boar.GetSEMan().HaveStatusEffect(hook));
            if (c.Check(shot.Ok && boar != null && boar.GetSEMan().HaveStatusEffect(hook),
                    $"the harpoon did not hook the tamed Boar (health {F(health)} -> {(boar != null ? F(boar.GetHealth()) : "gone")}; {shot.Detail})"))
            {
                c.Check(Near(skill.m_accumulator, 0f), $"hooking a tamed Boar paid Sneak XP (+{F(skill.m_accumulator)})");
                c.Check(boar.m_nview.GetZDO().GetLong(SneakXp.LastXpKey, 0L) == 0L, "LastXp written on the hooked tame");
                c.Check(Tap.Messages("Sneak attack") == 0 && Tap.Logged("Sneak attack") == 0,
                    $"hooking a tame: message or Debug line ({Tap.MessageTexts("Sneak attack")} {Tap.FirstLogged("Sneak attack")})");
            }
            yield return new WaitForSeconds(0.5f);
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.still-notice (T06, T07) ----------

    private static IEnumerator RunStillNotice()
    {
        var c = new Checks(StillNoticeName);
        var rig = Rig.Create(StillNoticeName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            // Fog off (numbers of this test are about light and stillness); still bonus off for T06, like a slow crawl.
            var noStill = new AmbushRules { FogBonus = 0, StillBonus = 0 };
            var rules = new AmbushRules { FogBonus = 0 };
            ServerRules.TestRules = noStill;
            StealthCues.TestShowCues = true;
            Tap.Install();
            rig.SetEnv("Clear");
            rig.SetTime(0.5f);
            rig.SetSneak(0f);
            yield return Stand(player);
            var settled = new Box();
            yield return SettleEnv("Clear", settled);
            // Open ground in the noon sun, found by vanilla's own shade rays (first run: the start spot of the test
            // world was in shade at clear noon, light factor 0.35, and every bar value of the list was off).
            var place = new Spot();
            yield return FindOpenSpot(rig, 12f, true, null, place);
            if (!place.Found)
            {
                c.Note("no sunlit open ground found within 48 m of the player: light and sight values may be off");
            }
            var dir = place.Dir;
            var origin = player.transform.position;
            rig.TakeControls();
            rig.Aim(dir, 0f);
            var crouched = new Box();
            yield return Crouch(player, crouched);
            if (!c.Check(crouched.Ok, "the player did not crouch"))
            {
                c.Report();
                yield break;
            }

            // T06. Noon, open ground, no holding still: bar about 85% at Sneak 0, about 60% at Sneak 100.
            yield return new WaitForSeconds(0.3f);
            yield return Settle(player, settled);
            var light = LightFactor(player);
            var bar0 = player.m_stealthFactor;
            var vanilla0 = VanillaTarget(player);
            c.Check(settled.Ok && Near(bar0, vanilla0 * 0.85f, 0.012f), $"Sneak 0: bar {F(bar0)}, vanilla {F(vanilla0)} x 0.85 = {F(vanilla0 * 0.85f)}");
            c.Check(light >= 0.9f, $"clear noon on open ground: light factor {F(light)}, full light expected (the 85% / 60% values need it)");
            c.Check(bar0 >= 0.79f && bar0 <= 0.87f, $"Sneak 0 at noon: bar {F(bar0)}, expected about 85%");
            rig.SetSneak(100f);
            yield return Settle(player, settled);
            var bar100 = player.m_stealthFactor;
            var vanilla100 = VanillaTarget(player);
            c.Check(settled.Ok && Near(bar100, vanilla100, 0.012f), $"Sneak 100: bar {F(bar100)}, vanilla {F(vanilla100)}");
            c.Check(bar100 >= 0.55f && bar100 <= 0.64f, $"Sneak 100 at noon: bar {F(bar100)}, expected about 60%");
            c.Note($"clear noon, light factor {F(light)}: bar {F(bar0)} at Sneak 0 (normal game {F(vanilla0)}), {F(bar100)} at Sneak 100; "
                   + $"spot {(place.Found ? "found" : "NOT found")} {F(Utils.DistanceXZ(origin, rig.Home))} m from the start, no shade there "
                   + $"{NoShade(player.GetCenterPoint())}; {SunText()}");

            // T07. Holding still: icon about 1 s after crouching, "-70%", bar to about a quarter in 2-3 s.
            rig.SetSneak(0f);
            ServerRules.TestRules = rules;
            yield return Stand(player);
            var crouchAt = Time.time;
            player.SetCrouch(true);
            var cue = new Box();
            yield return WaitFor(() => StealthCues.Has(player, CueKind.Still), 4f, cue);
            var cueAfter = Time.time - crouchAt;
            if (c.Check(cue.Ok, "crouched and still 4 s: no Holding still icon"))
            {
                c.Check(cueAfter >= 0.8f && cueAfter <= 2.4f, $"Holding still icon {F(cueAfter)} s after crouching, expected about 1 s");
                c.Check(StealthCues.IconText(CueKind.Still) == "-70%", $"Holding still icon reads '{StealthCues.IconText(CueKind.Still)}', expected '-70%'");
                var cueAt = Time.time;
                yield return Settle(player, settled);
                var sink = Time.time - cueAt;
                var quarter = player.m_stealthFactor;
                c.Check(settled.Ok && quarter >= 0.2f && quarter <= 0.3f, $"holding still at noon: bar {F(quarter)}, expected about a quarter");
                c.Check(sink >= 1.2f && sink <= 3.6f, $"the bar sank for {F(sink)} s, expected 2-3 s");

                // Turning the camera keep it.
                var target = player.m_stealthFactorTarget;
                var lost = false;
                var turn = Time.time;
                while (Time.time - turn < 1.2f)
                {
                    rig.Aim(Quaternion.Euler(0f, 150f * (Time.time - turn) / 1.2f, 0f) * dir, 0f);
                    yield return new WaitForFixedUpdate();
                    lost |= !StealthState.StillActive || !StealthCues.Has(player, CueKind.Still);
                }
                yield return new WaitForSeconds(0.6f);
                c.Check(!lost && StealthState.StillActive && Near(player.m_stealthFactorTarget, target, 0.01f),
                    $"turning the camera 150 degrees ended holding still (target {F(target)} -> {F(player.m_stealthFactorTarget)})");
                rig.Aim(dir, 0f);

                // A Greydwarf 8-10 m in front, looking at the player, AI on (held in place each frame so its walk
                // cannot change the distance): it never see, hear or target the still player.
                var withStill = player.m_stealthFactor;
                var withoutStill = StealthState.LastWithoutStill;
                var spot = Ground(origin + dir * 9f);
                var g = rig.Creature("Greydwarf", spot, -dir, true);
                if (c.Check(g != null, "could not spawn a Greydwarf"))
                {
                    var ai = (MonsterAI)g.GetBaseAI();
                    var view = ai.m_viewRange;
                    var distance = 9f;
                    if (view * withStill + 0.5f >= distance || view * withoutStill - 0.5f <= distance)
                    {
                        distance = view * (withStill + withoutStill) * 0.5f;
                        spot = Ground(origin + dir * distance);
                        c.Note($"Greydwarf view range {F(view)} m: 9 m is not between sight with ({F(view * withStill)} m) and "
                               + $"without ({F(view * withoutStill)} m) the bonus; test at {F(distance)} m");
                    }
                    var noticed = false;
                    var stillAll = true;
                    var watch = Time.time;
                    while (Time.time - watch < 4f)
                    {
                        Place(g, spot, -dir);
                        yield return null;
                        noticed |= ReferenceEquals(ai.m_targetCreature, player) || ai.CanSeeTarget(player) || ai.CanHearTarget(player);
                        stillAll &= StealthState.StillActive;
                    }
                    c.Check(stillAll, "the player did not stay still while the Greydwarf watched");
                    c.Check(!noticed, $"a Greydwarf {F(distance)} m in front noticed the still player (bar {F(player.m_stealthFactor)}, "
                                      + $"it sees within {F(view * player.m_stealthFactor)} m)");

                    // Control: same place without the bonus (a slow crawl): it see the player and come.
                    ServerRules.TestRules = noStill;
                    var found = new Box();
                    var control = Time.time;
                    while (Time.time - control < 9f && !ReferenceEquals(ai.m_targetCreature, player))
                    {
                        Place(g, spot, -dir);
                        yield return null;
                    }
                    found.Ok = ReferenceEquals(ai.m_targetCreature, player);
                    c.Check(found.Ok, $"without the still bonus the Greydwarf {F(distance)} m in front did not notice the player "
                                      + $"within 9 s (bar {F(player.m_stealthFactor)}, sees {ai.CanSeeTarget(player)})");
                    if (found.Ok)
                    {
                        // Let go of it: it come over (or attack from where it stand: Greydwarfs also throw stones).
                        // Alerted or not is the game's own rule (sight inside alert range x bar; one that only
                        // noticed walk over with the yellow icon): noted, not judged (first run: came unalerted).
                        var before = Vector3.Distance(g.transform.position, player.transform.position);
                        var now = before;
                        var came = false;
                        var free = Time.time;
                        while (Time.time - free < 6f && !came && g != null)
                        {
                            yield return null;
                            if (g == null)
                            {
                                break;
                            }
                            now = Vector3.Distance(g.transform.position, player.transform.position);
                            came = g.InAttack() || now < before - 0.5f;
                        }
                        c.Check(came, $"the Greydwarf that noticed the player did not come for him within 6 s ({F(before)} -> {F(now)} m, "
                                      + $"target kept {ReferenceEquals(ai.m_targetCreature, player)}, alerted {ai.IsAlerted()})");
                        c.Note($"control: the Greydwarf came over {(ai.IsAlerted() ? "alerted" : "not alerted yet (it only noticed)")}: "
                               + $"{F(before)} -> {F(now)} m, alert range {F(ai.m_alertRange)} m x bar {F(player.m_stealthFactor)}");
                    }
                    c.Note($"Greydwarf view range {F(view)} m; bar {F(withStill)} still, {F(withoutStill)} without the bonus; watched at {F(distance)} m");
                }
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.still-ends (T08, X02) ----------

    private static IEnumerator RunStillEnds()
    {
        var c = new Checks(StillEndsName);
        var rig = Rig.Create(StillEndsName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { FogBonus = 0 };
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            Tap.Install();
            rig.SaveAllSkills();
            rig.SetTime(0.5f);
            rig.SetSneak(0f);
            yield return Stand(player);
            // Long open stretch: the Karve of the last step sails along it.
            var dir = ClearDirection(player, 18f, out var open);
            if (!open)
            {
                c.Note("no 18 m of open ground found around the player: the ship step may bump into the scenery");
            }
            var origin = player.transform.position;
            rig.TakeControls();
            rig.Aim(dir, 0f);
            rig.MarkMoved();
            var still = new Box();
            var gone = new Box();

            // 1. One step.
            yield return HoldStill(rig, still);
            if (!c.Check(still.Ok, "crouched and still: no Holding still bonus (nothing to end)"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1.5f);
            var without = StealthState.LastWithoutStill;
            var sunk = player.m_stealthFactor;
            rig.Drive(Vector3.forward);
            yield return StillGone(rig, gone, 0.6f);
            var jumped = player.m_stealthFactor;
            rig.Drive(Vector3.zero);
            c.Check(gone.Ok, "one step: Holding still did not end");
            c.Check(sunk < without - 0.05f && jumped >= Mathf.Min(without, player.m_stealthFactorTarget) - 0.02f,
                $"one step: bar {F(sunk)} -> {F(jumped)}, expected a jump to {F(without)}");

            // 2. Standing up.
            yield return HoldStill(rig, still);
            yield return new WaitForSeconds(1f);
            without = StealthState.LastWithoutStill;
            sunk = player.m_stealthFactor;
            player.SetCrouch(false);
            yield return StillGone(rig, gone);
            c.Check(still.Ok && gone.Ok, $"standing up: Holding still did not end (was on {still.Ok})");
            c.Check(player.m_stealthFactor >= without - 0.02f, $"standing up: bar {F(sunk)} -> {F(player.m_stealthFactor)}, expected at least {F(without)} at once");
            yield return Stand(player);

            // 3. Jump key while crouched (the normal game rolls).
            yield return HoldStill(rig, still);
            // Bar let sink first, so "it jumps up at once" can be seen (same for the hit below).
            yield return new WaitForSeconds(1.5f);
            without = StealthState.LastWithoutStill;
            sunk = player.m_stealthFactor;
            player.SetControls(Vector3.zero, false, false, false, false, false, false, true, false, false, false);
            yield return null;
            rig.Drive(Vector3.zero);
            var rolled = false;
            var wait = Time.time;
            while (Time.time - wait < 1.5f && (StealthState.StillActive || StealthCues.Has(player, CueKind.Still)))
            {
                rolled |= player.InDodge();
                yield return new WaitForFixedUpdate();
            }
            c.Check(still.Ok && !StealthState.StillActive && !StealthCues.Has(player, CueKind.Still),
                $"jump key while crouched: Holding still did not end (roll seen {rolled})");
            c.Check(sunk < without - 0.05f && player.m_stealthFactor >= Mathf.Min(without, player.m_stealthFactorTarget) - 0.02f,
                $"jump key while crouched: bar {F(sunk)} -> {F(player.m_stealthFactor)}, expected a jump to {F(without)} at once");
            yield return WaitFor(() => !player.InDodge() && player.IsOnGround(), 2.5f);
            yield return new WaitForSeconds(0.3f);

            // 4. Taking a hit (god mode off for it): a Greydwarf's own attack numbers.
            var g = rig.Creature("Greydwarf", Ground(player.transform.position + dir * 1.6f), -dir, false);
            if (c.Check(g != null, "could not spawn a Greydwarf"))
            {
                yield return HoldStill(rig, still);
                var claw = ((Humanoid)g).GetInventory().GetAllItems().FirstOrDefault(i => i.m_shared.m_damages.GetTotalDamage() > 0f);
                var hit = new HitData
                {
                    m_damage = claw != null ? claw.GetDamage() : Blunt(10f),
                    m_pushForce = claw != null ? claw.m_shared.m_attackForce : 30f,
                    m_staggerMultiplier = 1f,
                    m_point = player.GetCenterPoint(),
                    m_dir = Flat(player.transform.position - g.transform.position),
                    m_hitType = HitData.HitType.EnemyHit,
                    m_blockable = true,
                    m_dodgeable = true,
                };
                var total = hit.m_damage.GetTotalDamage();
                // A hit wears the armour: its durability goes back after.
                var worn = player.GetInventory().GetAllItems().Select(i => new KeyValuePair<ItemDrop.ItemData, float>(i, i.m_durability)).ToList();
                rig.GodOff();
                // Full health for the hit (earlier creature hits may have left the god-mode player at 1): it must
                // hurt, never kill.
                var healthBefore = player.GetHealth();
                player.SetHealth(player.GetMaxHealth());
                var health = player.GetHealth();
                if (total > health - 6f)
                {
                    hit.m_damage.Modify(Mathf.Max(0.05f, (health - 6f) / total));
                }
                hit.SetAttacker(g);
                var spotBefore = player.transform.position;
                yield return new WaitForSeconds(1.5f);
                var stillAtHit = StealthState.StillActive;
                without = StealthState.LastWithoutStill;
                sunk = player.m_stealthFactor;
                if (health > 8f)
                {
                    player.Damage(hit);
                }
                var staggered = false;
                wait = Time.time;
                while (Time.time - wait < 1.5f && (StealthState.StillActive || StealthCues.Has(player, CueKind.Still)))
                {
                    staggered |= player.IsStaggering();
                    yield return new WaitForFixedUpdate();
                }
                c.Check(stillAtHit && sunk < without - 0.05f && player.m_stealthFactor >= Mathf.Min(without, player.m_stealthFactorTarget) - 0.02f,
                    $"taking a hit: bar {F(sunk)} -> {F(player.m_stealthFactor)}, expected a jump to {F(without)} at once (still before the hit {stillAtHit})");
                c.Check(player.GetHealth() < health - 0.1f, $"the hit did not hurt the player ({F(health)} -> {F(player.GetHealth())}): nothing checked");
                c.Check(still.Ok && !StealthState.StillActive && !StealthCues.Has(player, CueKind.Still),
                    $"taking a hit of {F(hit.m_damage.GetTotalDamage())}: Holding still did not end (staggered {staggered}, moved "
                    + $"{F(Vector3.Distance(spotBefore, player.transform.position))} m)");
                c.Note($"hit by a Greydwarf ({(claw != null ? claw.m_shared.m_name : "no attack item found")}, "
                       + $"{F(hit.m_damage.GetTotalDamage())} damage): staggered {staggered}, pushed {F(Vector3.Distance(spotBefore, player.transform.position))} m");
                player.SetGodMode(true);
                player.SetHealth(healthBefore);
                foreach (var item in worn)
                {
                    item.Key.m_durability = item.Value;
                }
                rig.Remove(g.gameObject);
                yield return WaitFor(() => !player.IsStaggering(), 3f);
                yield return new WaitForSeconds(0.3f);
            }

            // 5. Bow draw: the list only ask to note what happens. Draw cancelled before any arrow leaves.
            var bow = rig.Give("Bow");
            var arrows = rig.Give("ArrowWood", 5);
            if (c.Check(bow != null && arrows != null && rig.Equip(bow), "could not equip a Bow with ArrowWood"))
            {
                var arrowName = arrows.m_shared.m_name;
                var arrowCount = player.GetInventory().CountItems(arrowName, -1, false);
                yield return HoldStill(rig, still);
                var drew = false;
                var draw = Time.time;
                while (Time.time - draw < 1.2f)
                {
                    player.SetControls(Vector3.zero, false, true, false, false, false, false, false, false, false, false);
                    yield return new WaitForFixedUpdate();
                    drew |= player.IsDrawingBow();
                }
                c.Note($"bow draw from Holding still (drawing seen {drew}): crouching {player.IsCrouching()}, bonus "
                       + $"{StealthState.StillActive}, icon {(StealthCues.Has(player, CueKind.Still) ? "stays" : "goes")}");
                // What the block key does to a draw in the normal game: draw dropped, pose off, nothing shot.
                player.m_attackDrawTime = -1f;
                var pose = bow.m_shared.m_attack != null ? bow.m_shared.m_attack.m_drawAnimationState : "";
                if (!string.IsNullOrEmpty(pose))
                {
                    player.m_zanim.SetBool(pose, false);
                }
                rig.Drive(Vector3.zero);
                yield return new WaitForSeconds(0.4f);
                c.Check(player.GetInventory().CountItems(arrowName, -1, false) == arrowCount, "the cancelled bow draw used an arrow");
                player.UnequipItem(bow, false);
            }

            // 6. X02: raising a tower shield end the crouch (normal game) and Holding still with it.
            var shield = rig.Give("ShieldWoodTower");
            if (c.Check(shield != null && rig.Equip(shield), "could not equip a ShieldWoodTower"))
            {
                yield return HoldStill(rig, still);
                // Block key down (first step also the "press", for players who set block to toggle), then held.
                var press = true;
                var block = Time.time;
                while (Time.time - block < 0.8f)
                {
                    player.SetControls(Vector3.zero, false, false, false, false, press, true, false, false, false, false);
                    press = false;
                    yield return new WaitForFixedUpdate();
                }
                c.Check(player.m_blocking, "the block key did not raise the shield: nothing checked");
                c.Check(still.Ok && !player.IsCrouching() && !StealthState.StillActive && !StealthCues.Has(player, CueKind.Still),
                    $"raised tower shield: crouching {player.IsCrouching()}, bonus {StealthState.StillActive}, icon "
                    + $"{StealthCues.Has(player, CueKind.Still)} (was still {still.Ok})");
                if (player.m_toggleBlock && player.m_blocking)
                {
                    player.SetControls(Vector3.zero, false, false, false, false, true, false, false, false, false, false);
                }
                rig.Drive(Vector3.zero);
                player.UnequipItem(shield, false);
                yield return new WaitForSeconds(0.3f);
            }

            // 7. Deck of a moving Karve: no icon while it moves, icon while it lies still.
            var ship = new Box();
            yield return ShipCase(c, rig, origin, dir, ship);
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Karve held just above dry ground (no water near the spawn of the test world), no gravity, moved at 2 m/s by its
    // own body velocity: player on deck is carried like on a sailing ship (vanilla ground-body follow). What end the
    // bonus there is the world displacement between two refreshes, whatever move the ship.
    private static IEnumerator ShipCase(Checks c, Rig rig, Vector3 origin, Vector3 dir, Box result)
    {
        var player = rig.Player;
        // Player back where the test began (steps and the roll moved him), ship ahead of him.
        player.SetCrouch(false);
        MovePlayer(player, origin + Vector3.up * 0.05f);
        yield return new WaitForSeconds(0.3f);
        var start = Ground(origin + dir * 7f) + Vector3.up * 2f;
        var go = rig.Spawn("Karve", start, Quaternion.LookRotation(dir));
        var ship = go != null ? go.GetComponent<Ship>() : null;
        var body = go != null ? go.GetComponent<Rigidbody>() : null;
        if (!c.Check(ship != null && body != null, "could not spawn a Karve"))
        {
            yield break;
        }
        body.useGravity = false;
        var held = Time.time;
        while (Time.time - held < 0.5f)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
        }
        // Deck: a ray down beside the mast (fore or aft of the middle), highest flat hit of the ship near its floor
        // level (mast top and yard are metres higher).
        var deck = go.transform.position + Vector3.up * 0.5f;
        var foundDeck = false;
        foreach (var along in new[] { -1.5f, 1.5f, -2.5f, 2.5f, -0.8f, 0.8f })
        {
            var above = go.transform.position + go.transform.forward * along + Vector3.up * 6f;
            foreach (var hit in Physics.RaycastAll(above, Vector3.down, 9f).OrderBy(h => h.distance))
            {
                if (hit.collider != null && !hit.collider.isTrigger && hit.collider.transform.IsChildOf(go.transform)
                    && hit.normal.y > 0.6f && hit.point.y < go.transform.position.y + 1.5f)
                {
                    deck = hit.point;
                    foundDeck = true;
                    break;
                }
            }
            if (foundDeck)
            {
                break;
            }
        }
        c.Check(foundDeck, "no deck found on the Karve (rays down beside its mast hit no flat part of it)");
        player.SetCrouch(false);
        MovePlayer(player, deck + Vector3.up * 0.15f);
        var aboard = new Box();
        var boarding = Time.time;
        while (Time.time - boarding < 3f && !ReferenceEquals(player.GetStandingOnShip(), ship))
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
        }
        aboard.Ok = ReferenceEquals(player.GetStandingOnShip(), ship);
        if (!c.Check(aboard.Ok, "the player does not stand on the Karve's deck"))
        {
            yield break;
        }
        // Ship at rest: the bonus come (control: deck itself does not stop it).
        player.SetCrouch(true);
        var rest = Time.time;
        while (Time.time - rest < 4f && !(StealthState.StillActive && StealthCues.Has(player, CueKind.Still)))
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
        }
        c.Check(StealthState.StillActive && StealthCues.Has(player, CueKind.Still), "crouched on the deck of a Karve at rest: no Holding still icon");
        // Ship moves.
        var from = player.transform.position;
        var anyStill = false;
        var calm = 0;
        var samples = 0;
        var onDeck = 0;
        var late = false;
        var sail = Time.time;
        while (Time.time - sail < 3.5f)
        {
            body.linearVelocity = dir * 2f;
            body.angularVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
            samples++;
            if (ReferenceEquals(player.GetStandingOnShip(), ship))
            {
                onDeck++;
            }
            if (player.IsCrouching() && player.IsOnGround() && !player.IsSneaking())
            {
                calm++;
            }
            // First refresh after the start may still hold the bonus (0.5 s rhythm): counted from 1 s on.
            if (Time.time - sail > 1f)
            {
                late = true;
                anyStill |= StealthState.StillActive || StealthCues.Has(player, CueKind.Still);
            }
        }
        body.linearVelocity = Vector3.zero;
        var carried = Vector3.Distance(from, player.transform.position);
        c.Check(carried > 3f && onDeck > samples * 0.8f,
            $"the moving Karve carried the player only {F(carried)} m in 3.5 s (on deck {onDeck} of {samples} steps): nothing checked");
        c.Check(late && !anyStill, "crouched on the deck of a moving Karve: Holding still bonus or icon on");
        c.Note($"moving Karve: carried {F(carried)} m in 3.5 s, {calm} of {samples} steps crouched, standing and not walking, on deck {onDeck}");
        // Off the ship first (its deck volume count the player out), then the ship go.
        player.SetCrouch(false);
        MovePlayer(player, origin + Vector3.up * 0.1f);
        player.m_maxAirAltitude = player.transform.position.y;
        var off = Time.time;
        while (Time.time - off < 1f && player.InNumShipVolumes > 0)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
        }
        rig.Remove(go);
        yield return new WaitForSeconds(0.3f);
        if (player.InNumShipVolumes > 0)
        {
            c.Note("the player still counted as aboard after the Karve was removed: count put back to 0");
            player.InNumShipVolumes = 0;
        }
        player.m_maxAirAltitude = player.transform.position.y;
        result.Ok = true;
    }

    // ---------- sneak.foliage (T09) ----------

    private static IEnumerator RunFoliage()
    {
        var c = new Checks(FoliageName);
        var rig = Rig.Create(FoliageName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { StillBonus = 0, FogBonus = 0 };
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            Tap.Install();
            rig.SetEnv("Clear");
            rig.SetTime(0.5f);
            rig.SetSneak(0f);
            yield return Stand(player);
            var settled = new Box();
            yield return SettleEnv("Clear", settled);
            // Open ground in the noon sun (first run: the start spot was in shade already, so a tree could add none).
            var place = new Spot();
            yield return FindOpenSpot(rig, 14f, true, null, place);
            if (!place.Found)
            {
                c.Note("no sunlit open ground found within 48 m of the player: the shade check may fail on the scenery");
            }
            var dir = place.Dir;
            var crouched = new Box();
            yield return Crouch(player, crouched);
            if (!c.Check(crouched.Ok, "the player did not crouch"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.6f);
            yield return ForceRefresh(player);
            var openLight = LightFactor(player);
            c.Check(openLight >= 0.9f, $"clear noon on open ground: light factor {F(openLight)}, full light expected (nothing to shade)");
            c.Check(!StealthCues.Has(player, CueKind.Foliage), "In foliage icon on open ground before any bush");
            var seman = player.GetSEMan();
            var hash = StealthCues.NameHash(CueKind.Foliage);
            var feet = player.transform.position;
            var dialog = InventoryGui.instance != null ? InventoryGui.instance.m_textsDialog : null;

            foreach (var name in new[] { "RaspberryBush", "Bush01" })
            {
                var at = feet + dir * (player.GetRadius() + BushStemRadius + 0.1f);
                at.y = ZoneSystem.instance.GetSolidHeight(at, out var solid, 2) ? solid : Ground(at).y;
                var bush = rig.Spawn(name, at, Quaternion.identity);
                if (!c.Check(bush != null, $"could not spawn {name}"))
                {
                    continue;
                }
                yield return new WaitForSeconds(1.2f);
                c.Note($"{name} colliders: {Colliders(bush)}; player moved {V(player.transform.position - feet)}, touching "
                       + $"{StealthState.FoliageTouching}, crown {StealthState.FoliageCrown}, light factor {F(LightFactor(player))} "
                       + $"(open ground {F(openLight)})");
                c.Check(StealthState.FoliageTouching || StealthState.FoliageCrown, $"crouched against {name}: no foliage found");
                var effect = seman.GetStatusEffect(hash);
                if (c.Check(effect != null, $"crouched against {name}: no In foliage icon"))
                {
                    c.Check(effect.GetIconText() == "", $"{name}: In foliage icon shows '{effect.GetIconText()}', expected no number");
                    var tip = effect.GetTooltipString();
                    c.Check(tip.Contains("block creatures' sight") && tip.Contains("shade you"), $"{name}: icon tooltip does not explain sight block and shade: {tip}");
                    if (dialog != null)
                    {
                        // What the compendium's Active effects page print (vanilla builder), then taken out again.
                        var count = dialog.m_texts.Count;
                        dialog.AddActiveEffects();
                        var page = dialog.m_texts.Count > count ? dialog.m_texts[0].m_text : "";
                        if (dialog.m_texts.Count > count)
                        {
                            dialog.m_texts.RemoveAt(0);
                        }
                        c.Check(page.Contains("In foliage") && page.Contains("block creatures' sight") && page.Contains("shade you"),
                            $"{name}: the Active effects page does not explain the In foliage icon: {page}");
                    }
                    else
                    {
                        c.Check(false, "no compendium text dialog found");
                    }
                }
                // Into the open (6 m from the bush, still crouched): icon stays about 1 s, then goes.
                MovePlayer(player, Ground(feet + dir * 6.8f) + Vector3.up * 0.02f);
                rig.MarkMoved();
                var left = Time.time;
                yield return new WaitForSeconds(0.3f);
                c.Check(StealthCues.Has(player, CueKind.Foliage), $"away from {name}: the In foliage icon went at once (expected about 1 s later)");
                var away = new Box();
                yield return WaitFor(() => !StealthCues.Has(player, CueKind.Foliage), 2.2f, away);
                var after = Time.time - left;
                c.Check(away.Ok && after >= 0.4f && after <= 2.2f, $"away from {name}: In foliage icon {(away.Ok ? "went" : "still there")} after {F(after)} s, expected about 1 s");
                rig.Remove(bush);
                MovePlayer(player, feet + Vector3.up * 0.02f);
                yield return new WaitForSeconds(0.7f);
            }

            // Under a big tree: same icon, and shade (light factor down).
            var tree = rig.Spawn("Beech1", Ground(player.transform.position + dir * 2f), Quaternion.identity);
            if (c.Check(tree != null, "could not spawn Beech1"))
            {
                yield return new WaitForSeconds(1.2f);
                var treeLight = LightFactor(player);
                c.Check(StealthState.FoliageTouching || StealthState.FoliageCrown, "under Beech1: no foliage found");
                c.Check(StealthCues.Has(player, CueKind.Foliage), "under Beech1: no In foliage icon");
                c.Check(StealthCues.IconText(CueKind.Foliage) == "", $"under Beech1: icon shows '{StealthCues.IconText(CueKind.Foliage)}'");
                c.Check(treeLight < openLight - 0.02f, $"under Beech1 at noon: light factor {F(treeLight)}, open ground {F(openLight)}: no shade");
                c.Note($"under Beech1 at noon: light factor {F(treeLight)} (open ground {F(openLight)}, spot {(place.Found ? "found" : "NOT found")} "
                       + $"{F(Utils.DistanceXZ(feet, rig.Home))} m from the start); {SunText()}");
                rig.Remove(tree);
                yield return Frames(2);
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.bush-sight (T10) ----------

    private static IEnumerator RunBushSight()
    {
        var c = new Checks(BushSightName);
        var rig = Rig.Create(BushSightName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            ServerRules.TestRules = new AmbushRules { StillBonus = 0, FogBonus = 0 };
            rig.SetEnv("Clear");
            rig.SetTime(0.5f);
            rig.SetSneak(0f);
            yield return Stand(player);
            var dir = ClearDirection(player, 12f, out var clear);
            if (!clear)
            {
                c.Note("no open ground found: sight may be blocked by the scenery");
            }
            var crouched = new Box();
            yield return Crouch(player, crouched);
            if (!c.Check(crouched.Ok, "the player did not crouch"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1.5f);
            var feet = player.transform.position;
            var far = Ground(feet + dir * 6f);
            var g = rig.Creature("Greydwarf", far, -dir, false);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var ai = g.GetBaseAI();
            c.Check(ai.CanSeeTarget(player), $"no bush: the Greydwarf 6 m in front does not see the crouched player (bar {F(player.m_stealthFactor)}, view {F(ai.m_viewRange)} m)");
            var at = feet + dir * (player.GetRadius() + BushStemRadius + 0.1f);
            at.y = ZoneSystem.instance.GetSolidHeight(at, out var solid, 2) ? solid : Ground(at).y;
            var bush = rig.Spawn("Bush01", at, Quaternion.identity);
            if (c.Check(bush != null, "could not spawn Bush01"))
            {
                yield return new WaitForSeconds(0.8f);
                Place(g, far, -dir);
                yield return Frames(2);
                c.Check(!ai.CanSeeTarget(player), "the Greydwarf on the other side of the bush sees the crouched player");
                // It walks round the bush: same distance, 100 degrees to the side, looking at the player.
                var round = Ground(feet + Quaternion.Euler(0f, 100f, 0f) * dir * 6f);
                Place(g, round, feet - round);
                yield return Frames(2);
                var seesRound = ai.CanSeeTarget(player);
                if (!seesRound)
                {
                    round = Ground(feet + Quaternion.Euler(0f, -100f, 0f) * dir * 6f);
                    Place(g, round, feet - round);
                    yield return Frames(2);
                    seesRound = ai.CanSeeTarget(player);
                }
                c.Check(seesRound, "the Greydwarf beside the bush (clear line) does not see the crouched player");
                rig.Remove(bush);
                Place(g, far, -dir);
                yield return Frames(3);
                c.Check(ai.CanSeeTarget(player), "bush gone: the Greydwarf in front does not see the player again");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.fog (T11, T12) ----------

    private static IEnumerator RunFog()
    {
        var c = new Checks(FogName);
        var rig = Rig.Create(FogName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { StillBonus = 0 };
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            Tap.Install();
            rig.SetSneak(0f);
            yield return Stand(player);
            var origin = player.transform.position;
            var crouched = new Box();
            yield return Crouch(player, crouched);
            if (!c.Check(crouched.Ok, "the player did not crouch"))
            {
                c.Report();
                yield break;
            }
            var settled = new Box();
            var lines = new List<string>();

            // Weather and hour, then the icon. want: exact text, "" = no icon, null = only inside lo..hi percent.
            IEnumerator Weather(string env, float tod, string want, int lo, int hi)
            {
                if (EnvMan.instance.GetEnv(env) == null)
                {
                    c.Check(false, $"weather {env} does not exist in this game version");
                    yield break;
                }
                rig.SetEnv(env);
                rig.SetTime(tod);
                settled.Ok = false;
                yield return SettleEnv(env, settled);
                if (!c.Check(settled.Ok, $"weather {env} did not settle ({settled.Detail})"))
                {
                    yield break;
                }
                yield return new WaitForSeconds(0.8f);
                var shown = StealthCues.Has(player, CueKind.Fog);
                var text = shown ? StealthCues.IconText(CueKind.Fog) : "";
                var pct = StealthState.FogPercent(rules);
                lines.Add($"{env} at {F(tod)}: density {F(StealthState.FogDensity)}, {pct}%, icon '{text}'");
                if (want != null)
                {
                    c.Check(text == want, $"{env} at tod {F(tod)}: Fog icon '{text}', expected '{want}' (density {F(StealthState.FogDensity)})");
                }
                else
                {
                    c.Check(shown && pct >= lo && pct <= hi && text == $"-{pct}%",
                        $"{env} at tod {F(tod)}: Fog icon '{text}' ({pct}%), expected {lo}-{hi}%");
                }
            }

            yield return Weather("Misty", 0f, "-30%", 0, 0);
            yield return Weather("Misty", 0.5f, "-4%", 0, 0);
            yield return Weather("Clear", 0.5f, "", 0, 0);
            yield return Weather("SwampRain", 0.5f, null, 4, 9);
            yield return Weather("SwampRain", 0f, null, 4, 9);
            yield return Weather("Darklands_dark", 0.5f, null, 4, 9);

            // Standing up: no fog icon.
            yield return Weather("Misty", 0f, "-30%", 0, 0);
            player.SetCrouch(false);
            var gone = new Box();
            yield return WaitFor(() => !StealthCues.Has(player, CueKind.Fog), 2f, gone);
            c.Check(gone.Ok, "standing up in thick fog: the Fog icon stays");

            // Inside a dungeon (the game: above 3000 m): no fog icon although the weather is Misty. Floor of the test
            // up there, player on it, then back.
            var high = new Vector3(origin.x, 3010f, origin.z);
            var floor = new GameObject(ModInfo.Guid + ".TestFloor");
            rig.Spawned.Add(floor);
            floor.layer = LayerMask.NameToLayer("piece");
            floor.transform.position = high - Vector3.up * 0.5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(8f, 1f, 8f);
            yield return null;
            MovePlayer(player, high + Vector3.up * 0.1f);
            rig.MarkMoved();
            var landed = new Box();
            yield return WaitFor(() => player.IsOnGround(), 3f, landed);
            yield return Crouch(player, crouched);
            yield return new WaitForSeconds(1.3f);
            c.Check(landed.Ok && crouched.Ok && player.InInterior(), $"the player is not crouched in an interior (on the floor {landed.Ok}, crouched {crouched.Ok}, interior {player.InInterior()})");
            c.Check(StealthState.FogDensityKnown && StealthState.FogShareFor(StealthState.FogDensity, rules) >= 0.99f,
                $"interior: the weather is no longer thick fog (density {F(StealthState.FogDensity)}): nothing checked");
            c.Check(Near(StealthState.FogShare, 0f) && !StealthCues.Has(player, CueKind.Fog),
                $"inside a dungeon: fog share {F(StealthState.FogShare)}, Fog icon {StealthCues.Has(player, CueKind.Fog)}");
            MovePlayer(player, origin + Vector3.up * 0.1f);
            player.m_maxAirAltitude = player.transform.position.y;
            rig.Remove(floor);
            yield return WaitFor(() => player.IsOnGround(), 3f, landed);
            player.m_maxAirAltitude = player.transform.position.y;
            yield return Crouch(player, crouched);
            var back = new Box();
            yield return WaitFor(() => StealthCues.Has(player, CueKind.Fog), 2.5f, back);
            c.Check(back.Ok, "back outdoors in thick fog: no Fog icon");
            c.Note("fog: " + string.Join("; ", lines.ToArray()));

            // T12. In the mist (the mist itself is forced: only the icon logic is checked here).
            yield return Weather("Clear", 0.5f, "", 0, 0);
            c.Check(!StealthCues.Has(player, CueKind.Mist), "In the mist icon in the Meadows");
            Tap.ForceMist = true;
            var mist = new Box();
            yield return WaitFor(() => StealthCues.Has(player, CueKind.Mist), 2f, mist);
            if (c.Check(mist.Ok, "crouched in mist: no In the mist icon"))
            {
                var effect = player.GetSEMan().GetStatusEffect(StealthCues.NameHash(CueKind.Mist));
                c.Check(effect != null && effect.GetIconText() == "", "In the mist icon shows a number");
                c.Check(effect != null && effect.GetTooltipString() == "Creatures without mist sight cannot see you from more than 10 m away.",
                    "In the mist tooltip text");
                player.SetCrouch(false);
                yield return WaitFor(() => !StealthCues.Has(player, CueKind.Mist), 2f, gone);
                c.Check(gone.Ok, "standing up in mist: the In the mist icon stays");
            }
            Tap.ForceMist = false;
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.recipe (T13, X07, T30) ----------

    private static IEnumerator RunRecipe()
    {
        var c = new Checks(RecipeName);
        var rig = Rig.Create(RecipeName);
        if (rig == null)
        {
            yield break;
        }
        var owner = rig.Player;
        var gui = InventoryGui.instance;
        // What the player knew: put back whole (other recipes unlock with the three materials too).
        var knownRecipes = new HashSet<string>(owner.m_knownRecipes);
        var knownMaterial = new HashSet<string>(owner.m_knownMaterial);
        var knownStations = new Dictionary<string, int>(owner.m_knownStations);
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules();
            ServerRules.TestRules = rules;
            Tap.Install();
            rig.SaveAllSkills();
            yield return Stand(player);
            var dir = ClearDirection(player, 6f, out _);
            var origin = player.transform.position;
            var recipe = SmokeContent.CraftRecipe;
            var resin = Shared("Resin");
            var coal = Shared("Coal");
            var scraps = Shared("LeatherScraps");
            if (!c.Check(gui != null && recipe != null && resin != null && coal != null && scraps != null,
                    "no inventory screen, no Smoke Screen recipe, or Resin / Coal / LeatherScraps missing"))
            {
                c.Report();
                yield break;
            }
            if (FeatureRegistry.Find(ForgeGuid) != null)
            {
                c.Check(ModActive(ForgeGuid) && ModActive(ModInfo.Guid), "Forge Idol Upgrades and Sneak Ambush are not both active");
            }
            else
            {
                c.Note("Forge Idol Upgrades is not installed: X07 not checked");
            }

            // A character that never held the three materials, next to a workbench.
            foreach (var shared in new[] { resin, coal, scraps })
            {
                player.m_knownMaterial.Remove(shared.m_name);
                rig.Track(shared.m_name);
            }
            player.m_knownRecipes.Remove(SmokeContent.DisplayName);
            var benchGo = rig.Spawn(AmbushRules.DefaultRecipeStation, Ground(origin + dir * 1.6f), Quaternion.LookRotation(-dir));
            var station = benchGo != null ? benchGo.GetComponent<CraftingStation>() : null;
            if (!c.Check(station != null, "could not spawn piece_workbench"))
            {
                c.Report();
                yield break;
            }
            var knows = new Box();
            yield return WaitFor(() => player.m_knownStations.ContainsKey(station.m_name), 4f, knows);
            c.Check(knows.Ok, "the player next to the workbench does not know the station");
            bool Known() => player.m_knownRecipes.Contains(SmokeContent.DisplayName);
            c.Check(!Known(), "the recipe is known before any material was picked up");
            var before = new[]
            {
                player.GetInventory().CountItems(resin.m_name, -1, false),
                player.GetInventory().CountItems(coal.m_name, -1, false),
                player.GetInventory().CountItems(scraps.m_name, -1, false),
            };
            rig.Give("Resin", 2);
            c.Check(!Known(), "the recipe appeared with Resin only");
            rig.Give("Coal", 1);
            c.Check(!Known(), "the recipe appeared with Resin and Coal only");
            rig.Give("LeatherScraps", 1);
            c.Check(Known(), "the recipe did not appear once Resin, Coal and Leather scraps were picked up");

            // At the workbench: listed, description shown, craft uses 2 + 1 + 1 and gives 2.
            MovePlayer(player, Ground(benchGo.transform.position - dir * 1.3f) + Vector3.up * 0.05f);
            rig.MarkMoved();
            yield return Frames(3);
            c.Check(station.InUseDistance(player), "the player is not within reach of the workbench");
            c.Note($"spawned workbench usable {station.CheckUsable(player, false)} (a workbench needs a roof; the test opens it directly)");
            player.SetCraftingStation(station);
            gui.Show(null, 3);
            yield return Frames(4);
            var available = new List<Recipe>();
            player.GetAvailableRecipes(ref available);
            c.Check(available.Contains(recipe), "the Smoke Screen recipe is not available at the workbench");
            var index = -1;
            for (var i = 0; i < gui.m_availableRecipes.Count; i++)
            {
                if (gui.m_availableRecipes[i].Recipe == recipe)
                {
                    index = i;
                }
            }
            if (c.Check(index >= 0, $"the Smoke Screen is not in the workbench's recipe list ({gui.m_availableRecipes.Count} listed)"))
            {
                gui.SetRecipe(index, false);
                yield return Frames(4);
                var shown = gui.m_recipeDecription != null ? gui.m_recipeDecription.text : "";
                c.Check(gui.m_recipeName != null && gui.m_recipeName.text.Contains(SmokeContent.DisplayName), "the selected recipe is not named Smoke Screen");
                c.Check(shown.Contains("A pouch of soot and resin."), $"the recipe panel does not show the description: {shown}");
                var tip = ItemDrop.ItemData.GetTooltip(recipe.m_item.m_itemData, 1, false, Game.m_worldLevel);
                c.Check(tip.Contains("A pouch of soot and resin."), "the item tooltip does not show the description");
                var smoke = rig.SmokeCount();
                gui.OnCraftPressed();
                c.Check(gui.m_craftTimer >= 0f, "pressing Craft did not start crafting");
                // Skip the 2 s bar: next panel update finish the craft (vanilla DoCrafting).
                gui.m_craftTimer = 1000f;
                yield return Frames(4);
                var made = rig.SmokeCount() - smoke;
                var bonus = player.GetSkillFactor(station.m_craftingSkill) > 0f;
                c.Check(made == 2 || (bonus && made > 2), $"one craft made {made} Smoke Screens, expected 2");
                var after = new[]
                {
                    player.GetInventory().CountItems(resin.m_name, -1, false),
                    player.GetInventory().CountItems(coal.m_name, -1, false),
                    player.GetInventory().CountItems(scraps.m_name, -1, false),
                };
                c.Check(after[0] == before[0] && after[1] == before[1] && after[2] == before[2],
                    $"after the craft: Resin {before[0] + 2} -> {after[0]}, Coal {before[1] + 1} -> {after[1]}, Leather scraps "
                    + $"{before[2] + 1} -> {after[2]} (expected 2 + 1 + 1 used)");
            }
            gui.Hide();
            player.SetCraftingStation(null);
            yield return Frames(3);

            // T30. Odd recipe settings (names made new each run: each warning is said once per session).
            var tag = Time.frameCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Tap.Clear();
            ServerRules.TestRules = new AmbushRules { RecipeResources = $"Resin:2,McNoItem{tag}:1,Coal:x{tag}" };
            c.Check(recipe.m_enabled && recipe.m_resources.Length == 1 && recipe.m_resources[0].m_amount == 2,
                $"unknown material and bad amount: recipe is \"{RecipeText(recipe)}\", expected Resin:2 alone");
            c.Check(Tap.Logged(LogLevel.Warning, $"\"McNoItem{tag}\" is not an item in this game; it is skipped.") == 1
                    && Tap.Logged(LogLevel.Warning, $"\"Coal:x{tag}\" has no valid amount") == 1,
                $"unknown material and bad amount: expected one warning each, got: {Tap.Problems()}");
            ServerRules.TestRules = new AmbushRules { RecipeResources = $"McNoItem{tag}b:1" };
            c.Check(!recipe.m_enabled && Tap.Logged(LogLevel.Warning, "has no valid material") == 1,
                $"no valid material: recipe shown {recipe.m_enabled}, warnings: {Tap.Problems()}");
            ServerRules.TestRules = new AmbushRules { RecipeStation = "" };
            c.Check(recipe.m_enabled && recipe.m_craftingStation == null, "empty RecipeStation: not a hand craft");
            c.Check(OfferedAt(player, null, recipe), "empty RecipeStation: the Smoke Screen is not offered for crafting by hand (no station)");
            ServerRules.TestRules = new AmbushRules { RecipeStation = "McNoStation" + tag };
            c.Check(!recipe.m_enabled && Tap.Logged(LogLevel.Warning, $"\"McNoStation{tag}\" is not a crafting station") == 1,
                $"unknown station: recipe shown {recipe.m_enabled}, warnings: {Tap.Problems()}");
            ServerRules.TestRules = rules;
            c.Check(recipe.m_enabled && RecipeText(recipe) == "Resin:2,Coal:1,LeatherScraps:1 -> 2 at piece_workbench level 1",
                $"default settings back: recipe is \"{RecipeText(recipe)}\"");
            c.Check(Tap.Logged(LogLevel.Error, "") == 0, $"an error was logged: {Tap.Problems()}");
            c.Report();
        }
        finally
        {
            if (gui != null && InventoryGui.IsVisible())
            {
                gui.Hide();
            }
            if (owner != null)
            {
                owner.SetCraftingStation(null);
                owner.m_knownRecipes.Clear();
                owner.m_knownRecipes.UnionWith(knownRecipes);
                owner.m_knownMaterial.Clear();
                owner.m_knownMaterial.UnionWith(knownMaterial);
                owner.m_knownStations.Clear();
                foreach (var pair in knownStations)
                {
                    owner.m_knownStations[pair.Key] = pair.Value;
                }
            }
            rig.Restore();
        }
    }
}
#endif
