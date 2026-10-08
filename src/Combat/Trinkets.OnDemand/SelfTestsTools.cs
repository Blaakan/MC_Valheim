#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Debug build only. Tools of self tests (same class as SelfTests.cs): log listener, wait / swing / face helpers,
// message and effect readers. Nothing here run outside test but log listener (one cheap compare per log line).
internal static partial class SelfTests
{
    // Other MC mods test look at (soft: by GUID through shared registry, never their types).
    private const string TowerWallGuid = "MC.Combat.Shields.TowerWall";
    private const string DualWieldGuid = "MC.Combat.Weapons.DualWield";
    private const string StaysLoadedGuid = "MC.Combat.Crossbow.StaysLoaded";
    private const string HarpoonGuid = "MC.Farming.Harpoon.HooksTames";
    private const string MusicGuid = "MC.Exploration.Music.Instruments";

    // Vanilla names (checked in 1.0.16 game data, see TESTING.md Setup; Hammer, SpearFlint, StaffFireball and
    // BombOoze used by passing tests of Dual Wielding, Weapon Moveset and Creature Morale).
    private const string BronzeTrinket = "TrinketBronzeHealth";
    private const string IronTrinket = "TrinketIronHealth";
    private const string SwordName = "SwordIron";
    private const string KnifeName = "KnifeCopper";
    private const string ShieldName = "ShieldWood";
    private const string TowerShieldName = "ShieldGoldTower";
    private const string BoltName = "BoltBone";
    private const string BoarName = "Boar";
    private const string GreylingName = "Greyling";
    private const string HammerName = "Hammer";
    private const string SpearName = "SpearFlint";
    private const string HarpoonName = "SpearChitin";
    private const string BombName = "BombOoze";
    private const string StaffName = "StaffFireball";

    private static readonly WaitForFixedUpdate Fixed = new WaitForFixedUpdate();

    // ---------- log ----------

    // Me hear every line me logs (also Debug ones, whatever BepInEx writes to file), Unity warning
    // about missing "Flash" animator parameter, and error line of any other source that names my code
    // (exception that got out of mod: Unity logs it with stack, my namespace in it). Lines of tests
    // themselves ("[selftest] ...") left out. Log events can come from any thread: me lock, never throw.
    private sealed class LogCapture : ILogListener
    {
        internal struct Line
        {
            internal LogLevel Level;
            internal bool Mine;   // my log source (false = Unity line about Flash parameter, or error naming my code)
            internal string Text;
        }

        private const int Max = 4000;

