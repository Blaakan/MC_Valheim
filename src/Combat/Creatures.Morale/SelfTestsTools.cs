#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.CreaturesMoraleMod;

// Debug build only. Tools shared by the self tests of SelfTests.cs, SelfTestsWorld*.cs and SelfTestsMp.cs: the mod's
// own log lines while a test run (BepInEx listener: it get every level, also Debug lines the log file drop), corner
// messages, name plates, real projectiles, turning the mod off and on through the framework, ownership give-away.
internal static partial class SelfTests
{
    // ================================================================ log

    // Me keep the lines this mod logged while a test record (Checks start and stop me). One listener for the whole
    // session, added at first use, never removed: turning the mod off inside a test (L01) must not lose its lines.
    private sealed class LogWatch : ILogListener
    {
        private const int MaxLines = 4000;

        private struct Line
        {
            internal LogLevel Level;
            internal string Text;
        }

        private static LogWatch _instance;
        private readonly object _gate = new object();
        private readonly List<Line> _lines = new List<Line>();
        private bool _recording;

        internal static LogWatch Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LogWatch();
                    BepInEx.Logging.Logger.Listeners.Add(_instance);
                }
                return _instance;
            }
        }

        internal void Begin()
        {
            lock (_gate)
            {
                _lines.Clear();
                _recording = true;
            }
        }

        internal void End()
        {
            lock (_gate)
            {
                _recording = false;
            }
        }

        // Place in the record: lines logged from now on have a higher index.
        internal int Mark()
        {
            lock (_gate)
            {
                return _lines.Count;
            }
        }

        // Lines from `from` on that hold `part` (and have one of `levels`).
        internal int Count(string part, int from = 0, LogLevel levels = LogLevel.All)
        {
            var count = 0;
            lock (_gate)
            {
                for (var i = Math.Max(from, 0); i < _lines.Count; i++)
                {
                    if ((_lines[i].Level & levels) != 0 && _lines[i].Text.IndexOf(part, StringComparison.Ordinal) >= 0)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        // Last line from `from` on that holds `part`, or "".
        internal string Last(string part, int from = 0)
        {
            lock (_gate)
            {
                for (var i = _lines.Count - 1; i >= Math.Max(from, 0); i--)
                {
                    if (_lines[i].Text.IndexOf(part, StringComparison.Ordinal) >= 0)
                    {
                        return _lines[i].Text;
                    }
                }
            }
            return "";
        }

        // Warning and error lines of the record that no step asked for (self test result lines left out).
        internal List<string> Problems(IList<string> expected)
        {
            var problems = new List<string>();
            lock (_gate)
            {
                foreach (var line in _lines)
                {
                    if ((line.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0
                        || line.Text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var asked = false;
                    for (var i = 0; i < expected.Count && !asked; i++)
                    {
                        asked = line.Text.IndexOf(expected[i], StringComparison.Ordinal) >= 0;
                    }
                    if (!asked)
                    {
                        problems.Add(line.Text.Length > 300 ? line.Text.Substring(0, 300) + "..." : line.Text);
                    }
                }
            }
            return problems;
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (!_recording || eventArgs == null || eventArgs.Source == null || eventArgs.Source.SourceName != ModInfo.Name)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? (eventArgs.Data != null ? eventArgs.Data.ToString() : null);
                if (text == null)
                {
                    return;
                }
                lock (_gate)
                {
                    if (_recording && _lines.Count < MaxLines)
                    {
                        _lines.Add(new Line { Level = eventArgs.Level, Text = text });
                    }
                }
            }
            catch
            {
                // Me never throw inside the logger.
            }
        }

        public void Dispose()
        {
            End();
        }
    }

    private static LogWatch Watch => LogWatch.Instance;

    // ================================================================ messages

    // How many times the game showed exactly this message (corner or center) so far: vanilla's own message log
    // (MessageHud.ShowMessage add every shown text, last 50). Count before and after = shown or not.
    private static int Shown(string text)
    {
        var hud = MessageHud.instance;
        if (hud == null)
        {
            return 0;
        }
        var count = 0;
        foreach (var line in hud.GetLog())
        {
            if (line == text)
            {
                count++;
            }
        }
        return count;
    }

    // Corner message (top left), not a center one: it waits in the corner queue or is the corner line on screen now
    // (MessageHud.ShowMessage put only TopLeft messages there; a center message goes straight to the center text).
    private static bool InCorner(string text)
    {
        var hud = MessageHud.instance;
        if (hud == null)
        {
            return false;
        }
        if (hud.currentMsg != null && hud.currentMsg.m_text == text)
        {
            return true;
        }
        foreach (var queued in hud.m_msgQeue)
        {
            if (queued != null && queued.m_text == text)
            {
                return true;
            }
        }
        return false;
    }

    private static string KillStepText(string token, int kills)
    {
        var loc = Localization.instance;
        return $"{(loc != null ? loc.Localize(token) : token)}: {kills} kills. They will be afraid of you sooner.";
    }

    // ================================================================ name plates

    private static EnemyHud.HudData PlateOf(Character ch)
    {
        var hud = EnemyHud.instance;
        if (hud == null || ch == null || !hud.m_huds.TryGetValue(ch, out var data) || data == null || data.m_gui == null)
        {
            return null;
        }
        return data;
    }

    // Me do what the crosshair on the creature do (hover timer 0 = plate shown 60 s) until the plate is on screen with
    // the alert icon as asked and the aware icon off.
    private static IEnumerator PlateShows(Character ch, bool alerted, float timeout, Action eachFrame, Waiter w, PlateRef plate)
    {
        yield return Until(() =>
        {
            plate.Data = PlateOf(ch);
            if (plate.Data == null)
            {
                return false;
            }
            plate.Data.m_hoverTimer = 0f;
            return IconsShow(plate.Data, alerted);
        }, timeout, eachFrame, w);
    }

    // Every text on the plate. Vanilla: the creature's name only (mounts: numbers).
    private static string PlateWords(EnemyHud.HudData data)
    {
        var sb = new StringBuilder();
        if (data == null || data.m_gui == null)
        {
            return "";
        }
        foreach (var text in data.m_gui.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            if (text != null && !string.IsNullOrEmpty(text.text))
            {
                sb.Append(sb.Length > 0 ? " | " : "").Append(text.text);
            }
        }
        return sb.ToString();
    }

    // No word of the mod's own on the plate, and its name line is the creature's vanilla name.
    private static bool PlateClean(EnemyHud.HudData data, Character ch)
    {
        if (data == null || data.m_name == null || ch == null || Localization.instance == null)
        {
            return false;
        }
        var words = PlateWords(data);
        foreach (var word in new[] { "afraid", "routed", "shaken", "morale" })
        {
            if (words.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
        }
        return data.m_name.text == Localization.instance.Localize(ch.GetHoverName());
    }

    // ================================================================ Encyclopedia (X02)

    // MC Encyclopedia keeps the creatures a character has met in the character's own data (its OwnRecords: version
    // line, then one name token per line), and marks one as met when the game shows its name plate. Me only read that
    // text, and take one token out before a step so "met" is news again (put back if the step does not bring it back).
    private const string EncyclopediaGuid = "MC.Exploration.Compendium.Encyclopedia";
    private const string EncyclopediaSeenKey = EncyclopediaGuid + ".Seen";

    private static bool EncyclopediaOn()
    {
        var view = FeatureRegistry.Find(EncyclopediaGuid);
        return view != null && view.Value.IsActive;
    }

    private static bool MetInEncyclopedia(Player player, string token)
    {
        if (player == null || player.m_customData == null || !player.m_customData.TryGetValue(EncyclopediaSeenKey, out var raw) || raw == null)
        {
            return false;
        }
        return Array.IndexOf(raw.Replace("\r", "").Split('\n'), token) >= 1;
    }

    // The record as it was (null = none), with the token taken out of the stored one.
    private static string ForgetInEncyclopedia(Player player, string token)
    {
        if (player == null || player.m_customData == null || !player.m_customData.TryGetValue(EncyclopediaSeenKey, out var raw) || raw == null)
        {
            return null;
        }
        var lines = new List<string>(raw.Replace("\r", "").Split('\n'));
        if (lines.RemoveAll(line => line == token) > 0)
        {
            player.m_customData[EncyclopediaSeenKey] = string.Join("\n", lines.ToArray());
        }
        return raw;
    }

    // ================================================================ creatures

    private static Vector3 _meadowsPoint;

    // TESTING items say "in the Meadows": a spawned creature count as a creature of the biome it spawned in. Test spot
    // not in the Meadows (rare: the player start there) = me move its spawn point to a real Meadows point, like
    // SpawnBiomeSteps, and say so.
    private static void AsMeadows(Stage s, MonsterAI ai)
    {
        if (ai == null || ai.m_nview == null || !ai.m_nview.IsValid())
        {
            return;
        }
        var state = CreatureState.Get(ai);
        state.ForgetFacts();
        state.EnsureFacts(ServerRules.Current);
        if (state.SpawnLevel == 1)
        {
            return;
        }
        if (_meadowsPoint == Vector3.zero)
        {
            _meadowsPoint = BiomePoint(Heightmap.Biome.Meadows);
        }
        if (_meadowsPoint == Vector3.zero)
        {
            SelfTest.Note(s.Test, $"{ai.m_character.m_name} spawned in the {Biomes.Name(state.SpawnLevel)} and no Meadows point was found: its rank is not a Meadows rank");
            return;
        }
        ai.m_spawnPoint = _meadowsPoint;
        ai.m_nview.GetZDO().Set(ZDOVars.s_spawnPoint, _meadowsPoint);
        state.ForgetSpawnLevel();
        state.EnsureFacts(ServerRules.Current);
        SelfTest.Note(s.Test, $"the test spot is not in the Meadows: {ai.m_character.m_name} given a spawn point in the Meadows ({_meadowsPoint})");
    }

    // Spawn + Meadows spawn point. Null (with a failed check) when the prefab is missing.
    private static MonsterAI SpawnMeadows(Stage s, Checks c, string prefab, Vector3 pos, Vector3 facing)
    {
        var ai = s.Spawn(prefab, pos, facing);
        if (!c.Check(ai != null && ai.m_character != null, $"could not spawn {prefab}"))
        {
            return null;
        }
        AsMeadows(s, ai);
        return ai;
    }

    // Health far above what a few hits take: creature that must live through a fight.
    private static void Tough(Character ch)
    {
        if (ch != null)
        {
            ch.SetMaxHealth(100000f);
            ch.SetHealth(100000f);
        }
    }

    private static void HitWith(Character victim, Player attacker, Action<HitData> damage, float backstab)
    {
        var hit = new HitData();
        damage(hit);
        hit.m_point = victim.GetCenterPoint();
        var dir = victim.transform.position - attacker.transform.position;
        hit.m_dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        hit.m_backstabBonus = backstab;
        hit.SetAttacker(attacker);
        victim.Damage(hit);
    }

    private static bool Alive(MonsterAI ai) => ai != null && ai.m_character != null && !ai.m_character.IsDead();

    private static int ProvokerCountOf(MonsterAI ai) =>
        ai != null && ai.m_nview != null && ai.m_nview.IsValid()
            ? CreatureKeys.ProvokerCount(ai.m_nview.GetZDO().GetByteArray(CreatureKeys.Provokers))
            : 0;

    private static double SecondsLeft(long until) => (until - Attitudes.Now()) / (double)TimeSpan.TicksPerSecond;

    // ================================================================ items and projectiles

    private static ItemDrop.ItemData ItemOf(string prefabName)
    {
        var db = ObjectDB.instance;
        var prefab = db != null ? db.GetItemPrefab(prefabName) : null;
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        return drop != null ? drop.m_itemData : null;
    }

    // Real projectile of a weapon as vanilla Attack fire it (Projectile.Setup, the player as owner), from `from` with
    // `velocity`. The first ray from the owner's body is turned off: me fire it from where the test want it.
    // item null = a thrown weapon does not drop itself where it lands.
    private static Projectile Fire(Stage s, GameObject prefab, Vector3 from, Vector3 velocity, float hitNoise, HitData hit,
        ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        if (prefab == null)
        {
            return null;
        }
        var go = Object.Instantiate(prefab, from, Quaternion.LookRotation(velocity.normalized));
        s.Track(go);
        var projectile = go.GetComponent<Projectile>();
        if (projectile == null)
        {
            return null;
        }
        projectile.m_doOwnerRaytest = false;
        projectile.Setup(s.Player, velocity, hitNoise, hit, item, ammo);
        return projectile;
    }

    private static HitData PlayerHit(Player player, HitData.DamageTypes damage, Skills.SkillType skill)
    {
        var hit = new HitData();
        hit.m_damage = damage;
        hit.m_skill = skill;
        hit.SetAttacker(player);
        return hit;
    }

    // Area effects left by a projectile (bomb cloud) near a point: gone now, so the next step starts on clean ground.
    private static int ClearAreas(Vector3 center, float radius)
    {
        var scene = ZNetScene.instance;
        var count = 0;
        foreach (var aoe in Object.FindObjectsByType<Aoe>(FindObjectsSortMode.None))
        {
            if (aoe == null || scene == null || (aoe.transform.position - center).sqrMagnitude > radius * radius)
            {
                continue;
            }
            scene.Destroy(aoe.gameObject);
            count++;
        }
        return count;
    }

    // ================================================================ mod on / off, ownership

    // Like the player turning the mod off or on (ModPlugin.Refresh -> OnDeactivated / OnActivated, patches removed or
    // applied), but through the Debug blocker: Enabled is never written.
    private static void SetBlocked(bool blocked)
    {
        Plugin.TestBlocked = blocked;
        FeatureRegistry.RefreshAll();
    }

    private static string ModStateNow()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view != null ? view.Value.State : "not registered";
    }

    // Another game own the creature for a while (ZDO owner = an id that is not this game), then this game take it
    // back: CreatureCheck see the gap and reset its owner memory (OnGained), like a real take-over.
    private static IEnumerator GiveAway(string test, string label, float seconds, Action eachFrame, params MonsterAI[] creatures)
    {
        var other = ZDOMan.GetSessionID() + 7919L;
        foreach (var ai in creatures)
        {
            if (ai != null && ai.m_nview != null && ai.m_nview.IsValid())
            {
                CreatureState.Get(ai).CorneredTime = 500f; // owner memory marker: OnGained put it back to 0
                ai.m_nview.GetZDO().SetOwner(other);
            }
        }
        var handedBack = 0;
        var end = Time.time + seconds;
        while (Time.time < end)
        {
            eachFrame?.Invoke();
            // The local server hands an orphan back by itself every 2 s (ZDOMan.ReleaseNearbyZDOS): me give it away
            // again, so the gap is long enough for the take-over to be seen.
            foreach (var ai in creatures)
            {
                if (ai != null && ai.m_nview != null && ai.m_nview.IsValid() && ai.m_nview.IsOwner())
                {
                    handedBack++;
                    ai.m_nview.GetZDO().SetOwner(other);
                }
            }
            yield return null;
        }
        if (handedBack > 0)
        {
            SelfTest.Note(test, $"{label}: the game handed a creature back {handedBack} time(s) during the {F(seconds)} s; given away again each time");
        }
        foreach (var ai in creatures)
        {
            if (ai != null && ai.m_nview != null && ai.m_nview.IsValid())
            {
                ai.m_nview.ClaimOwnership();
            }
        }
    }

    private static bool Gained(MonsterAI ai) =>
        ai != null && CreatureState.TryGet(ai, out var state) && state.CorneredTime < 400f;

    private static string Hex(byte[] bytes)
    {
        if (bytes == null)
        {
            return "";
        }
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private static readonly FieldInfo SneakLogField =
        typeof(Patches.BaseAIPatches).GetField("_nextSneakLog", BindingFlags.Static | BindingFlags.NonPublic);

    // The slow-sneak Debug line is logged at most every 10 s: me open the gate so the next call can log.
    private static bool OpenSneakLog()
    {
        if (SneakLogField == null)
        {
            return false;
        }
        SneakLogField.SetValue(null, 0f);
        return true;
    }
}
#endif
