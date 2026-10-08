using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
#endif

namespace MC.Exploration.StatsPerCreatureMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod Stats.PerCreature) and multiplayer probe (tools/Test-Multiplayer.ps1). Files: SelfTests.cs (this: list, rig,
// helpers), SelfTests.Kills.cs, SelfTests.Tames.cs, SelfTests.Page.cs, SelfTests.Multiplayer.cs.
//   percreature.data         prefab values the kill types hang on (bare hands name, claws, staff, butcher knife)
//   percreature.page         section text on the real Compendium page with injected kills and tames: order, rows at the
//                            limits, grey line, other names, vanilla text after
//   percreature.scroll       entry picked like the gamepad pick it: section on top, right scroll bar on and scrolling
//   percreature.settings     SortBy / ShowWeaponTypes (in-memory override) change the page at next open
//   percreature.kills        hit by weapon skill (injected HitData) -> melee / ranged / magic / unarmed / other bucket,
//                            kill sent by another game (routed RPC), API = profile numbers = page numbers
//   percreature.club         real Club swing kill = melee
//   percreature.fists        real bare-hands kill = unarmed, real Fenris claws kill = melee
//   percreature.butcher      tame boar killed with real Butcher knife = melee kill, tame count stay
//   percreature.bow          real full-draw Bow shot kill = ranged
//   percreature.mixed        real Club kill = melee; real Club hit then real Bow kill = other
//   percreature.tames        tame near = message + count, again = no count, far = owner count no message, hidden HUD
//                            = no message but count, creature that start tame = no count
//   percreature.tame-paths   vanilla taming timer (TamingUpdate) near and far, tame message from another game (RPC),
//                            other center/top-left messages never count, muted MessageHud still count
//   percreature.feed         real feeding: boar eat a dropped berry and get tame by itself, near and far
//   percreature.store        new character page, stored format, start date (also Thai calendar), API, save data
//   percreature.format       stored value with junk / newer version: read safe, rewrite clean, newer never rewritten
//   percreature.toggle       feature off live (Debug block hook, Enabled untouched): vanilla page byte for byte, no
//                            tame count, kills still read; on again: section back
//   percreature.compat       other mod move Compendium entries / drop the stats entry: section follow, no error
//   percreature.bug.respawn-gap  REAL BUG, fail until mod fixed: tame while game has no character at all (respawn
//                            wait) must be credited after. Alone in own test: no other item wait on it
//   percreature.log          no error line of this mod since it went on
//   percreature.mp.session   client on dedicated server (server never load this Client mod): kill, tame near, far,
//                            tame message RPC
//   percreature.mp.config    real config entries (throwaway config of the multiplayer run): SortBy, ShowWeaponTypes,
//                            Enabled off and on
// Me never set a ConfigEntry in single-player tests: Plugin.TestDisplay / Plugin.TestForceOff only. Rig put back kill
// stats, tame data, skills, known items, hands, look, HUD and destroy what it spawn.
internal static partial class SelfTests
{
    private const string DataName = "percreature.data";
    private const string PageName = "percreature.page";
    private const string ScrollName = "percreature.scroll";
    private const string SettingsName = "percreature.settings";
    private const string KillsName = "percreature.kills";
    private const string ClubName = "percreature.club";
    private const string FistsName = "percreature.fists";
    private const string ButcherName = "percreature.butcher";
    private const string BowName = "percreature.bow";
    private const string MixedName = "percreature.mixed";
    private const string TamesName = "percreature.tames";
    private const string PathsName = "percreature.tame-paths";
    private const string FeedName = "percreature.feed";
    private const string StoreName = "percreature.store";
    private const string FormatName = "percreature.format";
    private const string ToggleName = "percreature.toggle";
    private const string CompatName = "percreature.compat";
    private const string GapName = "percreature.bug.respawn-gap";
    private const string LogName = "percreature.log";
    private const string MpSessionName = "percreature.mp.session";
    private const string MpConfigName = "percreature.mp.config";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        // Log watch first: it also see this activation's own lines. Stay on until game quit.
        InstallWatch();
        SelfTest.Register(DataName, RunData);
        SelfTest.Register(PageName, RunPage);
        SelfTest.Register(ScrollName, RunScroll);
        SelfTest.Register(SettingsName, RunSettings);
        SelfTest.Register(KillsName, RunKills);
        SelfTest.Register(ClubName, RunClub);
        SelfTest.Register(FistsName, RunFists);
        SelfTest.Register(ButcherName, RunButcher);
        SelfTest.Register(BowName, RunBow);
        SelfTest.Register(MixedName, RunMixed);
        SelfTest.Register(TamesName, RunTames);
        SelfTest.Register(PathsName, RunTamePaths);
        SelfTest.Register(FeedName, RunFeed);
        SelfTest.Register(StoreName, RunStore);
        SelfTest.Register(FormatName, RunFormat);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(CompatName, RunCompat);
        SelfTest.Register(GapName, RunRespawnGap);
        // Last: it look back on every test above.
        SelfTest.Register(LogName, RunLog);
        SelfTest.RegisterMultiplayer(MpSessionName, SelfTest.Modded, RunMpSession);
        SelfTest.RegisterMultiplayer(MpConfigName, SelfTest.Modded, RunMpConfig);
#endif
    }

    // Me no touch Plugin.TestForceOff / TestDisplay here: percreature.toggle turn feature off on purpose and this run
    // then (OnDeactivated). Each test put its own hooks back in finally (Rig.End).
    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        foreach (var name in new[]
                 {
                     DataName, PageName, ScrollName, SettingsName, KillsName, ClubName, FistsName, ButcherName, BowName, MixedName,
                     TamesName, PathsName, FeedName, StoreName, FormatName, ToggleName, CompatName, GapName, LogName,
                 })
        {
            SelfTest.Unregister(name);
        }
        SelfTest.UnregisterMultiplayer(MpSessionName);
        SelfTest.UnregisterMultiplayer(MpConfigName);
#endif
    }