        // Namespace of mod: in every stack frame of my code (patches, RPC handlers, tests).
        private static readonly string MyCode = typeof(Plugin).Namespace + ".";
        private readonly object _gate = new object();
        private readonly List<Line> _lines = new List<Line>();

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? (eventArgs.Data != null ? eventArgs.Data.ToString() : null);
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }
                var mine = eventArgs.Source != null && eventArgs.Source.SourceName == ModInfo.Name;
                if (mine)
                {
                    if (text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
                else if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) != 0)
                {
                    // Other source's error: kept only when my code is named in it.
                    if (text.IndexOf(MyCode, StringComparison.Ordinal) < 0)
                    {
                        return;
                    }
                }
                else if (text.IndexOf("Flash", StringComparison.Ordinal) < 0
                         || text.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }
                lock (_gate)
                {
                    if (_lines.Count < Max)
                    {
                        _lines.Add(new Line { Level = eventArgs.Level, Mine = mine, Text = text });
                    }
                }
            }
            catch (Exception)
            {
                // Me never throw inside logger.
            }
        }

        public void Dispose()
        {
        }

        internal int Mark()
        {
            lock (_gate)
            {
                return _lines.Count;
            }
        }

        internal List<Line> Since(int mark)
        {
            lock (_gate)
            {
                var from = Mathf.Clamp(mark, 0, _lines.Count);
                return _lines.GetRange(from, _lines.Count - from);
            }
        }
    }

    private static LogCapture _seen;

    // One listener for whole session (added once: BindConfig). Tests read it from mark.
    private static void EnsureLog()
    {
        if (_seen != null)
        {
            return;
        }
        _seen = new LogCapture();
        BepInEx.Logging.Logger.Listeners.Add(_seen);
    }

    private static int LogMark()
    {
        EnsureLog();
        return _seen.Mark();
    }

    // My lines since mark that hold text.
    private static List<string> LogLines(int mark, string contains)
    {
        EnsureLog();
        return _seen.Since(mark).Where(l => l.Mine && l.Text.IndexOf(contains, StringComparison.Ordinal) >= 0)
            .Select(l => l.Text).ToList();
    }

    private static int LogCount(int mark, string contains) => LogLines(mark, contains).Count;

    // Warnings and errors of me since mark (expected ones, by how they start, left out), every Unity
    // warning about missing Flash parameter, and every other source's error that names my code.
    private static List<string> LogProblems(int mark, params string[] expectedStarts)
    {
        EnsureLog();
        var problems = new List<string>();
        foreach (var line in _seen.Since(mark))
        {
            if (!line.Mine)
            {
                problems.Add(((line.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 ? "error naming the mod's code: " : "Unity: ")
                             + FirstLine(line.Text));
                continue;
            }
            if ((line.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0)
            {
                continue;
            }
            if (expectedStarts != null && expectedStarts.Any(e => line.Text.StartsWith(e, StringComparison.Ordinal)))
            {
                continue;
            }
            problems.Add(line.Level + ": " + FirstLine(line.Text));
        }
        return problems;
    }

    private static string FirstLine(string text)
    {
        var cut = text.IndexOf('\n');
        var line = cut > 0 ? text.Substring(0, cut).TrimEnd() : text;
        return line.Length > 200 ? line.Substring(0, 200) + "..." : line;
    }

    // "no warning or error from mod" check at end of test.
    private static void CheckCleanLog(Checks c, int mark, params string[] expectedStarts)
    {
        var problems = LogProblems(mark, expectedStarts);
        c.Check(problems.Count == 0,
            $"{problems.Count} warning or error line(s) from the mod during the test, first: "
            + (problems.Count > 0 ? problems[0] : ""));
    }

    // Debug line RangedBonus.Begin writes for shot (same format, built from shot record).
    private static string ShotLine(RangedBonus.Shot shot)
    {
        return "Ranged shot (" + shot.Skill + "): cycle " + shot.Nominal.ToString("0.##", CultureInfo.InvariantCulture)
               + " s, " + (float.IsPositiveInfinity(shot.SinceLast)
                   ? "first shot"
                   : shot.SinceLast.ToString("0.##", CultureInfo.InvariantCulture) + " s since the last shot")
               + " -> adrenaline per hit x" + shot.Factor.ToString("0.##", CultureInfo.InvariantCulture) + ".";
    }

    // ---------- me and others ----------

    private static Plugin Self() =>
        Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance as Plugin : null;

    // Another MC mod loaded and active now (shared registry: plain strings, no type of theirs).
    private static bool ModActive(string guid)
    {
        var view = FeatureRegistry.Find(guid);
        return view != null && view.Value.IsActive;
    }

    // ---------- waiting and driving ----------

    // Wait until condition holds or (real) seconds run out.
    private static IEnumerator Until(Func<bool> condition, float seconds)
    {
        var t = 0f;
        while (!condition() && t < seconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static IEnumerator FixedTicks(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return Fixed;
        }
    }

    private static void Put(Player p, Vector3 pos)
    {
        p.transform.position = pos;
        p.m_body.position = pos;
        p.m_body.linearVelocity = Vector3.zero;
    }

    // Body and view both along dir: swing, block or shot goes that way.
    private static void Face(Player p, Vector3 dir)
    {
        var rot = Quaternion.LookRotation(Flat(dir));
        p.transform.rotation = rot;
        p.m_body.rotation = rot;
        p.SetMouseLookForward();
        p.SetMouseLook(Vector2.zero);
    }

    private static bool Calm(Player p) =>
        !p.InAttack() && !p.InDodge() && !p.InMinorAction() && !p.IsStaggering() && p.IsOnGround();

    // One real attack with weapon in hand (vanilla StartAttack, what attack button calls), followed to end
    // of its animation: hit or miss happens inside. started[0] = game took attack.
    private static IEnumerator Swing(Player p, bool secondary, bool[] started)
    {
        started[0] = false;
        yield return Until(() => Calm(p), 4f);
        var t = 0f;
        while (t < 1.5f)
        {
            if (p.StartAttack(null, secondary))
            {
                started[0] = true;
                break;
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!started[0])
        {
            yield break;
        }
        yield return Until(() => p.InAttack(), 2f);
        yield return Until(() => !p.InAttack(), 5f);
        yield return Fixed;
    }

    // ---------- adrenaline ----------

    // What vanilla AddAdrenaline(v) call adds at bar's fill right now (world rate, gain curve, effects), before
    // full-bar rule. Losses (v below 0) added as they are.
    private static float Gain(Player p, float v) => GainAt(p, v, p.GetAdrenaline());

    // Same, for call made when bar was at <bar>.
    private static float GainAt(Player p, float v, float bar)
    {
        var max = p.GetMaxAdrenaline();
        if (v <= 0f || max <= 0f)
        {
            return v < 0f ? v : 0f;
        }
        var gain = v * Game.m_adrenalineRate * p.m_adrenalineGainMultiplier.Evaluate(bar / max);
        p.GetSEMan().ModifyAdrenaline(gain, ref gain);
        return gain;
    }

    // Hit on local player from creature, from front (side shield covers), as creature's attack
    // sends it: Character.Damage -> RPC_Damage -> block or not.
    private static void HitPlayerFront(Player p, Character attacker, bool blockable, float blunt = 1f)
    {
        var hit = new HitData();
        hit.m_damage.m_blunt = blunt;
        hit.m_point = p.GetCenterPoint() + p.transform.forward * 0.3f;
        hit.m_dir = -p.transform.forward;
        hit.m_blockable = blockable;
        if (attacker != null)
        {
            hit.SetAttacker(attacker);
        }
        p.Damage(hit);
    }

    private static Collider ColliderOf(Character c)
    {
        var own = c.GetComponent<Collider>();
        return own != null ? own : c.GetComponentInChildren<Collider>();
    }

    // ---------- what screen holds ----------

    // Top-left message line or its waiting queue holds this text (vanilla MessageHud).
    private static bool TopLeftHas(string text)
    {
        var hud = MessageHud.instance;
        if (hud == null || string.IsNullOrEmpty(text))
        {
            return false;
        }
        var want = Localization.instance != null ? Localization.instance.Localize(text) : text;
        foreach (var message in hud.m_msgQeue)
        {
            if (message != null && message.m_text == want)
            {
                return true;
            }
        }
        return hud.m_messageText != null && hud.m_messageText.text != null
               && hud.m_messageText.text.StartsWith(want, StringComparison.Ordinal);
    }

    // Effect shows in status effect row of HUD (vanilla list: icon and not hidden).
    private static bool HudShows(Player p, int hash)
    {
        var list = new List<StatusEffect>();
        p.GetSEMan().GetHUDStatusEffects(list);
        return list.Any(se => se != null && se.NameHash() == hash);
    }

    // Live networked objects made from this prefab (pop effect of bar...).
    private static int CountObjects(string prefabName)
    {
        var scene = ZNetScene.instance;
        if (scene == null || string.IsNullOrEmpty(prefabName))
        {
            return 0;
        }
        var count = 0;
        foreach (var view in scene.m_instances.Values)
        {
            if (view != null && view.gameObject.name.StartsWith(prefabName, StringComparison.Ordinal))
            {
                count++;
            }
        }
        return count;
    }

    private static string PopEffectName(Player p)
    {
        var list = p.m_adrenalinePopEffects != null ? p.m_adrenalinePopEffects.m_effectPrefabs : null;
        if (list == null)
        {
            return null;
        }
        foreach (var entry in list)
        {
            if (entry != null && entry.m_enabled && entry.m_prefab != null && entry.m_prefab.GetComponent<ZNetView>() != null)
            {
                return entry.m_prefab.name;
            }
        }
        return null;
    }

    // Tooltip of item as inventory shows it (raw text, before UI turns tokens into words).
    private static string TipOf(ItemDrop.ItemData item) =>
        ItemDrop.ItemData.GetTooltip(item, item.m_quality, false, Game.m_worldLevel, -1, false);

    private static ItemDrop.ItemData.SharedData PrefabShared(string prefab)
    {
        var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
        var drop = go != null ? go.GetComponent<ItemDrop>() : null;
        return drop != null ? drop.m_itemData.m_shared : null;
    }

    // Trinket by name, or first one game has when that name gone (other game version).
    private static ItemDrop.ItemData EquipNamedTrinket(Checks c, Rig rig, string prefab)
    {
        var name = PrefabShared(prefab) != null ? prefab : FindTrinket(c, false);
        if (name != prefab)
        {
            c.Note($"{prefab} not found: using {name ?? "nothing"}");
        }
        return name != null ? rig.EquipTrinket(name) : null;
    }
}
#endif
