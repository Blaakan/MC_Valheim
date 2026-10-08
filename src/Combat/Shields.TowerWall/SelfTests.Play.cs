#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;
using ItemType = ItemDrop.ItemData.ItemType;
using Object = UnityEngine.Object;

namespace MC.Combat.ShieldsTowerWallMod;

// Debug build only. More in-world self tests (one per group of TESTING.md items), same rules as SelfTests.cs: rules
// forced in memory only (ServerRules.TestRules, or TowerRules.DebugOwn + ServerRules.OwnChanged for the own-settings
// path), player driven through SelfTestHooks, never a config write; Rig put everything back.
//   tower.tips     tooltip lines of every default tower (two-handed, cannot parry, bash lines, Braced sentence,
//                  movement -30%, block armor per quality)
//   tower.equip    every weapon kind put the tower away and the tower empty both hands (bow, battleaxe, hammer too);
//                  character loaded with a sword and a tower both marked equipped keep only the later one
//   tower.stand    item stand and armor stand take a two-handed tower, show it, give it back two-handed
//   tower.speed    speed the game aim for (m/s): carried, sprint, walk, crouch, braced, round shield, back, heavy armor;
//                  turn rate while braced
//   tower.parry    timed block never parry (attacker not staggered, normal block stamina and adrenaline); a round
//                  shield do (control), and lose it as a listed tower
//   tower.wall     Troll punch numbers at Blocking 0, Wood tower until the guard break, fire / fall / behind not blocked,
//                  stagger resist from behind, guard-break push, knockback resist 0, PvP bash on a braced bearer
//   tower.push     push the blocked attacker get, at BlockForcePercent 100 and 50
//   tower.poison   real Blob and Greydwarf Shaman attacks: blocked from the front, full from behind, full with the
//                  setting off
// Other new tests: SelfTests.Bash.cs. Multiplayer: SelfTests.Mp.cs.
internal static partial class SelfTests
{
    private const string TipsName = "tower.tips";
    private const string EquipName = "tower.equip";
    private const string StandName = "tower.stand";
    private const string SpeedName = "tower.speed";
    private const string ParryName = "tower.parry";
    private const string WallName = "tower.wall";
    private const string PushName = "tower.push";
    private const string PoisonName = "tower.poison";