#if DEBUG
    // Same text StatsSection write. Me spell it again here on purpose: test say what player must read.
    private const string Header = "<color=orange>Creatures</color>";
    private const string GreyOpen = "<size=85%><color=#B0B0B0>";
    private const string GreyClose = "</color></size>";
    private const string OtherNamesLine = GreyOpen + "Other names (older PvP kills, or creatures without a translation key):" + GreyClose;
    private const string NothingRow = "Nothing killed or tamed yet.";
    private const string Indent = "    ";
    private const string TamedSuffix = " $hud_tamedone";

    // Vanilla Tameable.Tame tell closest player within this many metres.
    private const float MessageRange = 30f;

    // Center text me put before a tame: still there after = game showed no message.
    private const string NoMessage = " ";

    private static readonly PlayerStatType[] TouchedStats =
    {
        PlayerStatType.EnemyHits, PlayerStatType.EnemyKills, PlayerStatType.EnemyKillsLastHits,
        PlayerStatType.CreatureTamed, PlayerStatType.ArrowsShot, PlayerStatType.TamedPetting,
        PlayerStatType.TamedCommand, PlayerStatType.ItemsPickedUp,
    };

    private static string GreyLine(string since) =>
        GreyOpen + "Kills: the game's own count for this character, in every world. Tames: counted while this mod is on (since "
        + since + ")." + GreyClose;

    // ---------- checks ----------

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
            if (_failures.Count == 0 && _count > 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK{extra}");
            }
            else if (_failures.Count == 0)
            {
                SelfTest.Fail(_name, "no check ran");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    private sealed class Waiter
    {
        internal bool Met;
        internal float Took;
    }

    private static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    // Stored value on one line, like the mod's own log line show it.
    private static string Esc(string raw) => raw == null ? "(none)" : raw.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    private static IEnumerator Until(Func<bool> condition, float timeout, Action eachFrame, Waiter w)
    {
        var start = Time.time;
        w.Met = false;
        w.Took = 0f;
        while (true)
        {
            eachFrame?.Invoke();
            if (condition())
            {
                w.Met = true;
                w.Took = Time.time - start;
                yield break;
            }
            if (Time.time - start >= timeout)
            {
                w.Took = Time.time - start;
                yield break;
            }
            yield return null;
        }
    }

    private static IEnumerator Wait(float seconds, Action eachFrame)
    {
        var end = Time.time + seconds;
        while (Time.time < end)
        {
            eachFrame?.Invoke();
            yield return null;
        }
    }

    private static Plugin FindPlugin() =>
        Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance as Plugin : null;

    // ---------- log watch ----------

    // Me listen to BepInEx log (every level, also Debug that the disk log may drop): this mod's own lines, plus error
    // lines of any source that name this mod. "[selftest]" lines never count (a FAIL is logged as error).
    private sealed class LogWatch : ILogListener
    {
        private struct Line
        {
            internal int Seq;
            internal LogLevel Level;
            internal string Text;
        }

        private readonly object _gate = new object();
        private readonly List<Line> _lines = new List<Line>();
        private readonly List<string> _errors = new List<string>();
        private int _seq;
        private int _seen;

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                if (text == null || text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    return;
                }
                var own = eventArgs.Source != null && eventArgs.Source.SourceName == ModInfo.Name;
                var error = (eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) != 0;
                if (!own && !(error && NamesThisMod(text)))
                {
                    return;
                }
                lock (_gate)
                {
                    _seen++;
                    if (own)
                    {
                        _lines.Add(new Line { Seq = ++_seq, Level = eventArgs.Level, Text = text });
                        if (_lines.Count > 3000)
                        {
                            _lines.RemoveRange(0, 1000);
                        }
                    }
                    if (error && _errors.Count < 50)
                    {
                        var cut = text.IndexOf('\n');
                        _errors.Add((eventArgs.Source != null ? eventArgs.Source.SourceName : "?") + ": "
                                    + (cut > 0 ? text.Substring(0, cut).TrimEnd() : text));
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

        private static bool NamesThisMod(string text) =>
            text.IndexOf(ModInfo.Guid, StringComparison.Ordinal) >= 0
            || text.IndexOf("StatsPerCreatureMod", StringComparison.Ordinal) >= 0
            || text.IndexOf(ModInfo.Name, StringComparison.Ordinal) >= 0;

        internal int Mark()
        {
            lock (_gate)
            {
                return _seq;
            }
        }

        internal int Seen
        {
            get
            {
                lock (_gate)
                {
                    return _seen;
                }
            }
        }

        // This mod's lines after the mark, of these levels, that hold this text.
        internal List<string> Since(int mark, LogLevel levels, string contains)
        {
            var found = new List<string>();
            lock (_gate)
            {
                foreach (var line in _lines)
                {
                    if (line.Seq > mark && (line.Level & levels) != 0
                        && (contains == null || line.Text.IndexOf(contains, StringComparison.Ordinal) >= 0))
                    {
                        found.Add(line.Text);
                    }
                }
            }
            return found;
        }

        internal List<string> Errors()
        {
            lock (_gate)
            {
                return new List<string>(_errors);
            }
        }
    }

    private static LogWatch _watch;

    private static void InstallWatch()
    {
        if (_watch != null)
        {
            return;
        }
        _watch = new LogWatch();
        BepInEx.Logging.Logger.Listeners.Add(_watch);
    }

    private static List<string> TameLines(int mark) => _watch.Since(mark, LogLevel.Debug, "Tame counted: ");

    // Exact Debug line CounterStore.AddTame write for this count.
    private static string TameLine(string name, string via, string storedRaw) =>
        $"Tame counted: {name} ({via}). Stored {CreatureCounts.TamesDataKey} = {storedRaw.Replace("\n", "\\n").Replace("\t", "\\t")}";

    // ---------- kill numbers ----------

    private readonly struct Tally
    {
        internal readonly int Total;
        internal readonly int Melee;
        internal readonly int Ranged;
        internal readonly int Magic;
        internal readonly int Unarmed;
        internal readonly int Other;

        private Tally(CreatureCounts.KillBreakdown b)
        {
            Total = b.Total;
            Melee = b.Melee;
            Ranged = b.Ranged;
            Magic = b.Magic;
            Unarmed = b.Unarmed;
            Other = b.Other;
        }

        // Through the public API on purpose (page and other mods read there).
        internal static Tally Of(string name) => new Tally(CreatureCounts.GetKillBreakdown(name));

        internal bool Grew(Tally before, int total, int melee, int ranged, int magic, int unarmed, int other) =>
            Total - before.Total == total && Melee - before.Melee == melee && Ranged - before.Ranged == ranged
            && Magic - before.Magic == magic && Unarmed - before.Unarmed == unarmed && Other - before.Other == other;

        public override string ToString() =>
            $"total {Total}, melee {Melee}, ranged {Ranged}, magic {Magic}, unarmed {Unarmed}, other {Other}";
    }

    private static string Loc(string token) => Localization.instance.Localize(token);

    // ---------- rig ----------

    // Me = everything one test may change on the character and the world. End (finally, no yield) put all back:
    // kill stats and pickup stats of every profile slot, tame data, skills, known items, hands, given items, look,
    // place, controls, ghost mode, hidden HUD, center message text, open page, test hooks; spawned objects destroyed.
    private sealed class Rig
    {
        internal readonly string Test;
        internal readonly Player P;
        internal readonly PlayerProfile Profile;
        internal readonly Inventory Inv;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<ItemDrop.ItemData> _given = new List<ItemDrop.ItemData>();
        private readonly Dictionary<string, int> _stackBefore = new Dictionary<string, int>();

        private readonly Dictionary<string, float>[][] _enemy;
        private readonly Dictionary<string, float>[] _pickups;
        private readonly float?[][] _stats;
        private readonly bool _hadTames;
        private readonly string _tames;
        private readonly bool _hadSince;
        private readonly string _since;
        private readonly List<KeyValuePair<Skills.SkillType, Vector2>> _skills = new List<KeyValuePair<Skills.SkillType, Vector2>>();
        private readonly HashSet<string> _knownMaterial;
        private readonly HashSet<string> _knownRecipes;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly ItemDrop.ItemData _hiddenRight;
        private readonly ItemDrop.ItemData _hiddenLeft;
        private readonly ItemDrop.ItemData _ammo;
        private readonly Vector3 _pos;
        private readonly Quaternion _rot;
        private readonly Quaternion _lookYaw;
        private readonly float _lookPitch;
        private readonly bool _ghost;
        private readonly bool _hudHidden;
        private readonly string _centerText;
        private PlayerController _controller;
        private bool _controllerWasEnabled;
        private bool _moved;
        private bool _handsTouched;
        private bool _ended;

        private Rig(string test, Player player, PlayerProfile profile)
        {
            Test = test;
            P = player;
            Profile = profile;
            Inv = player.GetInventory();

            var slots = profile.m_playerStats;
            _enemy = new Dictionary<string, float>[slots.Length][];
            _pickups = new Dictionary<string, float>[slots.Length];
            _stats = new float?[slots.Length][];
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    continue;
                }
                var buckets = slots[i].m_enemyStats;
                _enemy[i] = new Dictionary<string, float>[buckets.Length];
                for (var j = 0; j < buckets.Length; j++)
                {
                    _enemy[i][j] = buckets[j] != null ? new Dictionary<string, float>(buckets[j]) : null;
                }
                _pickups[i] = new Dictionary<string, float>(slots[i].m_itemPickupStats);
                _stats[i] = new float?[TouchedStats.Length];
                for (var k = 0; k < TouchedStats.Length; k++)
                {
                    _stats[i][k] = slots[i].m_stats.TryGetValue(TouchedStats[k], out var value) ? value : (float?)null;
                }
            }

            _hadTames = player.m_customData.TryGetValue(CreatureCounts.TamesDataKey, out _tames);
            _hadSince = player.m_customData.TryGetValue(CreatureCounts.CountingSinceDataKey, out _since);
            foreach (var pair in player.m_skills.m_skillData)
            {
                _skills.Add(new KeyValuePair<Skills.SkillType, Vector2>(pair.Key, new Vector2(pair.Value.m_level, pair.Value.m_accumulator)));
            }
            _knownMaterial = new HashSet<string>(player.m_knownMaterial);
            _knownRecipes = new HashSet<string>(player.m_knownRecipes);
            _right = player.m_rightItem;
            _left = player.m_leftItem;
            _hiddenRight = player.m_hiddenRightItem;
            _hiddenLeft = player.m_hiddenLeftItem;
            _ammo = player.m_ammoItem;
            _pos = player.transform.position;
            _rot = player.transform.rotation;
            _lookYaw = player.m_lookYaw;
            _lookPitch = player.m_lookPitch;
            _ghost = player.InGhostMode();
            _hudHidden = Hud.instance != null && Hud.instance.m_userHidden;
            var hud = MessageHud.instance;
            _centerText = hud != null && hud.m_messageCenterText != null ? hud.m_messageCenterText.text : null;
        }

        internal static Rig Begin(string test)
        {
            var player = Player.m_localPlayer;
            var game = Game.instance;
            var profile = game != null ? game.GetPlayerProfile() : null;
            if (player == null || profile == null || ZNet.instance == null || ZNetScene.instance == null
                || ZoneSystem.instance == null || ObjectDB.instance == null || Localization.instance == null
                || player.m_nview == null || !player.m_nview.IsValid() || player.m_customData == null
                || profile.m_playerStats == null || profile.m_playerStats.Length == 0 || profile.m_playerStats[0] == null
                || _watch == null)
            {
                SelfTest.Fail(test, "no world, no local player or no player profile");
                return null;
            }
            // Page same for every tester: default display, whatever the config file say (test may set other).
            Plugin.TestDisplay = (SortOrder.MostKilled, true);
            return new Rig(test, player, profile);
        }

        // Lifetime kill dictionaries (index = KillModifiers), the ones the mod read.
        internal Dictionary<string, float>[] Kills => Profile.m_playerStats[0].m_enemyStats;

        internal float Stat(PlayerStatType type) =>
            Profile.m_playerStats[0].m_stats.TryGetValue(type, out var value) ? value : 0f;

        // Lifetime kills gone (saved copy come back at End): page of this test stand alone. Only for tests that
        // credit no kill: other mods (MC Creature Morale) read this table again at each credited kill.
        internal void ClearKills()
        {
            foreach (var bucket in Kills)
            {
                bucket?.Clear();
            }
        }

        // Lifetime kills of these creatures gone (saved copy come back at End), rest of the table untouched: their
        // page rows then read exactly what this test does.
        internal void ForgetKills(params string[] names)
        {
            foreach (var bucket in Kills)
            {
                if (bucket == null)
                {
                    continue;
                }
                foreach (var name in names)
                {
                    if (name != null)
                    {
                        bucket.Remove(name);
                    }
                }
            }
        }

        internal void SetKills(string name, float total, float melee = 0f, float ranged = 0f, float magic = 0f, float unarmed = 0f)
        {
            Kills[(int)KillModifiers.MixedAndTotal][name] = total;
            Put(KillModifiers.Melee, name, melee);
            Put(KillModifiers.Ranged, name, ranged);
            Put(KillModifiers.Magic, name, magic);
            Put(KillModifiers.Unarmed, name, unarmed);
        }

        private void Put(KillModifiers bucket, string name, float value)
        {
            if (value > 0f)
            {
                Kills[(int)bucket][name] = value;
            }
            else
            {
                Kills[(int)bucket].Remove(name);
            }
        }

        // Stored tames = this text (new string object each time: parse cache go by reference), or none.
        internal void SetTames(string raw)
        {
            if (raw == null)
            {
                P.m_customData.Remove(CreatureCounts.TamesDataKey);
            }
            else
            {
                P.m_customData[CreatureCounts.TamesDataKey] = new string(raw.ToCharArray());
            }
            CounterStore.ClearCache();
        }

        internal string RawTames => P.m_customData.TryGetValue(CreatureCounts.TamesDataKey, out var raw) ? raw : null;

        internal string RawSince => P.m_customData.TryGetValue(CreatureCounts.CountingSinceDataKey, out var raw) ? raw : null;

        // Creature at spot, turned to the player. frozen = AI off (no walk, no target, no regen); kill leave nothing.
        internal Character Spawn(string prefabName, Vector3 pos, bool frozen)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                SelfTest.Note(Test, $"prefab {prefabName} not found");
                return null;
            }
            var facing = P.transform.position - pos;
            facing.y = 0f;
            var rot = facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing.normalized) : Quaternion.identity;
            var go = Object.Instantiate(prefab, pos + Vector3.up * 0.1f, rot);
            _spawned.Add(go);
            var character = go.GetComponent<Character>();
            if (character == null)
            {
                SelfTest.Note(Test, $"{prefabName} has no Character");
                return null;
            }
            var ai = go.GetComponent<BaseAI>();
            if (ai != null && frozen)
            {
                ai.enabled = false;
            }
            var drop = go.GetComponent<CharacterDrop>();
            if (drop != null)
            {
                drop.SetDropsEnabled(false);
            }
            // No ragdoll, no puff: only this instance (prefab keep its own list).
            character.m_deathEffects = new EffectList();
            return character;
        }

        internal GameObject SpawnObject(GameObject prefab, Vector3 pos)
        {
            var go = Object.Instantiate(prefab, pos, Quaternion.identity);
            _spawned.Add(go);
            return go;
        }

        internal void Destroy(Component c)
        {
            if (c == null)
            {
                return;
            }
            var go = c.gameObject;
            _spawned.Remove(go);
            DestroyObject(go);
        }

        private static void DestroyObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }
            var view = go.GetComponent<ZNetView>();
            if (ZNetScene.instance != null && view != null && view.IsValid())
            {
                ZNetScene.instance.Destroy(go);
            }
            else
            {
                Object.Destroy(go);
            }
        }

        // New item in the inventory. End take it back (stack items: back to the count before).
        internal ItemDrop.ItemData Give(string prefabName, int stack = 1)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                SelfTest.Note(Test, $"item {prefabName} not found");
                return null;
            }
            var shared = drop.m_itemData.m_shared;
            var stacks = shared.m_maxStackSize > 1;
            if (stacks && !_stackBefore.ContainsKey(shared.m_name))
            {
                _stackBefore[shared.m_name] = Inv.CountItems(shared.m_name);
            }
            var item = Inv.AddItem(prefabName, stack, 1, 0, 0L, "", false);
            if (item != null && !stacks)
            {
                _given.Add(item);
            }
            return item;
        }

        internal bool Equip(ItemDrop.ItemData item)
        {
            _handsTouched = true;
            return item != null && P.EquipItem(item, false);
        }

        internal void EmptyHands()
        {
            _handsTouched = true;
            if (P.m_rightItem != null)
            {
                P.UnequipItem(P.m_rightItem, false);
            }
            if (P.m_leftItem != null)
            {
                P.UnequipItem(P.m_leftItem, false);
            }
        }

        // Me drive the player (no keyboard or mouse in between).
        internal void TakeControls()
        {
            _moved = true;
            if (_controller == null)
            {
                _controller = P.GetComponent<PlayerController>();
                if (_controller != null)
                {
                    _controllerWasEnabled = _controller.enabled;
                    _controller.enabled = false;
                }
            }
            Drive(false);
        }

        internal void Drive(bool attackHold) =>
            P.SetControls(Vector3.zero, false, attackHold, false, false, false, false, false, false, false, false);

        // Player stay on its spot, body turned to this point.
        internal void HoldFacing(Vector3 point)
        {
            _moved = true;
            var flat = point - _pos;
            flat.y = 0f;
            var rot = flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flat.normalized) : P.transform.rotation;
            P.transform.position = _pos;
            P.transform.rotation = rot;
            var body = P.m_body;
            if (body != null)
            {
                body.position = _pos;
                body.rotation = rot;
                body.linearVelocity = Vector3.zero;
            }
        }

        // Eyes look along from -> at (vanilla attack aim = eye forward).
        internal void Aim(Vector3 from, Vector3 at)
        {
            _moved = true;
            var dir = at - from;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }
            dir.Normalize();
            var flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude > 0.000001f)
            {
                P.m_lookYaw = Quaternion.LookRotation(flat.normalized);
            }
            // Eye rotation = yaw * Euler(pitch, 0, 0): plus pitch look down.
            P.m_lookPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, -89f, 89f);
            P.UpdateEyeRotation();
            P.m_lookDir = P.m_eye.forward;
        }

        internal void SetGhost(bool on) => P.SetGhostMode(on);

        internal void SetHudHidden(bool hidden)
        {
            if (Hud.instance != null)
            {
                Hud.instance.m_userHidden = hidden;
            }
        }

        // Center message text now. ArmMessage first: NoMessage still there after = nothing was shown.
        internal string CenterText
        {
            get
            {
                var hud = MessageHud.instance;
                return hud != null && hud.m_messageCenterText != null ? hud.m_messageCenterText.text : null;
            }
        }

        internal void ArmMessage()
        {
            var hud = MessageHud.instance;
            if (hud != null && hud.m_messageCenterText != null)
            {
                hud.m_messageCenterText.text = NoMessage;
            }
        }

        internal void End()
        {
            if (_ended)
            {
                return;
            }
            _ended = true;
            Safe("hooks", () =>
            {
                Plugin.TestDisplay = null;
            });
            Safe("page", ClosePage);
            Safe("controls", () =>
            {
                if (_controller != null)
                {
                    Drive(false);
                    _controller.enabled = _controllerWasEnabled;
                }
            });
            Safe("items", GiveBack);
            Safe("spawned", () =>
            {
                foreach (var go in _spawned)
                {
                    DestroyObject(go);
                }
                _spawned.Clear();
            });
            Safe("player", () =>
            {
                if (P == null)
                {
                    return;
                }
                P.SetGhostMode(_ghost);
                if (_moved)
                {
                    P.transform.position = _pos;
                    P.transform.rotation = _rot;
                    var body = P.m_body;
                    if (body != null)
                    {
                        body.position = _pos;
                        body.rotation = _rot;
                        body.linearVelocity = Vector3.zero;
                    }
                    P.m_lookYaw = _lookYaw;
                    P.m_lookPitch = _lookPitch;
                    P.UpdateEyeRotation();
                    P.m_lookDir = P.m_eye.forward;
                }
            });
            Safe("hud", () =>
            {
                if (Hud.instance != null)
                {
                    Hud.instance.m_userHidden = _hudHidden;
                }
                var hud = MessageHud.instance;
                if (hud != null && hud.m_messageCenterText != null && _centerText != null)
                {
                    hud.m_messageCenterText.text = _centerText;
                }
            });
            Safe("tame data", () =>
            {
                if (P == null || P.m_customData == null)
                {
                    return;
                }
                PutBack(CreatureCounts.TamesDataKey, _hadTames, _tames);
                PutBack(CreatureCounts.CountingSinceDataKey, _hadSince, _since);
                CounterStore.ClearCache();
            });
            Safe("stats", RestoreStats);
            Safe("skills", () =>
            {
                var data = P.m_skills.m_skillData;
                var keep = new HashSet<Skills.SkillType>();
                foreach (var pair in _skills)
                {
                    keep.Add(pair.Key);
                    if (data.TryGetValue(pair.Key, out var skill))
                    {
                        skill.m_level = pair.Value.x;
                        skill.m_accumulator = pair.Value.y;
                    }
                }
                foreach (var type in new List<Skills.SkillType>(data.Keys))
                {
                    if (!keep.Contains(type))
                    {
                        data.Remove(type);
                    }
                }
            });
            Safe("known items", () =>
            {
                P.m_knownMaterial.RemoveWhere(name => !_knownMaterial.Contains(name));
                P.m_knownRecipes.RemoveWhere(name => !_knownRecipes.Contains(name));
            });
        }

        private void PutBack(string key, bool had, string value)
        {
            if (had)
            {
                P.m_customData[key] = value;
            }
            else
            {
                P.m_customData.Remove(key);
            }
        }

        private void GiveBack()
        {
            if (P == null)
            {
                return;
            }
            foreach (var item in _given)
            {
                if (item == null)
                {
                    continue;
                }
                if (item.m_lastProjectile != null)
                {
                    DestroyObject(item.m_lastProjectile);
                    item.m_lastProjectile = null;
                }
                if (P.IsItemEquiped(item))
                {
                    P.UnequipItem(item, false);
                }
                if (Inv.ContainsItem(item))
                {
                    Inv.RemoveItem(item);
                }
            }
            _given.Clear();
            foreach (var pair in _stackBefore)
            {
                if (P.m_ammoItem != null && P.m_ammoItem.m_shared.m_name == pair.Key)
                {
                    P.UnequipItem(P.m_ammoItem, false);
                }
                var extra = Inv.CountItems(pair.Key) - pair.Value;
                if (extra > 0)
                {
                    Inv.RemoveItem(pair.Key, extra);
                }
            }
            _stackBefore.Clear();
            if (!_handsTouched)
            {
                return;
            }
            foreach (var old in new[] { _right, _left, _ammo })
            {
                if (old != null && Inv.ContainsItem(old) && !P.IsItemEquiped(old))
                {
                    P.EquipItem(old, false);
                }
            }
            // EquipItem refuse while a swing still play (test cut short): put it straight back in its hand.
            if (_right != null && Inv.ContainsItem(_right) && !P.IsItemEquiped(_right) && P.m_rightItem == null)
            {
                P.m_rightItem = _right;
                _right.m_equipped = true;
            }
            if (_left != null && Inv.ContainsItem(_left) && !P.IsItemEquiped(_left) && P.m_leftItem == null)
            {
                P.m_leftItem = _left;
                _left.m_equipped = true;
            }
            // EquipItem forget what was put away: say it again, then show it on the body.
            P.m_hiddenRightItem = _hiddenRight != null && Inv.ContainsItem(_hiddenRight) ? _hiddenRight : null;
            P.m_hiddenLeftItem = _hiddenLeft != null && Inv.ContainsItem(_hiddenLeft) ? _hiddenLeft : null;
            P.SetupEquipment();
        }

        // Saved kill table back now (test go on with the character's real numbers).
        internal void RestoreKills() => RestoreStats();

        private void RestoreStats()
        {
            var slots = Profile.m_playerStats;
            for (var i = 0; i < slots.Length && i < _enemy.Length; i++)
            {
                if (slots[i] == null || _enemy[i] == null)
                {
                    continue;
                }
                var buckets = slots[i].m_enemyStats;
                for (var j = 0; j < buckets.Length && j < _enemy[i].Length; j++)
                {
                    if (buckets[j] == null || _enemy[i][j] == null)
                    {
                        continue;
                    }
                    buckets[j].Clear();
                    foreach (var pair in _enemy[i][j])
                    {
                        buckets[j][pair.Key] = pair.Value;
                    }
                }
                slots[i].m_itemPickupStats.Clear();
                foreach (var pair in _pickups[i])
                {
                    slots[i].m_itemPickupStats[pair.Key] = pair.Value;
                }
                for (var k = 0; k < TouchedStats.Length; k++)
                {
                    if (_stats[i][k].HasValue)
                    {
                        slots[i].m_stats[TouchedStats[k]] = _stats[i][k].Value;
                    }
                }
            }
        }

        // One step that throw must not stop the others.
        private void Safe(string what, Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                SelfTest.Note(Test, $"clean-up of {what} threw {e.GetType().Name}: {e.Message}");
            }
        }
    }

    // ---------- places ----------

    private static int _solidMask;
    private static int _sightMask;

    private static Vector3 Ground(Vector3 p)
    {
        if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out var height))
        {
            p.y = height;
        }
        return p;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    // Dry ground where a creature stand free (no rock, tree or piece in a creature-size capsule).
    private static bool OpenGround(Vector3 p)
    {
        if (_solidMask == 0)
        {
            _solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle");
        }
        if (ZoneSystem.instance != null && p.y < ZoneSystem.instance.m_waterLevel + 0.3f)
        {
            return false;
        }
        return !Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.5f, _solidMask, QueryTriggerInteraction.Ignore);
    }

    private static bool ClearLine(Vector3 a, Vector3 b)
    {
        if (_sightMask == 0)
        {
            _sightMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
        }
        return !Physics.Linecast(a + Vector3.up * 1.2f, b + Vector3.up * 1.2f, _sightMask, QueryTriggerInteraction.Ignore);
    }

    // Direction (flat) from the player along which every given distance is open ground with a clear line from the
    // player. Player's facing first, then turn 30 degrees left and right. None = facing, and say so.
    private static Vector3 OpenDirection(Rig rig, bool needLine, params float[] distances)
    {
        var from = rig.P.transform.position;
        var forward = Flat(rig.P.transform.forward);
        for (var i = 0; i < 12; i++)
        {
            var angle = (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 30f;
            var dir = Quaternion.Euler(0f, angle, 0f) * forward;
            var ok = true;
            foreach (var d in distances)
            {
                var p = Ground(from + dir * d);
                if (!OpenGround(p) || (needLine && !ClearLine(from, p)))
                {
                    ok = false;
                    break;
                }
            }
            if (ok)
            {
                return dir;
            }
        }
        SelfTest.Note(rig.Test, "no open direction found around the player; using the one straight ahead");
        return forward;
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

    // ---------- the Compendium page ----------

    private sealed class Page
    {
        internal string Problem;                 // null = read
        internal string Entry;                   // Player Statistics entry text in the list (mod + vanilla wrote it)
        internal string Shown;                   // text area after the entry is picked (what the player read)
        internal string Topic;                   // title over the text area
        internal int Index = -1;
        internal readonly List<string> Topics = new List<string>();
        internal TextsDialog Dialog;

        internal bool Ok => Problem == null && Entry != null;
    }

    private sealed class Section
    {
        internal bool Found;                     // entry start with the header and end with two empty lines
        internal string Grey = "";
        internal bool OtherLine;
        internal string Raw = "";                // whole block, line ends included
        internal string Rest = "";               // vanilla text after
        internal readonly List<string> Creatures = new List<string>();
        internal readonly List<string> Others = new List<string>();
        internal readonly List<string> Odd = new List<string>();

        // Creature row that start with "<name>: " (indent cut), or null.
        internal string Row(string shownName)
        {
            foreach (var row in Creatures)
            {
                if (row.StartsWith(shownName + ": ", StringComparison.Ordinal))
                {
                    return row;
                }
            }
            return null;
        }

        internal string Describe() =>
            Found ? $"[{string.Join(" | ", Creatures.ToArray())}]" + (OtherLine ? $" other names [{string.Join(" | ", Others.ToArray())}]" : "")
                : "(no Creatures section)";
    }

    private static Section ParseSection(string entry)
    {
        var s = new Section();
        if (entry == null || !entry.StartsWith(Header + "\n", StringComparison.Ordinal))
        {
            return s;
        }
        // Mod write "\n" only; vanilla text use "\r\n": first "\n\n\n" = last row end + the two empty lines.
        var end = entry.IndexOf("\n\n\n", StringComparison.Ordinal);
        if (end < 0)
        {
            return s;
        }
        s.Found = true;
        s.Raw = entry.Substring(0, end + 3);
        s.Rest = entry.Substring(end + 3);
        var lines = entry.Substring(0, end).Split('\n');
        s.Grey = lines.Length > 1 ? lines[1] : "";
        var target = s.Creatures;
        for (var i = 2; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line == OtherNamesLine)
            {
                s.OtherLine = true;
                target = s.Others;
            }
            else if (line.StartsWith(Indent, StringComparison.Ordinal))
            {
                target.Add(line.Substring(Indent.Length));
            }
            else
            {
                s.Odd.Add(line);
            }
        }
        return s;
    }

    // First line vanilla AddStats write.
    private static bool VanillaStart(string text) =>
        text != null && (text.StartsWith("Cheater!", StringComparison.Ordinal)
                         || text.StartsWith("<color=orange>Difficulty Category: RawStats</color>", StringComparison.Ordinal));

    private static bool _openedInventory;

    // Me open the page like the raven button: inventory shown, TextsDialog.Setup (vanilla list build with every
    // patch), then pick the Player Statistics entry. Not InventoryGui.OnOpenTexts: MC Encyclopedia may jump to its own
    // window there (it remember the last tab). Page stay open until ClosePage (Rig.End call it too).
    private static IEnumerator OpenPage(Page page)
    {
        page.Problem = null;
        page.Entry = null;
        page.Shown = null;
        page.Topic = null;
        page.Index = -1;
        page.Topics.Clear();
        var gui = InventoryGui.instance;
        var player = Player.m_localPlayer;
        if (gui == null || player == null || gui.m_textsDialog == null || Localization.instance == null)
        {
            page.Problem = "no InventoryGui, Valheim Compendium dialog or local player";
            yield break;
        }
        var dialog = gui.m_textsDialog;
        page.Dialog = dialog;
        if (!gui.m_animator.GetBool("visible"))
        {
            gui.Show(null);
            _openedInventory = true;
        }
        // Dialog live under the inventory: wait until its parent is on.
        var end = Time.realtimeSinceStartup + 2f;
        while (dialog.transform.parent != null && !dialog.transform.parent.gameObject.activeInHierarchy
               && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        yield return null;
        if (dialog.transform.parent != null && !dialog.transform.parent.gameObject.activeInHierarchy)
        {
            page.Problem = "the inventory did not show within 2 s";
            yield break;
        }
        dialog.Setup(player);
        var texts = dialog.m_texts;
        var topic = Loc("$inventory_stats");
        for (var i = 0; i < texts.Count; i++)
        {
            page.Topics.Add(texts[i] != null ? texts[i].m_topic : "(null)");
            if (texts[i] != null && texts[i].m_topic == topic)
            {
                page.Index = i;
            }
        }
        if (page.Index < 0)
        {
            page.Problem = $"no '{topic}' entry among {texts.Count} entries";
            yield break;
        }
        page.Entry = texts[page.Index].m_text;
        dialog.ShowText(page.Index);
        yield return null;
        yield return null;
        page.Topic = dialog.m_textAreaTopic != null ? dialog.m_textAreaTopic.text : null;
        page.Shown = dialog.m_textArea != null ? dialog.m_textArea.text : null;
    }

    private static void ClosePage()
    {
        var gui = InventoryGui.instance;
        if (gui == null)
        {
            _openedInventory = false;
            return;
        }
        if (gui.m_textsDialog != null && gui.m_textsDialog.gameObject.activeSelf)
        {
            gui.m_textsDialog.OnClose();
        }
        if (_openedInventory)
        {
            _openedInventory = false;
            gui.Hide();
        }
    }

    // Open, read, close. "Reopen Player Statistics" of the test list.
    private static IEnumerator ReadPage(Page page)
    {
        yield return OpenPage(page);
        ClosePage();
        yield return null;
    }

    // Player Statistics entry text as the vanilla list build make it right now (TextsDialog.AddStats with every patch
    // on it), without showing anything: list left as before. For checks that must happen in one frame.
    private static string BuildEntry()
    {
        var gui = InventoryGui.instance;
        if (gui == null || gui.m_textsDialog == null || Localization.instance == null)
        {
            return null;
        }
        var dialog = gui.m_textsDialog;
        var texts = dialog.m_texts;
        var before = texts.Count;
        var topic = Loc("$inventory_stats");
        dialog.AddStats();
        string text = null;
        while (texts.Count > before)
        {
            var last = texts[texts.Count - 1];
            if (text == null && last != null && last.m_topic == topic)
            {
                text = last.m_text;
            }
            texts.RemoveAt(texts.Count - 1);
        }
        return text;
    }

    // ---------- hits ----------

    // Hit as vanilla attack code build it for a weapon of this skill (Attack set m_skill; bare hands also 99999).
    private static void Hit(Character victim, Player attacker, Skills.SkillType skill, float damage, int statusHash = 0)
    {
        var hit = new HitData();
        hit.m_damage.m_damage = damage;
        hit.m_skill = skill;
        hit.m_statusEffectHash = statusHash;
        hit.m_point = victim.GetCenterPoint();
        var dir = victim.transform.position - attacker.transform.position;
        hit.m_dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        hit.SetAttacker(attacker);
        victim.Damage(hit);
    }

    // Spawn frozen creature near the player, wait until it stand.
    private static IEnumerator SpawnNear(Rig rig, string prefab, Vector3 pos, Character[] result)
    {
        result[0] = rig.Spawn(prefab, pos, true);
        yield return null;
        yield return null;
    }

    // Wait until owner game removed the dead creature (death run on next physics step).
    private static IEnumerator WaitGone(Character victim, float timeout, Waiter w)
    {
        yield return Until(() => victim == null, timeout, null, w);
        yield return null;
    }
#endif
}