    private static readonly KeyValuePair<string, Func<IEnumerator>>[] MoreTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(TipsName, RunTips),
        new KeyValuePair<string, Func<IEnumerator>>(EquipName, RunEquip),
        new KeyValuePair<string, Func<IEnumerator>>(StandName, RunStand),
        new KeyValuePair<string, Func<IEnumerator>>(SpeedName, RunSpeed),
        new KeyValuePair<string, Func<IEnumerator>>(ParryName, RunParry),
        new KeyValuePair<string, Func<IEnumerator>>(WallName, RunWall),
        new KeyValuePair<string, Func<IEnumerator>>(PushName, RunPush),
        new KeyValuePair<string, Func<IEnumerator>>(PoisonName, RunPoison),
        new KeyValuePair<string, Func<IEnumerator>>(TrainName, RunTrain),
        new KeyValuePair<string, Func<IEnumerator>>(GuardName, RunGuard),
        new KeyValuePair<string, Func<IEnumerator>>(FollowName, RunFollow),
        new KeyValuePair<string, Func<IEnumerator>>(CadenceName, RunCadence),
        new KeyValuePair<string, Func<IEnumerator>>(GroupName, RunGroup),
        new KeyValuePair<string, Func<IEnumerator>>(LiveName, RunLive),
        new KeyValuePair<string, Func<IEnumerator>>(WarnName, RunWarn),
        new KeyValuePair<string, Func<IEnumerator>>(CreatureName, RunCreature),
        new KeyValuePair<string, Func<IEnumerator>>(SwitchName, RunSwitch),
        new KeyValuePair<string, Func<IEnumerator>>(PressName, RunPress),
        new KeyValuePair<string, Func<IEnumerator>>(PressBugName, RunPressBug),
        new KeyValuePair<string, Func<IEnumerator>>(CrossbowName, RunCrossbow),
        new KeyValuePair<string, Func<IEnumerator>>(LogName, RunLog), // last: it look back at the whole run
    };

    private static void RegisterMore()
    {
        EnsureTap();
        foreach (var test in MoreTests)
        {
            SelfTest.Register(test.Key, test.Value);
        }
        RegisterMultiplayer();
    }

    private static void UnregisterMore()
    {
        foreach (var test in MoreTests)
        {
            SelfTest.Unregister(test.Key);
        }
        UnregisterMultiplayer();
    }

    // ---------- log tap ----------

    // Me listen to this mod's own log lines (BepInEx listener, on from the first Register to the end of the game):
    // tests read the Warnings and Info lines a step made, and tower.log look back at every error of the run.
    private sealed class LogTap : BepInEx.Logging.ILogListener
    {
        private readonly object _gate = new object();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _infos = new List<string>();
        private readonly List<string> _bad = new List<string>();

        public void LogEvent(object sender, BepInEx.Logging.LogEventArgs e)
        {
            try
            {
                if (e == null || e.Source == null || e.Source.SourceName != ModInfo.Name)
                {
                    return;
                }
                var text = e.Data != null ? e.Data.ToString() : "";
                if (text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    return; // test results go through the same logger (a FAIL is an Error line)
                }
                lock (_gate)
                {
                    if ((e.Level & (BepInEx.Logging.LogLevel.Error | BepInEx.Logging.LogLevel.Fatal)) != 0)
                    {
                        _bad.Add("error: " + text);
                    }
                    else if ((e.Level & BepInEx.Logging.LogLevel.Warning) != 0)
                    {
                        _warnings.Add(text);
                        if (text.Contains("Towers setting") && DefaultsInForce())
                        {
                            _bad.Add("Towers warning with the default settings: " + text);
                        }
                    }
                    else if ((e.Level & (BepInEx.Logging.LogLevel.Info | BepInEx.Logging.LogLevel.Message)) != 0)
                    {
                        _infos.Add(text);
                    }
                }
            }
            catch (Exception)
            {
                // Listener never throw into the logger.
            }
        }

        public void Dispose()
        {
        }

        internal int WarningMark()
        {
            lock (_gate)
            {
                return _warnings.Count;
            }
        }

        internal List<string> WarningsSince(int mark)
        {
            lock (_gate)
            {
                return _warnings.Skip(Mathf.Clamp(mark, 0, _warnings.Count)).ToList();
            }
        }

        internal int InfoMark()
        {
            lock (_gate)
            {
                return _infos.Count;
            }
        }

        internal List<string> InfosSince(int mark)
        {
            lock (_gate)
            {
                return _infos.Skip(Mathf.Clamp(mark, 0, _infos.Count)).ToList();
            }
        }

        internal List<string> Bad()
        {
            lock (_gate)
            {
                return _bad.ToList();
            }
        }
    }

    private static LogTap _tap;
    private static string _defaultsKey;

    private static LogTap Tap
    {
        get
        {
            EnsureTap();
            return _tap;
        }
    }

    private static void EnsureTap()
    {
        if (_tap == null)
        {
            _tap = new LogTap();
            BepInEx.Logging.Logger.Listeners.Add(_tap);
        }
    }

    private static bool DefaultsInForce()
    {
        _defaultsKey ??= TowerRules.Defaults().Key;
        var rules = TowerRules.InForce;
        return rules != null && rules.Key == _defaultsKey;
    }

    // ---------- helpers ----------

    private static int Hash(string prefab) => prefab.GetStableHashCode();

    private static string Plain(string text) => text.Replace("<color=orange>", "").Replace("<color=yellow>", "").Replace("</color>", "");

    private static string OneLine(string text) => text.Replace("\n", " | ");

    private static void SetSkill(Player p, Skills.SkillType type, float level)
    {
        var skill = p.m_skills.GetSkill(type);
        skill.m_level = level;
        skill.m_accumulator = 0f;
    }

    // Level and progress in one number that only go up when the skill is trained.
    private static double Trained(Player p, Skills.SkillType type)
    {
        var skill = p.m_skills.GetSkill(type);
        return skill.m_level * 100000.0 + skill.m_accumulator;
    }

    // Own-settings path with no config write: "the settings now say this", then what Plugin.OnSettingChanged call.
    // Wait till TowerSync applied them; took = seconds from the change to the items (-1 = never).
    private static IEnumerator UseOwn(TowerRules rules, Box<float> took = null)
    {
        ServerRules.TestRules = null;
        TowerRules.DebugOwn = rules;
        var t0 = Time.unscaledTime;
        ServerRules.OwnChanged();
        while (Time.unscaledTime - t0 < 3f && (TowerSync.HasWork || TowerSync.AppliedKey != rules.Key))
        {
            yield return null;
        }
        if (took != null)
        {
            took.Value = TowerSync.AppliedKey == rules.Key ? Time.unscaledTime - t0 : -1f;
        }
    }

    private static void ClearOwn()
    {
        if (TowerRules.DebugOwn != null)
        {
            TowerRules.DebugOwn = null;
            ServerRules.OwnChanged();
        }
    }

    private static IEnumerator Seconds(float seconds)
    {
        var until = Time.time + seconds;
        while (Time.time < until)
        {
            yield return null;
        }
    }

    private static IEnumerator FixedSteps(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return Fixed;
        }
    }

    private static IEnumerator Guard(Player p, bool on)
    {
        SelfTestHooks.HoldBlock = on;
        if (on)
        {
            yield return Until(() => p.IsBlocking() && p.m_internalBlockingState, 3f);
        }
        else
        {
            yield return Until(() => !p.m_internalBlockingState, 2f);
        }
        yield return Fixed;
        yield return Fixed;
    }

    // Creature at this flat angle (degrees, + = right) and distance from the player, facing the player, still.
    private static void PlaceAt(Player p, Character c, float angle, float distance)
    {
        var dir = Quaternion.Euler(0f, angle, 0f) * Flat(p.transform.forward);
        var pos = p.transform.position + dir * distance;
        var rot = Quaternion.LookRotation(-dir);
        c.transform.SetPositionAndRotation(pos, rot);
        var body = c.m_body;
        if (body != null)
        {
            body.position = pos;
            body.rotation = rot;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
            }
        }
        c.m_pushForce = Vector3.zero;
    }

    // Creature out of the way (behind the player, far from every bash).
    private static void Park(Player p, Character c, float side = 0f)
    {
        PlaceAt(p, c, 180f + side, 9f);
    }

    // The player's own m/s the game aim for while it walk along dir: Character.m_currentVel (move direction x speed
    // after equipment, run / walk / crouch and status effects; slopes turn it, never change its length; collisions are
    // not in it). Last ten physics steps averaged.
    private static IEnumerator Stride(Player p, Vector3 dir, bool run, Box<float> speed, float seconds = 0.7f)
    {
        SelfTestHooks.Move = dir;
        SelfTestHooks.Run = run;
        SelfTestHooks.Moving = true;
        var until = Time.time + seconds;
        var last = new Queue<float>();
        while (Time.time < until)
        {
            yield return Fixed;
            last.Enqueue(p.m_currentVel.magnitude);
            if (last.Count > 10)
            {
                last.Dequeue();
            }
        }
        speed.Value = last.Count > 0 ? last.Average() : 0f;
        SelfTestHooks.Moving = false;
        SelfTestHooks.Move = Vector3.zero;
        SelfTestHooks.Run = false;
        yield return Until(() => p.m_currentVel.magnitude < 0.05f, 1f);
    }

    // ---------- tower.tips ----------

    private static IEnumerator RunTips()
    {
        var c = new Checks(TipsName);
        var rig = new Rig(TipsName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            c.Check(TowerSync.AppliedKey == rules.Key, "default rules applied");
            var iron = rig.Give(IronTower);
            if (iron == null)
            {
                c.Check(false, "could not add a ShieldIronTower");
                c.Report();
                yield break;
            }
            var tip = iron.GetTooltip();
            SelfTest.Note(TipsName, $"Iron tower tooltip: '{OneLine(tip)}'; '$item_twohanded' reads '{Localization.instance.Localize("$item_twohanded")}' in this game language");
            c.Check(tip.Contains("\n$item_twohanded") && !tip.Contains("$item_onehanded"), "Iron tower tooltip: the Two-handed line, no One-handed line");
            c.Check(tip.Contains("\n<color=orange>Cannot parry</color>"), "Iron tower tooltip: 'Cannot parry'");
            c.Check(tip.Contains("\nBash stagger: <color=orange>×25</color>"), "Iron tower tooltip: 'Bash stagger: ×25'");
            c.Check(tip.Contains("\nBash cooldown: <color=orange>2 s</color>"), "Iron tower tooltip: 'Bash cooldown: 2 s'");
            const string sentence = "Braced: movement -30%, blocks attacks from the front that normally cannot be blocked; "
                                    + "while you have stamina for another block: stagger -80%, no knockback from the front";
            c.Check(tip.Contains("\n" + TowerTooltip.BracedLine(rules)) && Plain(tip).Contains("\n" + sentence),
                $"Iron tower tooltip ends with the Braced sentence of TESTING T01 ('{sentence}')");
            c.Check(tip.Contains("$inventory_blunt: <color=orange>12</color>") && tip.Contains("$item_staminause: <color=orange>20</color>")
                    && tip.Contains("$item_knockback: <color=orange>40</color>"),
                "Iron tower tooltip lists the bash: blunt 12, stamina use 20, knockback 40");
            var movement = "\n" + Player.s_equipmentModifierTooltips[0] + ": <color=orange>-30%</color>";
            c.Check(tip.Contains(movement), $"Iron tower tooltip: movement -30% ('{movement.Trim()}')");

            // Block armor on the tooltips at quality 1 (TESTING T08), and +15 per quality on the Iron one.
            var expected = new Dictionary<string, float>
            {
                { "ShieldWoodTower", 25f }, { IronTower, 130f }, { "ShieldBlackmetalTower", 260f }, { "ShieldGoldTower", 395f },
            };
            var seen = new List<string>();
            foreach (var entry in rules.Entries)
            {
                var item = entry.Prefab == IronTower ? iron : rig.Give(entry.Prefab);
                var snap = SnapshotOf(entry.Prefab);
                if (item == null || snap == null)
                {
                    c.Check(false, $"could not add a {entry.Prefab} (or no snapshot)");
                    continue;
                }
                var text = item.GetTooltip();
                var armor = snap.BlockPower * rules.BlockArmorMultiplier;
                seen.Add($"{entry.Prefab} {F(armor)}");
                c.Check(text.Contains($"$item_blockarmor: <color=orange>{armor}</color>"),
                    $"{entry.Prefab} tooltip: block armor {F(armor)} (vanilla {F(snap.BlockPower)} x {F(rules.BlockArmorMultiplier)})");
                if (expected.TryGetValue(entry.Prefab, out var want))
                {
                    c.Check(Near(armor, want), $"{entry.Prefab}: block armor {F(armor)} at quality 1, TESTING T08 says {F(want)}");
                }
                c.Check(text.Contains("\n$item_twohanded") && text.Contains("Cannot parry") && !text.Contains("$item_parrybonus")
                        && !text.Contains("$item_parryadrenaline"),
                    $"{entry.Prefab} tooltip: Two-handed, Cannot parry, no parry bonus or parry adrenaline line");
                c.Check(text.Contains($"$inventory_blunt: <color=orange>{entry.BashDamage}</color>"),
                    $"{entry.Prefab} tooltip: bash blunt {F(entry.BashDamage)}");
            }
            SelfTest.Note(TipsName, "block armor on the tooltips at quality 1: " + string.Join(", ", seen.ToArray()));
            var iron2 = rig.Give(IronTower, 2);
            var tip2 = iron2 != null ? iron2.GetTooltip() : "";
            c.Check(iron2 != null && iron2.m_quality == 2 && tip2.Contains("$item_blockarmor: <color=orange>145</color>"),
                $"Iron tower of quality 2: block armor 145 on the tooltip (+15 per quality): '{OneLine(tip2)}'");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.equip ----------

    // What Player.Load do with the items a save marked equipped (Player.EquipInventoryItems), for these two items.
    // The real method when nothing else is marked equipped (it would unmark worn armor), else the same loop.
    private static string LoadEquipped(Rig rig, ItemDrop.ItemData a, ItemDrop.ItemData b)
    {
        var p = rig.P;
        rig.EmptyHands();
        a.m_equipped = true;
        b.m_equipped = true;
        var marked = rig.Inv.GetEquippedItems();
        if (marked.Count == 2)
        {
            p.EquipInventoryItems();
            return "Player.EquipInventoryItems";
        }
        foreach (var item in marked)
        {
            if ((ReferenceEquals(item, a) || ReferenceEquals(item, b)) && !p.EquipItem(item, false))
            {
                item.m_equipped = false;
            }
        }
        return "the loop of Player.EquipInventoryItems on these two items (other items are worn)";
    }

    private static IEnumerator RunEquip()
    {
        var c = new Checks(EquipName);
        var rig = new Rig(EquipName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            c.Check(TowerSync.AppliedKey == rules.Key, "default rules applied");
            var tower = rig.Give(IronTower);
            if (tower == null)
            {
                c.Check(false, "could not add a ShieldIronTower");
                c.Report();
                yield break;
            }
            ItemDrop.ItemData sword = null;
            // TESTING T02: each one put the tower away; the tower then empty both hands again.
            foreach (var name in new[] { "SwordIron", "Torch", "ShieldBanded", "Bow", "Battleaxe", "Hammer" })
            {
                var item = rig.Give(name);
                if (item == null)
                {
                    c.Check(false, $"could not add a {name}");
                    continue;
                }
                if (name == "SwordIron")
                {
                    sword = item;
                }
                rig.EmptyHands();
                p.EquipItem(tower);
                c.Check(Only(p, tower, left: true), $"before {name}: only the tower, in the left hand ({Hands(p)})");
                var equipped = p.EquipItem(item);
                var left = ReferenceEquals(p.m_leftItem, item);
                c.Check(equipped && item.m_equipped && !tower.m_equipped && !ReferenceEquals(p.m_leftItem, tower)
                        && !ReferenceEquals(p.m_hiddenLeftItem, tower) && Only(p, item, left),
                    $"{name} ({item.m_shared.m_itemType}) with the tower held: the tower is put away, only the {name} is held ({Hands(p)})");
                var back = p.EquipItem(tower);
                c.Check(back && Only(p, tower, left: true) && !item.m_equipped && p.m_hiddenLeftItem == null && p.m_hiddenRightItem == null,
                    $"tower again after {name}: both hands emptied, only the tower ({Hands(p)})");
            }

            // Character saved with a sword and a tower both in hand (mod off then), loaded with the mod on (TESTING
            // M16, README "Good to know"): only the one the game equip last stay.
            if (sword != null)
            {
                var first = rig.Inv.GetAllItems().IndexOf(tower) < rig.Inv.GetAllItems().IndexOf(sword) ? tower : sword;
                var how = LoadEquipped(rig, sword, tower);
                var winner = ReferenceEquals(first, tower) ? sword : tower;
                var loser = ReferenceEquals(winner, tower) ? sword : tower;
                c.Check(Only(p, winner, left: ReferenceEquals(winner, tower)) && !loser.m_equipped && rig.Inv.ContainsItem(loser)
                        && p.m_hiddenLeftItem == null && p.m_hiddenRightItem == null,
                    $"loaded with a sword and a tower both marked equipped ({Name(first)} first in the inventory; {how}): only the {Name(winner)} stays, "
                    + $"the {Name(loser)} is in the inventory ({Hands(p)})");
                // Other order: a second tower after the sword.
                var tower2 = rig.Give(IronTower);
                if (tower2 != null)
                {
                    LoadEquipped(rig, sword, tower2);
                    c.Check(Only(p, tower2, left: true) && !sword.m_equipped && rig.Inv.ContainsItem(sword)
                            && tower2.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft && p.m_hiddenRightItem == null,
                        $"the same with the tower after the sword: only the tower stays, two-handed, the right hand empty ({Hands(p)})");
                }
                else
                {
                    c.Check(false, "could not add a second ShieldIronTower");
                }
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.stand ----------

    private static GameObject Build(Rig rig, GameObject prefab, float right)
    {
        var p = rig.P;
        var pos = p.transform.position + Flat(p.transform.right) * right + Vector3.up * 1.2f;
        var obj = Object.Instantiate(prefab, pos, Quaternion.identity);
        rig.Track(obj);
        var wnt = obj.GetComponent<WearNTear>();
        if (wnt != null)
        {
            wnt.enabled = false; // no support check: it never break and spill
        }
        return obj;
    }

    // The tower a stand dropped: on the ground (not seen before), or already auto-picked into the inventory.
    private static ItemDrop.ItemData Dropped(Rig rig, HashSet<ItemDrop> before, out ItemDrop drop)
    {
        drop = null;
        foreach (var d in ItemDrop.s_instances)
        {
            if (d != null && !before.Contains(d) && Utils.GetPrefabName(d.gameObject) == IronTower)
            {
                drop = d;
                rig.Track(d.gameObject);
                return d.m_itemData;
            }
        }
        foreach (var item in rig.Inv.GetAllItems())
        {
            if (item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == IronTower && !rig.IsTracked(item))
            {
                return item;
            }
        }
        return null;
    }

    private static IEnumerator TakeBack(Rig rig, Checks c, string stand, HashSet<ItemDrop> before, Func<bool> empty)
    {
        var p = rig.P;
        yield return Until(empty, 2f);
        yield return Frames(3);
        var item = Dropped(rig, before, out var drop);
        c.Check(empty() && item != null, $"{stand}: taking it back drops the tower again");
        if (item == null)
        {
            yield break;
        }
        c.Check(item.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft, $"{stand}: the tower it gives back carries tower data (type {item.m_shared.m_itemType})");
        if (drop != null)
        {
            p.Pickup(drop.gameObject, true, false);
        }
        rig.Track(item);
        c.Check(rig.Inv.ContainsItem(item) && !item.m_equipped && p.m_leftItem == null,
            $"{stand}: picked up again it goes to the inventory, not into the hands ({Hands(p)})");
        c.Check(p.EquipItem(item) && Only(p, item, left: true) && item.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft,
            $"{stand}: equipped again it is two-handed ({Hands(p)})");
        rig.EmptyHands();
    }

    private static IEnumerator RunStand()
    {
        var c = new Checks(StandName);
        var rig = new Rig(StandName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var tower2 = rig.Give(IronTower);
            if (tower == null || tower2 == null)
            {
                c.Check(false, "could not add two ShieldIronTower");
                c.Report();
                yield break;
            }
            var hash = Hash(IronTower);
            var name = tower.m_shared.m_name;

            // Item stand: the hammer's own ones first ("itemstand", "itemstandh"), else the first buildable one that
            // give its item back. The real stands list their items and types (run: 38 items, 13 types), so "take any
            // item" is not how to find them: the test is whether the stand take the tower (CanAttach, checked below).
            GameObject standPrefab = null;
            var candidates = new List<GameObject>();
            foreach (var known in new[] { "itemstand", "itemstandh" })
            {
                var go = ZNetScene.instance.GetPrefab(known);
                if (go != null)
                {
                    candidates.Add(go);
                }
            }
            candidates.AddRange(ZNetScene.instance.m_prefabs);
            foreach (var go in candidates)
            {
                var s = go != null ? go.GetComponentInChildren<ItemStand>(true) : null;
                if (s == null || go.GetComponent<Piece>() == null || !s.m_canBeRemoved || s.m_guardianPower != null || s.m_autoAttach)
                {
                    continue;
                }
                standPrefab = go;
                break;
            }
            c.Check(standPrefab != null, "a buildable item stand that gives its item back exists");
            if (standPrefab != null)
            {
                var template = standPrefab.GetComponentInChildren<ItemStand>(true);
                c.Check(template.CanAttach(tower) && !template.IsUnsupported(tower),
                    $"item stand {standPrefab.name}: it accepts the two-handed tower (supported types [{string.Join(",", template.m_supportedTypes.Select(t => t.ToString()).ToArray())}], "
                    + $"{template.m_supportedItems.Count} listed items)");
            }
            if (standPrefab != null)
            {
                var stand = Build(rig, standPrefab, 6f).GetComponentInChildren<ItemStand>();
                yield return Frames(2);
                var before = new HashSet<ItemDrop>(ItemDrop.s_instances);
                var used = stand.UseItem(p, tower);
                yield return Until(() => stand.HaveAttachment(), 2f);
                yield return Frames(3);
                c.Check(used && stand.HaveAttachment() && stand.GetAttachedItem() == hash && !rig.Inv.ContainsItem(tower),
                    $"item stand {standPrefab.name}: it takes the two-handed tower (attached {stand.GetAttachedItem() == hash}, left the inventory {!rig.Inv.ContainsItem(tower)})");
                c.Check(stand.m_visualItem != null && stand.m_visualItem.activeInHierarchy && stand.m_currentItemName == name,
                    $"item stand {standPrefab.name}: it shows the tower (item '{stand.m_currentItemName}')");
                SelfTest.Screenshot(StandName, "item-stand");
                yield return null;
                yield return null;
                stand.Interact(p, true, false);
                yield return TakeBack(rig, c, $"item stand {standPrefab.name}", before, () => !stand.HaveAttachment());
            }

            // Armor stand: its shield slot.
            GameObject armorPrefab = null;
            var slotIndex = -1;
            foreach (var go in ZNetScene.instance.m_prefabs)
            {
                var a = go != null ? go.GetComponentInChildren<ArmorStand>(true) : null;
                if (a == null)
                {
                    continue;
                }
                for (var i = 0; i < a.m_slots.Count && slotIndex < 0; i++)
                {
                    if (a.m_slots[i].m_supportedTypes.Contains(ItemType.Shield) && a.m_slots[i].m_switch != null)
                    {
                        slotIndex = i;
                    }
                }
                if (slotIndex >= 0)
                {
                    armorPrefab = go;
                    break;
                }
            }
            c.Check(armorPrefab != null, "an armor stand with a shield slot exists");
            if (armorPrefab != null)
            {
                var armor = Build(rig, armorPrefab, -6f).GetComponentInChildren<ArmorStand>();
                yield return Frames(2);
                var slot = armor.m_slots[slotIndex];
                c.Check(armor.CanAttach(slot, tower2), $"armor stand {armorPrefab.name}: its shield slot (slot {slotIndex}, {slot.m_slot}) accepts the two-handed tower");
                var before = new HashSet<ItemDrop>(ItemDrop.s_instances);
                var used = armor.UseItem(slot.m_switch, p, tower2);
                yield return Until(() => armor.HaveAttachment(slotIndex), 2f);
                yield return Frames(4);
                c.Check(used && armor.HaveAttachment(slotIndex) && armor.GetAttachedItem(slotIndex) == hash && !rig.Inv.ContainsItem(tower2),
                    $"armor stand {armorPrefab.name}: the shield slot takes the tower (attached {armor.GetAttachedItem(slotIndex) == hash}, left the inventory {!rig.Inv.ContainsItem(tower2)})");
                var vis = armor.m_visEquipment;
                var shown = vis != null && (vis.m_leftItemInstance != null || vis.m_leftBackItemInstance != null);
                c.Check(slot.m_currentItemName == name && shown,
                    $"armor stand {armorPrefab.name}: it shows the tower (item '{slot.m_currentItemName}', model on the stand {shown})");
                SelfTest.Screenshot(StandName, "armor-stand");
                yield return null;
                yield return null;
                armor.UseItem(slot.m_switch, p, null);
                yield return TakeBack(rig, c, $"armor stand {armorPrefab.name}", before, () => !armor.HaveAttachment(slotIndex));
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.speed ----------

    private static IEnumerator Crouch(Player p, bool on)
    {
        p.SetCrouch(on);
        yield return Until(() => p.IsCrouching() == on, 2f);
        yield return FixedSteps(3);
    }

    // Degrees the body turn in ten physics steps (0.2 s) toward a look direction 170 degrees away, while blocking
    // (vanilla UpdateRotation: turn speed x status effects).
    private static IEnumerator Turn(Player p, Box<float> degrees)
    {
        yield return Fixed;
        var start = p.transform.rotation;
        p.m_lookYaw = start * Quaternion.Euler(0f, 170f, 0f);
        yield return FixedSteps(10);
        degrees.Value = Quaternion.Angle(start, p.transform.rotation);
        Face(p, start * Vector3.forward);
        yield return Fixed;
    }

    private static IEnumerator RunSpeed()
    {
        var c = new Checks(SpeedName);
        var rig = new Rig(SpeedName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var banded = rig.Give("ShieldBanded");
            if (tower == null || banded == null)
            {
                c.Check(false, "could not add ShieldIronTower and ShieldBanded");
                c.Report();
                yield break;
            }
            p.m_walk = false;
            p.UpdateModifiers();
            var jogFactor0 = p.GetJogSpeedFactor();
            var plain = Near(jogFactor0, 1f, 0.0001f);
            var fwd = Flat(p.transform.forward);
            var v = new Box<float>();
            var sign = 1f;
            // Each stride go the other way: player stay around its place.
            IEnumerator Go(bool run)
            {
                sign = -sign;
                yield return Stride(p, fwd * sign, run, v);
            }

            // Without the tower.
            yield return Go(false);
            var jog0 = v.Value;
            yield return Go(true);
            var run0 = v.Value;
            p.m_walk = true;
            yield return Go(false);
            var walk0 = v.Value;
            p.m_walk = false;
            yield return Crouch(p, true);
            var crouched0 = p.IsCrouching();
            yield return Go(false);
            var crouch0 = v.Value;
            yield return Crouch(p, false);
            c.Check(Near(jog0, p.m_speed * jogFactor0, 0.05f * p.m_speed) && run0 > jog0 + 0.5f,
                $"control, no tower: jog {F(jog0)} m/s (speed {F(p.m_speed)} x {F(jogFactor0)}), sprint {F(run0)} m/s");

            // Tower in hand (TESTING T05).
            p.EquipItem(tower);
            p.UpdateModifiers();
            yield return Go(false);
            var jog1 = v.Value;
            yield return Go(true);
            var run1 = v.Value;
            p.m_walk = true;
            yield return Go(false);
            var walk1 = v.Value;
            p.m_walk = false;
            yield return Crouch(p, true);
            var crouched1 = p.IsCrouching();
            yield return Go(false);
            var crouch1 = v.Value;
            yield return Crouch(p, false);
            SelfTest.Note(SpeedName, $"m/s without / with the tower in hand: jog {F(jog0)} / {F(jog1)}, sprint {F(run0)} / {F(run1)}, walk {F(walk0)} / {F(walk1)}, "
                                     + $"crouch {F(crouch0)} / {F(crouch1)} (player speed {F(p.m_speed)}, run {F(p.m_runSpeed)}, walk {F(p.m_walkSpeed)}, crouch {F(p.m_crouchSpeed)})");
            c.Check(Near(jog1, p.m_speed * (jogFactor0 - 0.30f), 0.04f * p.m_speed),
                $"tower in hand: jog {F(jog1)} m/s = speed {F(p.m_speed)} x ({F(jogFactor0)} - 0.30)");
            c.Check(run1 > jog1 + 0.1f, $"tower in hand: sprinting ({F(run1)} m/s) is still faster than jogging ({F(jog1)} m/s)");
            c.Check(Near(walk1, walk0, 0.05f) && Near(walk0, p.m_walkSpeed, 0.05f), $"walking speed does not change ({F(walk0)} -> {F(walk1)} m/s)");
            c.Check(crouched0 && crouched1 && Near(crouch1, crouch0, 0.05f), $"crouching speed does not change ({F(crouch0)} -> {F(crouch1)} m/s, crouched {crouched0}/{crouched1})");

            // The same tower with vanilla data (empty Towers list): the "3.6 with a normal tower shield" of T05.
            var none = TowerRules.Defaults().With(r => r.Towers = "");
            yield return UseRules(none);
            p.UpdateModifiers();
            yield return Go(false);
            var jogVanilla = v.Value;
            yield return UseRules(rules);
            p.UpdateModifiers();
            c.Check(jog1 < jogVanilla - 0.5f, $"much slower than with a normal tower shield ({F(jog1)} m/s against {F(jogVanilla)} m/s)");
            if (plain)
            {
                c.Check(jog1 >= 2.6f && jog1 <= 3.0f && jogVanilla >= 3.4f && jogVanilla <= 3.8f,
                    $"no slowing gear worn: about 2.8 m/s with the tower ({F(jog1)}), about 3.6 with a normal one ({F(jogVanilla)})");
            }
            else
            {
                SelfTest.Note(SpeedName, $"the player wears slowing gear (jog factor {F(jogFactor0)} without the tower): the 2.8 / 3.6 m/s numbers were not checked");
            }

            // Braced (TESTING T06): advance and turning, against a round shield.
            var turn = new Box<float>();
            yield return Guard(p, true);
            yield return Go(false);
            var braced = v.Value;
            yield return Turn(p, turn);
            var turnTower = turn.Value;
            yield return Guard(p, false);
            yield return Go(false);
            var released = v.Value;
            p.EquipItem(banded);
            p.UpdateModifiers();
            yield return Guard(p, true);
            var roundBraced = Brace.Clone != null && !Brace.Clone.m_hidden;
            yield return Go(false);
            var bandedBlock = v.Value;
            yield return Turn(p, turn);
            var turnBanded = turn.Value;
            yield return Guard(p, false);
            SelfTest.Note(SpeedName, $"blocking: advance {F(braced)} m/s with the tower, {F(bandedBlock)} m/s with ShieldBanded; turned in 0.2 s {F(turnTower)} degrees with the tower, "
                                     + $"{F(turnBanded)} with ShieldBanded (turn speed {F(p.m_turnSpeed)} degrees/s)");
            c.Check(Near(braced, jog1 * 0.70f, 0.05f * jog1), $"braced: advance {F(braced)} m/s = carried {F(jog1)} x 0.70");
            c.Check(!roundBraced && braced < bandedBlock * 0.75f, $"braced advance is noticeably slower than blocking with ShieldBanded ({F(braced)} against {F(bandedBlock)} m/s)");
            c.Check(turnBanded > 5f && turnTower / turnBanded >= 0.6f && turnTower / turnBanded <= 0.8f,
                $"braced turning is slower than with ShieldBanded: x{F(turnBanded > 0f ? turnTower / turnBanded : 0f)} (0.70 expected)");
            c.Check(Near(turnTower, p.m_turnSpeed * 0.70f * 0.2f, 0.15f * p.m_turnSpeed * 0.2f),
                $"braced: turned {F(turnTower)} degrees in 0.2 s (turn speed {F(p.m_turnSpeed)} x 0.70 x 0.2 = {F(p.m_turnSpeed * 0.14f)})");
            c.Check(Near(released, jog1, 0.04f * p.m_speed), $"block released: back to the carried speed ({F(released)} m/s, carried {F(jog1)})");

            // On the back, then drawn again (TESTING T28).
            p.EquipItem(tower);
            p.HideHandItems();
            p.UpdateModifiers();
            // Putting away play the "equip_hip" animation: while it play vanilla walk every character (Character.
            // UpdateWalking: InMinorActionSlowdown = walk speed), tower or not. First run measured that walk (1.6 m/s).
            // Speed is read once the animation is over.
            yield return Until(() => p.InMinorAction(), 0.5f);
            yield return Until(() => !p.InMinorAction(), 4f);
            yield return FixedSteps(3);
            c.Check(!p.InMinorAction() && ReferenceEquals(p.m_hiddenLeftItem, tower) && p.m_leftItem == null,
                $"put away (R): the tower is on the back and the put-away animation is over ({Hands(p)})");
            yield return Go(false);
            var jogBack = v.Value;
            yield return Go(true);
            var runBack = v.Value;
            p.ShowHandItems(false, false);
            p.UpdateModifiers();
            yield return Go(false);
            var jogDrawn = v.Value;
            c.Check(Near(jogBack, jog0, 0.04f * p.m_speed) && Near(runBack, run0, 0.04f * p.m_runSpeed),
                $"tower on the back: normal speed (jog {F(jogBack)} m/s, sprint {F(runBack)} m/s; without a tower {F(jog0)} and {F(run0)})");
            c.Check(ReferenceEquals(p.m_leftItem, tower) && Near(jogDrawn, jog1, 0.04f * p.m_speed), $"drawn again: slow again (jog {F(jogDrawn)} m/s)");

            // Heaviest armor of the game + tower (TESTING T30): sprint never slower than jog.
            var worn = new List<string>();
            foreach (var type in new[] { ItemType.Helmet, ItemType.Chest, ItemType.Legs, ItemType.Shoulder })
            {
                GameObject heaviest = null;
                var lowest = 0f;
                foreach (var go in ObjectDB.instance.m_items)
                {
                    var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                    var s = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
                    if (s == null || s.m_itemType != type || s.m_icons == null || s.m_icons.Length == 0 || s.m_movementModifier >= lowest
                        || !string.IsNullOrEmpty(s.m_dlc))
                    {
                        continue;
                    }
                    lowest = s.m_movementModifier;
                    heaviest = go;
                }
                var piece = heaviest != null ? rig.Give(heaviest.name) : null;
                if (piece != null && p.EquipItem(piece, false))
                {
                    worn.Add($"{heaviest.name} {F(lowest)}");
                }
            }
            p.UpdateModifiers();
            var total = p.GetEquipmentMovementModifier();
            var runSkill = p.GetSkillFactor(Skills.SkillType.Run);
            var vanillaSprint = p.m_runSpeed * (1f + runSkill * 0.25f) * (1f + total * 1.5f);
            yield return Go(false);
            var jogHeavy = v.Value;
            yield return Go(true);
            var runHeavy = v.Value;
            SelfTest.Note(SpeedName, $"heaviest armor found ({(worn.Count == 0 ? "none slows" : string.Join(", ", worn.ToArray()))}) + tower: movement modifier {F(total)}, "
                                     + $"jog {F(jogHeavy)} m/s, sprint {F(runHeavy)} m/s (the game's own sprint formula gives {F(vanillaSprint)} m/s)");
            c.Check(worn.Count > 0, "armor with a movement penalty exists and can be worn");
            c.Check(runHeavy >= jogHeavy - 0.03f && p.GetRunSpeedFactor() * p.m_runSpeed >= p.GetJogSpeedFactor() * p.m_speed - 0.001f,
                $"heaviest armor + tower: sprinting ({F(runHeavy)} m/s) is never slower than jogging ({F(jogHeavy)} m/s)");
            c.Report();
        }
        finally
        {
            rig.Done();
            if (rig.P != null)
            {
                rig.P.UpdateModifiers();
            }
        }
    }

    // ---------- tower.parry ----------

    // Adrenaline one AddAdrenaline(amount) call give this player now (vanilla formula: world rate, gain curve, effects).
    private static float AdrenalineFor(Player p, float amount)
    {
        var max = p.GetMaxAdrenaline();
        if (amount <= 0f || max <= 0f)
        {
            return 0f;
        }
        var v = amount * Game.m_adrenalineRate * p.m_adrenalineGainMultiplier.Evaluate(p.GetAdrenaline() / max);
        p.m_seman.ModifyAdrenaline(v, ref v);
        return v;
    }

    private sealed class Timed
    {
        internal float Stamina;
        internal float Adrenaline;
        internal float Landed;
        internal bool Staggered;
        internal Blow Blow;
        internal float BlockAdrenaline;
    }

    // One block timed right at the hit (shield raised 0.05 s ago: inside vanilla's 0.25 s parry window), 30 blunt from
    // the Greydwarf in front. What it cost and gave, and whether the Greydwarf got staggered by the block.
    private static IEnumerator TimedBlock(Player p, ItemDrop.ItemData shield, Character grey, float raised, Timed t)
    {
        yield return Ready(p, p.GetMaxStamina());
        yield return Until(() => !grey.IsStaggering(), 5f);
        p.m_adrenaline = 0f;
        var fwd = Flat(p.transform.forward);
        t.Blow = Expect(p, shield, 30f);
        t.BlockAdrenaline = AdrenalineFor(p, shield.m_shared.m_blockAdrenaline);
        var stamina = p.GetStamina();
        p.m_blockTimer = raised;
        var hit = Hit(p, -fwd, 30f, grey);
        p.RPC_Damage(0L, hit);
        t.Stamina = stamina - p.GetStamina();
        t.Adrenaline = p.GetAdrenaline();
        t.Landed = hit.m_damage.m_blunt;
        yield return Until(() => grey.IsStaggering(), 0.6f);
        t.Staggered = grey.IsStaggering();
    }

    private static IEnumerator RunParry()
    {
        var c = new Checks(ParryName);
        var rig = new Rig(ParryName);
        ItemDrop.ItemData ownTrinket = null;
        try
        {
            var p = rig.P;
            ownTrinket = p.m_trinketItem;
            p.SetGodMode(true);
            rig.TakeStaminaRate();
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var gold = rig.Give("ShieldGoldTower");
            var iron = rig.Give(IronTower);
            var banded = rig.Give("ShieldBanded");
            var grey = rig.Creature("Greydwarf");
            var goldSnap = SnapshotOf("ShieldGoldTower");
            if (gold == null || iron == null || banded == null || grey == null || goldSnap == null)
            {
                c.Check(false, "could not add ShieldGoldTower, ShieldIronTower and ShieldBanded or spawn a Greydwarf");
                c.Report();
                yield break;
            }
            // The Player prefab has no adrenaline bar of its own: a trinket give it (TESTING T39). Me wear the first
            // trinket of the game that give the bar room; none = the bar's size raised by hand (and a failed check).
            var bare = p.GetMaxAdrenaline();
            ItemDrop.ItemData trinket = null;
            foreach (var go in ObjectDB.instance.m_items)
            {
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
                if (shared == null || shared.m_itemType != ItemType.Trinket || shared.m_maxAdrenaline <= 0f || shared.m_icons == null || shared.m_icons.Length == 0
                    || !string.IsNullOrEmpty(shared.m_dlc))
                {
                    continue;
                }
                trinket = rig.Give(go.name);
                if (trinket != null && p.EquipItem(trinket, false))
                {
                    break;
                }
                trinket = null;
            }
            p.UpdateModifiers();
            var worn = trinket != null && p.GetMaxAdrenaline() > 0f;
            c.Check(Near(bare, 0f) || ownTrinket != null, $"without a trinket the adrenaline bar has no room ({F(bare)}): a block adds none (why TESTING T39 asks for a trinket)");
            c.Check(worn, $"a trinket gives the adrenaline bar its room ({(trinket != null ? trinket.m_dropPrefab.name : "no trinket found")}: max adrenaline {F(p.GetMaxAdrenaline())})");
            if (!worn)
            {
                p.m_maxAdrenaline = 100f;
            }
            SelfTest.Note(ParryName, $"Nord Greatshield in the normal game: parry bonus {F(goldSnap.TimedBlockBonus)}, parry adrenaline {F(goldSnap.PerfectBlockAdrenaline)}; "
                                     + $"Greydwarf staggers when parried: {grey.m_staggerWhenBlocked}");
            p.m_adrenaline = 0f;
            var t = new Timed();

            // Control: a round shield parry with this very block (else the tower checks below prove nothing).
            p.EquipItem(banded);
            yield return Guard(p, true);
            yield return TimedBlock(p, banded, grey, 0.05f, t);
            var parryAdrenaline = t.Adrenaline;
            c.Check(t.Staggered && grey.m_staggerWhenBlocked, $"control: a block with ShieldBanded timed like this parries (the Greydwarf is staggered: {t.Staggered})");
            SelfTest.Note(ParryName, $"control parry with ShieldBanded: adrenaline +{F(t.Adrenaline)}, stamina {F(t.Stamina)}");

            // Towers: never, however the block is timed (TESTING T07, T27).
            foreach (var tower in new[] { gold, iron })
            {
                var label = tower.m_dropPrefab.name;
                p.EquipItem(tower);
                yield return Guard(p, true);
                foreach (var raised in new[] { 0.01f, 0.05f, 0.12f, 0.24f })
                {
                    yield return TimedBlock(p, tower, grey, raised, t);
                    c.Check(!t.Staggered, $"{label}, shield raised {F(raised)} s before the hit: no parry, the Greydwarf is not staggered");
                    c.Check(Near(t.Stamina, t.Blow.Cost, 0.05f) && Near(t.Landed, t.Blow.Lands, 0.05f),
                        $"{label}, raised {F(raised)} s: a normal block (stamina {F(t.Stamina)}, {F(t.Blow.Cost)} expected; {F(t.Landed)} lands, {F(t.Blow.Lands)} expected)");
                    c.Check(t.BlockAdrenaline > 0f && Near(t.Adrenaline, t.BlockAdrenaline, 0.02f),
                        $"{label}, raised {F(raised)} s: adrenaline +{F(t.Adrenaline)} = the normal block amount {F(t.BlockAdrenaline)} "
                        + $"(block adrenaline {F(tower.m_shared.m_blockAdrenaline)} x world rate {F(Game.m_adrenalineRate)})");
                }
            }
            SelfTest.Note(ParryName, $"Nord Greatshield block adrenaline {F(gold.m_shared.m_blockAdrenaline)} (TESTING T27 says 2), never the parry amount "
                                     + $"({F(goldSnap.PerfectBlockAdrenaline)} in the normal game, {F(parryAdrenaline)} measured for a ShieldBanded parry)");
            c.Check(Near(gold.m_shared.m_blockAdrenaline, 2f), $"Nord Greatshield: the normal block amount is 2 ({F(gold.m_shared.m_blockAdrenaline)})");

            // A round shield listed as a tower lose its parry.
            yield return Guard(p, false);
            var listed = TowerRules.Defaults().With(r => r.Towers = TowerRules.DefaultTowers + ", ShieldBanded:7");
            yield return UseRules(listed);
            p.EquipItem(banded);
            yield return Guard(p, true);
            yield return TimedBlock(p, banded, grey, 0.05f, t);
            c.Check(banded.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft && !t.Staggered && Near(t.Stamina, t.Blow.Cost, 0.05f),
                $"ShieldBanded listed as a tower: the same timed block no longer parries (Greydwarf staggered {t.Staggered})");
            var bandedTip = banded.GetTooltip();
            c.Check(Only(p, banded, left: true) && Near(banded.m_shared.m_damages.m_blunt, 7f) && bandedTip.Contains("\n$item_twohanded") && bandedTip.Contains("Cannot parry")
                    && bandedTip.Contains("$inventory_blunt: <color=orange>7</color>") && !bandedTip.Contains("$item_parrybonus"),
                $"ShieldBanded listed with ':7': two-handed, a bash of 7 blunt, 'Cannot parry' on its tooltip ('{OneLine(bandedTip)}')");
            yield return Guard(p, false);
            yield return UseRules(rules);
            // The default list back (TESTING T40, end): the same held ShieldBanded is a round shield that parries again.
            if (!p.IsItemEquiped(banded))
            {
                p.EquipItem(banded);
            }
            yield return Guard(p, true);
            yield return TimedBlock(p, banded, grey, 0.05f, t);
            c.Check(banded.m_shared.m_itemType == ItemType.Shield && !banded.GetTooltip().Contains("Cannot parry") && t.Staggered,
                $"the default Towers list back: ShieldBanded is a round shield that parries again (type {banded.m_shared.m_itemType}, Greydwarf staggered {t.Staggered})");
            yield return Guard(p, false);
            c.Report();
        }
        finally
        {
            rig.Done();
            if (ownTrinket != null && rig.P != null && rig.Inv.ContainsItem(ownTrinket) && !rig.P.IsItemEquiped(ownTrinket))
            {
                rig.P.EquipItem(ownTrinket, false);
            }
        }
    }

    // ---------- tower.wall ----------

    private static IEnumerator RunWall()
    {
        var c = new Checks(WallName);
        var rig = new Rig(WallName);
        Player.Food meal = null;
        try
        {
            var p = rig.P;
            p.SetGodMode(true); // the guard-break punch lands 60: god mode keeps a small character alive
            rig.TakeStaminaRate();
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var iron = rig.Give(IronTower);
            var wood = rig.Give("ShieldWoodTower");
            var troll = rig.Creature("Troll");
            var grey = rig.Creature("Greydwarf");
            if (iron == null || wood == null || troll == null || grey == null)
            {
                c.Check(false, "could not add ShieldIronTower and ShieldWoodTower or spawn a Troll and a Greydwarf");
                c.Report();
                yield break;
            }
            Park(p, grey, 20f);
            // No body armor (Rig put it back), Blocking 0 before every numbered hit (each block train it).
            foreach (var item in new[] { p.m_helmetItem, p.m_chestItem, p.m_legItem, p.m_shoulderItem })
            {
                if (item != null)
                {
                    p.UnequipItem(item, false);
                }
            }
            // Food (TESTING T09: max health 75 or more, so the stagger threshold is 30 or more; not much more, a 60 hit
            // must still be able to stagger): the meal of the game nearest to +70 health put on the player's food
            // list, taken off again at the end.
            GameObject best = null;
            var bestGap = float.MaxValue;
            foreach (var go in ObjectDB.instance.m_items)
            {
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                var s = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
                if (s != null && s.m_itemType == ItemType.Consumable && s.m_foodBurnTime > 0f && s.m_icons != null && s.m_icons.Length > 0
                    && s.m_food >= 50f && Mathf.Abs(s.m_food - 70f) < bestGap)
                {
                    best = go;
                    bestGap = Mathf.Abs(s.m_food - 70f);
                }
            }
            if (best != null)
            {
                var data = best.GetComponent<ItemDrop>().m_itemData.Clone();
                data.m_dropPrefab = best;
                meal = new Player.Food
                {
                    m_name = best.name, m_item = data, m_time = data.m_shared.m_foodBurnTime, m_health = data.m_shared.m_food,
                    m_stamina = data.m_shared.m_foodStamina, m_eitr = data.m_shared.m_foodEitr,
                };
                p.m_foods.Add(meal);
                p.UpdateFood(0f, true);
                p.SetHealth(p.GetMaxHealth());
            }
            c.Check(p.GetMaxHealth() >= 75f,
                $"food eaten for the test ({(best != null ? best.name : "none found")}): max health {F(p.GetMaxHealth())}, stagger threshold {F(p.GetStaggerTreshold())}");
            var fwd = Flat(p.transform.forward);
            var front = -fwd;
            var behind = fwd;
            var scale = Game.instance.GetDifficultyDamageScalePlayer(p.transform.position) * Game.m_enemyDamageRate;
            var bare = Near(p.GetBodyArmor(), 0f) && Near(scale, 1f) && Near(Game.m_staminaRate, 1f) && Near(Game.m_localDamgeTakenRate, 1f);
            SelfTest.Note(WallName, $"body armor {F(p.GetBodyArmor())}, enemy damage scale {F(scale)}, stamina rate {F(Game.m_staminaRate)}, block stamina drain {F(p.m_blockStaminaDrain)}: "
                                    + (bare ? "the numbers of TESTING T08 / T09 are checked as written" : "not the conditions of TESTING T08 / T09: only the formula is checked"));
            c.Check(bare, "test conditions: no body armor, default world modifiers, one player");

            // TESTING T08: Troll punch (60 blunt) through the Iron tower at Blocking 0: about 7 damage and 4 stamina.
            p.EquipItem(iron);
            yield return Guard(p, true);
            yield return Ready(p, p.GetMaxStamina());
            var trollItems = troll is Humanoid trollBody ? trollBody.GetInventory().GetAllItems() : new List<ItemDrop.ItemData>();
            // What of an attack item reach this player: its damages through the player's own resistances, as vanilla
            // RPC_Damage do (HitData.ApplyResistance). A Troll's attacks carry chop and pickaxe damage too (trees and
            // rocks): DamageTypes.GetTotalDamage count them (first run read 200 for the punch), a player take none.
            float OnPlayer(ItemDrop.ItemData i)
            {
                var probe = new HitData();
                probe.m_damage = i.m_shared.m_damages;
                probe.ApplyResistance(p.GetDamageModifiers(), out _);
                return probe.m_damage.GetTotalDamage();
            }
            var punches = trollItems.Where(i => i != null && i.m_shared != null && i.m_dropPrefab != null && i.m_dropPrefab.name == "troll_punch").ToList();
            c.Check(punches.Count > 0 && punches.All(i => i.m_shared.m_blockable && Near(i.m_shared.m_damages.m_blunt, 60f) && Near(OnPlayer(i), 60f)),
                "a Troll's punch (troll_punch) is blockable and 60 blunt on a player, the hit this test uses ("
                + string.Join(", ", trollItems.Where(i => i != null && i.m_shared != null).Select(i =>
                    $"{(i.m_dropPrefab != null ? i.m_dropPrefab.name : "?")}: blunt {F(i.m_shared.m_damages.m_blunt)}, on a player {F(OnPlayer(i))}, "
                    + $"with chop and pickaxe {F(i.m_shared.m_damages.GetTotalDamage())}, blockable {i.m_shared.m_blockable}").ToArray()) + ")");
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            var blow = Expect(p, iron, 60f);
            var stamina = p.GetStamina();
            var health = p.GetHealth();
            var hit = Hit(p, front, 60f, troll);
            p.RPC_Damage(0L, hit);
            var lost = health - p.GetHealth();
            var used = stamina - p.GetStamina();
            c.Check(Near(lost, blow.Lands, 0.05f) && Near(used, blow.Cost, 0.05f),
                $"Troll punch blocked with the Iron tower at Blocking 0: {F(lost)} damage, {F(used)} stamina ({blow.Text})");
            if (bare)
            {
                c.Check(Near(lost, 6.9f, 0.4f) && Near(used, 4.1f, 0.3f), $"that is about 7 damage and 4 stamina ({F(lost)}, {F(used)})");
            }
            // The same shield with the normal game's data (empty Towers list): about 17 and 8.
            yield return UseRules(TowerRules.Defaults().With(r => r.Towers = ""));
            yield return Ready(p, p.GetMaxStamina());
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            blow = Expect(p, iron, 60f);
            stamina = p.GetStamina();
            health = p.GetHealth();
            hit = Hit(p, front, 60f, troll);
            p.RPC_Damage(0L, hit);
            lost = health - p.GetHealth();
            used = stamina - p.GetStamina();
            c.Check(iron.m_shared.m_itemType == ItemType.Shield && Near(lost, blow.Lands, 0.05f) && Near(used, blow.Cost, 0.05f),
                $"the same punch on a normal Iron tower shield: {F(lost)} damage, {F(used)} stamina ({blow.Text})");
            if (bare)
            {
                c.Check(Near(lost, 17.3f, 0.6f) && Near(used, 8.2f, 0.4f), $"that is about 17 damage and 8 stamina ({F(lost)}, {F(used)})");
            }
            yield return UseRules(rules);

            // TESTING T25: hit from behind while braced: full damage, little stagger. T11: not blocked.
            yield return Ready(p, p.GetMaxStamina());
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            blow = Expect(p, iron, 60f);
            stamina = p.GetStamina();
            health = p.GetHealth();
            var threshold = p.GetStaggerTreshold();
            hit = Hit(p, behind, 60f, grey);
            p.RPC_Damage(0L, hit);
            lost = health - p.GetHealth();
            var bar = p.m_staggerDamage;
            var landed = Mathf.Min(blow.Unblocked, health - 1f); // god mode keeps 1 health
            c.Check(Near(hit.m_damage.m_blunt, blow.Unblocked, 0.1f) && Near(lost, landed, 0.1f) && Near(stamina - p.GetStamina(), 0f, 0.01f),
                $"Greydwarf hit from behind while braced: not blocked, the full {F(hit.m_damage.m_blunt)} lands ({F(blow.Unblocked)} expected), no block stamina");
            c.Check(Near(bar, Mathf.Min(threshold, blow.Unblocked * 0.2f), 0.3f),
                $"from behind: stagger bar +{F(bar)} = 20% of the {F(blow.Unblocked)} that landed (threshold {F(threshold)}; without the brace it would be {F(blow.Unblocked)})");
            yield return Seconds(0.4f);
            c.Check(blow.Unblocked >= threshold && blow.Unblocked * 0.2f < threshold && !p.IsStaggering(),
                $"from behind: not staggered by a hit that would stagger without the brace ({F(blow.Unblocked)} against a threshold of {F(threshold)})");

            // TESTING T11: fall damage and fire as the game make them.
            yield return Ready(p, p.GetMaxStamina());
            stamina = p.GetStamina();
            health = p.GetHealth();
            var fall = new HitData();
            fall.m_damage.m_damage = 10f;
            fall.m_point = p.transform.position;
            fall.m_dir = Vector3.up;
            fall.m_hitType = HitData.HitType.Fall;
            p.RPC_Damage(0L, fall);
            c.Check(!fall.m_blockable && health - p.GetHealth() > 5f && Near(stamina - p.GetStamina(), 0f, 0.01f),
                $"fall damage while braced: not blocked (health -{F(health - p.GetHealth())}, no block stamina)");
            yield return Ready(p, p.GetMaxStamina());
            stamina = p.GetStamina();
            var fire = new HitData(); // what a fire's damage area sends (Aoe.OnHit without owner): no attacker, ranged, not blockable
            fire.m_damage.m_fire = 25f;
            fire.m_point = p.GetCenterPoint() - front * 0.3f;
            fire.m_dir = front;
            fire.m_ranged = true;
            fire.m_hitType = HitData.HitType.EnemyHit;
            p.RPC_Damage(0L, fire);
            var burning = p.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectBurning);
            c.Check(!fire.m_blockable && burning && Near(stamina - p.GetStamina(), 0f, 0.01f),
                $"fire from the front while braced (a fire's damage area has no attacker): not blocked, the player burns ({burning})");
            p.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectBurning, true);

            // TESTING T22: the punch that break the guard push; with BraceKnockbackResistPercent 0 blocked punches push
            // (less than unblocked ones).
            yield return Ready(p, Brace.Reserve(p) * 0.8f);
            hit = Hit(p, front, 60f, troll);
            hit.m_pushForce = 100f;
            p.RPC_Damage(0L, hit);
            var lowPush = p.m_pushForce.magnitude;
            c.Check(lowPush > 0.1f && p.IsKnockedBack(), $"stamina at one block or less: a frontal punch pushes again (push {F(lowPush)})");
            yield return Ready(p, p.GetMaxStamina());
            hit = Hit(p, front, 60f, troll);
            hit.m_pushForce = 100f;
            p.RPC_Damage(0L, hit);
            c.Check(Near(p.m_pushForce.magnitude, 0f, 0.001f), $"control at 100: a blocked frontal punch does not push (push {F(p.m_pushForce.magnitude)})");
            yield return UseRules(TowerRules.Defaults().With(r => r.BraceKnockbackResistPercent = 0));
            yield return Ready(p, p.GetMaxStamina());
            hit = Hit(p, front, 60f, troll);
            hit.m_pushForce = 100f;
            p.RPC_Damage(0L, hit);
            var blockedPush = p.m_pushForce.magnitude;
            yield return Ready(p, p.GetMaxStamina());
            hit = Hit(p, behind, 60f, troll);
            hit.m_pushForce = 100f;
            p.RPC_Damage(0L, hit);
            var freePush = p.m_pushForce.magnitude;
            c.Check(blockedPush > 0.1f && blockedPush < freePush - 0.1f,
                $"BraceKnockbackResistPercent 0: a blocked punch pushes again ({F(blockedPush)}), less than an unblocked one ({F(freePush)})");
            yield return UseRules(rules);

            // TESTING M08 (one game only): a bash from a player, PvP on, on the braced bearer: blocked, little stagger,
            // no push. The local player stand in for the attacker.
            yield return Ready(p, p.GetMaxStamina());
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            p.SetPVP(true);
            blow = Expect(p, iron, 12f / Mathf.Max(0.0001f, scale)); // a player's hit is not scaled like a creature's
            var bash = BashHit(p, p, 12f, rules.BashStagger);
            bash.m_dir = front;
            bash.m_point = p.GetCenterPoint() - front * 0.3f;
            bash.m_pushForce = 40f;
            stamina = p.GetStamina();
            p.RPC_Damage(0L, bash);
            bar = p.m_staggerDamage;
            var most = (blow.Through + blow.Lands * rules.BashStagger) * 0.2f;
            c.Check(stamina - p.GetStamina() > 0.1f && bash.m_damage.m_blunt < 1f && bar <= most + 0.2f && Near(p.m_pushForce.magnitude, 0f, 0.001f),
                $"a PvP bash on a braced tower shield player: blocked ({F(bash.m_damage.m_blunt)} of 12 lands), stagger bar +{F(bar)} "
                + $"(at most {F(most)}: the vanilla x{F(rules.BashStagger)} of what landed, 80% less), not pushed");
            p.SetPVP(false);

            // TESTING T09: Wood tower against Troll punches until the guard break.
            p.EquipItem(wood);
            yield return Guard(p, true);
            threshold = p.GetStaggerTreshold();
            var reserve = Brace.Reserve(p);
            var se = Brace.Clone;
            if (se == null)
            {
                c.Check(false, "no Braced effect with the Wood tower");
                c.Report();
                yield break;
            }
            // Four and a half blocks of stamina, regen held; punches 2.4 s apart (the stagger bar drain between them).
            yield return Ready(p, reserve * 4.5f);
            for (var i = 1; i <= 4; i++)
            {
                SetSkill(p, Skills.SkillType.Blocking, 0f);
                p.SetHealth(p.GetMaxHealth());
                blow = Expect(p, wood, 60f);
                stamina = p.GetStamina();
                health = p.GetHealth();
                var hitAt = Time.time;
                hit = Hit(p, front, 60f, troll);
                p.RPC_Damage(0L, hit);
                lost = health - p.GetHealth();
                used = stamina - p.GetStamina();
                c.Check(Near(lost, blow.Lands, 0.1f) && Near(used, blow.Cost, 0.1f),
                    $"Wood tower, punch {i} from {F(stamina)} stamina: {F(lost)} gets through, {F(used)} stamina ({blow.Text})");
                if (bare)
                {
                    c.Check(Near(lost, 35f, 1.5f) && Near(used, 10f, 0.6f), $"punch {i}: about 35 damage and 10 stamina ({F(lost)}, {F(used)})");
                }
                yield return Seconds(0.4f);
                c.Check(!p.IsStaggering(), $"punch {i}: not staggered while braced with stamina (bar {F(p.m_staggerDamage)} of {F(threshold)})");
                var left = p.GetStamina();
                var exhausted = left <= reserve;
                c.Check(se.m_name == (exhausted ? Brace.ExhaustedText : Brace.BracedText) && se.m_flashIcon == exhausted && !se.m_hidden,
                    $"after punch {i}, {F(left)} stamina (one block = {F(reserve)}): HUD '{se.m_name}'{(se.m_flashIcon ? ", flashing" : "")}");
                c.Check(i < 4 ? !exhausted : exhausted, $"after punch {i}: {(i < 4 ? "still braced" : "down to one block or less: exhausted")}");
                p.m_staminaRegenTimer = 30f;
                yield return UntilTime(hitAt + 2.4f);
            }
            p.SetHealth(p.GetMaxHealth());
            blow = Expect(p, wood, 60f);
            health = p.GetHealth();
            hit = Hit(p, front, 60f, troll);
            hit.m_pushForce = 100f;
            p.RPC_Damage(0L, hit);
            var breakPush = p.m_pushForce.magnitude;
            yield return Until(() => p.IsStaggering(), 0.6f);
            c.Check(Near(hit.m_damage.m_blunt, blow.Unblocked, 0.1f) && p.IsStaggering(),
                $"the next punch lands in full ({F(hit.m_damage.m_blunt)}, {F(blow.Unblocked)} expected) and staggers the player ({p.IsStaggering()})");
            c.Check(Near(p.GetStamina(), 0f, 0.001f) && breakPush > 0.1f, $"the punch that breaks the guard pushes the player (push {F(breakPush)})");
            // The brace come back only above one block of stamina.
            yield return Ready(p, reserve - 0.2f);
            c.Check(se.m_name == Brace.ExhaustedText && se.m_flashIcon, $"stamina just below one block ({F(reserve - 0.2f)}): still '{se.m_name}'");
            yield return Ready(p, reserve + 0.2f);
            c.Check(se.m_name == Brace.BracedText && !se.m_flashIcon && se.m_staggerModifier < -0.5f, $"stamina just above one block ({F(reserve + 0.2f)}): '{se.m_name}' again");
            c.Report();
        }
        finally
        {
            if (meal != null && rig.P != null)
            {
                rig.P.m_foods.Remove(meal);
                rig.P.UpdateFood(0f, true);
            }
            rig.Done();
        }
    }

    // ---------- tower.push ----------

    private static IEnumerator RunPush()
    {
        var c = new Checks(PushName);
        var rig = new Rig(PushName);
        try
        {
            var p = rig.P;
            p.SetGodMode(true);
            rig.TakeStaminaRate();
            var rules = TowerRules.Defaults();
            var half = TowerRules.Defaults().With(r => r.BlockForcePercent = 50);
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var grey = rig.Creature("Greydwarf");
            var draugr = rig.Creature("Draugr");
            if (tower == null || grey == null || draugr == null)
            {
                c.Check(false, "could not add a ShieldIronTower or spawn a Greydwarf and a Draugr");
                c.Report();
                yield break;
            }
            p.EquipItem(tower);
            yield return Guard(p, true);
            var scale = Game.instance.GetDifficultyDamageScalePlayer(p.transform.position) * Game.m_enemyDamageRate;
            var range = TowerSync.Applied != null ? TowerSync.Applied.BashRange : TowerRules.DefaultBashRange;
            foreach (var attacker in new[] { grey, draugr })
            {
                var other = ReferenceEquals(attacker, grey) ? draugr : grey;
                var pushes = new List<float>();
                foreach (var set in new[] { rules, half })
                {
                    yield return UseRules(set);
                    Park(p, other);
                    yield return Ready(p, p.GetMaxStamina());
                    SetSkill(p, Skills.SkillType.Blocking, 0f);
                    var start = PlaceInFront(p, attacker);
                    yield return Fixed;
                    attacker.m_pushForce = Vector3.zero;
                    var armor = tower.GetBlockPower(p.GetSkillFactor(Skills.SkillType.Blocking));
                    var blunt = 30f * scale;
                    var fraction = Mathf.Clamp01((blunt - Through(blunt, armor)) / armor);
                    var force = tower.GetDeflectionForce() * (1f - Mathf.Clamp01(fraction * 0.5f));
                    var expected = force / attacker.m_body.mass * 2.5f;
                    var hit = Hit(p, -Flat(p.transform.forward), 30f, attacker); // a melee hit: not ranged
                    p.RPC_Damage(0L, hit);
                    var push = attacker.m_pushForce.magnitude;
                    pushes.Add(push);
                    c.Check(Near(push, expected, 0.03f * expected + 0.01f),
                        $"{attacker.m_name} blocked at BlockForcePercent {set.BlockForcePercent}: pushed back {F(push)} (block force {F(tower.GetDeflectionForce())} "
                        + $"x {F(1f - Mathf.Clamp01(fraction * 0.5f))} / mass {F(attacker.m_body.mass)} x 2.5 = {F(expected)})");
                    yield return Seconds(1f);
                    var end = Flat3(attacker.transform.position - p.transform.position).magnitude;
                    SelfTest.Note(PushName, $"{attacker.m_name} at BlockForcePercent {set.BlockForcePercent}: from {F(start)} m to {F(end)} m from the player (centre to centre; its near side at "
                                            + $"{F(end - Radius(attacker))} m, bash range {F(range)} m: {(end - Radius(attacker) > range ? "beyond bash range" : "still within bash range")})");
                }
                c.Check(pushes.Count == 2 && pushes[0] > 0.05f && Near(pushes[1] / pushes[0], 0.5f, 0.03f),
                    $"{attacker.m_name}: half the push at BlockForcePercent 50 ({F(pushes.Count == 2 ? pushes[1] : 0f)} against {F(pushes.Count > 0 ? pushes[0] : 0f)})");
            }
            yield return UseRules(rules);
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    private static Vector3 Flat3(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    // ---------- tower.poison ----------

    private static float PoisonLeft(Player p)
    {
        var se = p.GetSEMan().GetStatusEffect(SEMan.s_statusEffectPoison) as SE_Poison;
        return se != null ? se.m_damageLeft : 0f;
    }

    // "Blocked: n" texts on screen now (DamageText).
    private static HashSet<object> BlockedTexts()
    {
        var found = new HashSet<object>();
        var dt = DamageText.instance;
        if (dt == null)
        {
            return found;
        }
        var word = Localization.instance.Localize("$msg_blocked");
        foreach (var text in dt.m_worldTexts)
        {
            if (text != null && text.m_textField != null && text.m_textField.text != null && text.m_textField.text.StartsWith(word, StringComparison.Ordinal))
            {
                found.Add(text);
            }
        }
        return found;
    }

    private sealed class Struck
    {
        internal bool Started;
        internal bool Hit;
        internal Brace.DebugHit Record;
        internal float Poison;
        internal bool BlockedText;
        internal string Item = "";
        internal string Setup = "";
    }

    private static ItemDrop.ItemData StrikeItem(Humanoid creature, string itemPrefab) =>
        creature.GetInventory().GetAllItems().FirstOrDefault(i => i != null && i.m_dropPrefab != null && i.m_dropPrefab.name == itemPrefab)
        ?? creature.GetInventory().GetAllItems().FirstOrDefault(i => i != null && i.IsWeapon() && !i.m_shared.m_blockable && i.HavePrimaryAttack());

    // Creature where its AI would attack from, on this side of the player (flat direction from the player to it):
    // near, looking at the player, the player its target, still. AI is off, so me do what the AI do before an attack:
    //   - look: a creature's body turn toward its look yaw every tick (Character.UpdateRotation); with no AI that yaw
    //     never followed the body me placed, and a sprayed attack would leave where the body turned to;
    //   - target: a projectile attack aim at the AI's target (Attack.GetProjectileSpawnPoint);
    //   - distance: a projectile (or the cloud a spray make) leave m_attackRange ahead of the creature: half a metre
    //     left before the player.
    // A blocked hit push the attacker metres away (tower.push): placed again before every strike.
    private static string Aim(Player p, Humanoid creature, ItemDrop.ItemData item, Vector3 side)
    {
        var attack = item != null ? item.m_shared.m_attack : null;
        var gap = 0.3f;
        var projectile = attack != null && (attack.m_attackType == Attack.AttackType.Projectile || attack.m_attackType == Attack.AttackType.TriggerProjectile);
        if (projectile)
        {
            gap = Mathf.Max(gap, attack.m_attackRange + 0.5f);
        }
        var distance = Radius(p) + Radius(creature) + gap;
        var pos = p.transform.position + side * distance;
        var rot = Quaternion.LookRotation(-side);
        creature.transform.SetPositionAndRotation(pos, rot);
        var body = creature.m_body;
        if (body != null)
        {
            body.position = pos;
            body.rotation = rot;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
            }
        }
        creature.m_pushForce = Vector3.zero;
        creature.SetLookDir(-side);
        var ai = creature.GetComponent<MonsterAI>();
        if (ai != null)
        {
            ai.m_targetCreature = p;
        }
        return attack == null
            ? "no attack"
            : $"attack type {attack.m_attackType}, attack range {F(attack.m_attackRange)}, AI attack range {F(item.m_shared.m_aiAttackRange)}, "
              + $"projectile {(attack.m_attackProjectile != null ? attack.m_attackProjectile.name : "none")}, placed {F(distance)} m away (centre to centre)";
    }

    // The creature's own attack (AI off: me start it like its AI would) from this side of the player (flat direction
    // from the player to the creature). Wait for its hit on the player, then read what the brace decided, the poison
    // left on player and whether a "Blocked" text came up.
    private static IEnumerator Strike(Player p, Humanoid creature, string itemPrefab, Vector3 side, Struck s)
    {
        s.Started = false;
        s.Hit = false;
        s.Poison = 0f;
        s.BlockedText = false;
        s.Setup = "";
        var item = StrikeItem(creature, itemPrefab);
        if (item == null)
        {
            s.Item = "no unblockable attack item (" + string.Join(", ", creature.GetInventory().GetAllItems().Select(i => i.m_dropPrefab != null ? i.m_dropPrefab.name : "?").ToArray()) + ")";
            yield break;
        }
        s.Item = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
        yield return Until(() => !creature.InAttack() && !creature.IsStaggering(), 4f);
        yield return Seconds(0.3f); // a sprayed attack's last projectiles land
        yield return Ready(p, p.GetMaxStamina());
        if (!creature.IsItemEquiped(item))
        {
            creature.EquipItem(item, false);
        }
        s.Setup = Aim(p, creature, item, side);
        yield return Fixed;
        yield return Fixed;
        var texts = BlockedTexts();
        var count = Brace.DebugLast.Count;
        s.Started = creature.StartAttack(p, false);
        yield return Until(() => Brace.DebugLast.Count > count && ReferenceEquals(Brace.DebugLast.Attacker, creature), 6f);
        s.Hit = Brace.DebugLast.Count > count && ReferenceEquals(Brace.DebugLast.Attacker, creature);
        s.Record = Brace.DebugLast;
        yield return null;
        s.Poison = PoisonLeft(p);
        s.BlockedText = BlockedTexts().Any(t => !texts.Contains(t));
        var to = Flat3(p.transform.position - creature.transform.position);
        s.Setup += $"; after the attack {F(to.magnitude)} m away, facing {F(Vector3.Angle(Flat(creature.transform.forward), to))} degrees off the player";
        p.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectPoison, true);
    }

    private static IEnumerator RunPoison()
    {
        var c = new Checks(PoisonName);
        var rig = new Rig(PoisonName);
        try
        {
            var p = rig.P;
            p.SetGodMode(true);
            rig.TakeStaminaRate();
            var rules = TowerRules.Defaults();
            var off = TowerRules.Defaults().With(r => r.BlockUnblockableAttacks = false);
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            if (tower == null)
            {
                c.Check(false, "could not add a ShieldIronTower");
                c.Report();
                yield break;
            }
            p.EquipItem(tower);
            yield return Guard(p, true);
            var facing = Flat(p.transform.forward);
            var s = new Struck();
            foreach (var pair in new[]
                     {
                         new KeyValuePair<string, string>("Blob", "blob_attack_aoe"),
                         new KeyValuePair<string, string>("Greydwarf_Shaman", "Greydwarf_shaman_attack"),
                     })
            {
                var creature = rig.Creature(pair.Key) as Humanoid;
                yield return Frames(4); // Humanoid.Start gives its attack items
                if (creature == null)
                {
                    c.Check(false, $"could not spawn a {pair.Key}");
                    continue;
                }
                // From the front, braced. The creature stand on the same side (facing) in all three strikes; the player
                // turn. First run: the blocked Blob was pushed away by the block and its second attack reached nobody,
                // and the Shaman's spray never reached the player from 0.3 m with no look direction: Strike now place
                // and aim the creature before each attack.
                SetSkill(p, Skills.SkillType.Blocking, 0f);
                Face(p, facing);
                yield return Fixed;
                yield return Strike(p, creature, pair.Value, facing, s);
                SelfTest.Note(PoisonName, $"{pair.Key} attack item {s.Item}, from the front: started {s.Started}, hit the player {s.Hit} ({s.Setup})");
                c.Check(s.Started && s.Hit, $"{pair.Key}: its attack ({s.Item}) hits the braced player from the front");
                if (!s.Hit)
                {
                    ZNetScene.instance.Destroy(creature.gameObject);
                    continue;
                }
                var poisonFront = s.Poison;
                c.Check(!s.Record.WasBlockable && s.Record.Braced && s.Record.MadeBlockable,
                    $"{pair.Key} from the front: the game marks its attack unblockable ({!s.Record.WasBlockable}), braced it is blocked ({s.Record.MadeBlockable})");
                c.Check(s.BlockedText, $"{pair.Key} from the front: the 'Blocked' text shows");
                // From behind: player turn its back.
                Face(p, -facing);
                yield return FixedSteps(3);
                yield return Strike(p, creature, pair.Value, facing, s);
                var poisonBehind = s.Poison;
                SelfTest.Note(PoisonName, $"{pair.Key} from behind: started {s.Started}, hit the player {s.Hit}, poison {F(poisonBehind)} against {F(poisonFront)} from the front ({s.Setup})");
                c.Check(s.Started && s.Hit && s.Record.Braced && !s.Record.WasBlockable && !s.Record.MadeBlockable && poisonBehind > 0.5f && !s.BlockedText,
                    $"{pair.Key} from behind: not blocked, the full poison lands ({F(poisonBehind)} poison on the player, hit {s.Hit})");
                c.Check(poisonBehind > 0.5f && poisonFront <= poisonBehind * 0.2f + 0.01f,
                    $"{pair.Key}: braced from the front a fifth or less of the poison gets through ({F(poisonFront)} of {F(poisonBehind)})");
                // Setting off: from the front as in the normal game.
                Face(p, facing);
                yield return UseRules(off);
                yield return Strike(p, creature, pair.Value, facing, s);
                c.Check(s.Started && s.Hit && s.Record.Braced && !s.Record.MadeBlockable && !s.BlockedText && poisonBehind > 0.5f && s.Poison >= poisonBehind * 0.8f,
                    $"{pair.Key}, BlockUnblockableAttacks off, from the front: the poison goes through as in the normal game ({F(s.Poison)} of {F(poisonBehind)})");
                yield return UseRules(rules);
                ZNetScene.instance.Destroy(creature.gameObject);
                yield return null;
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }
}
#endif
