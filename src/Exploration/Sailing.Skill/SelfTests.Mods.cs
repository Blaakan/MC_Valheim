#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using MC.Shared;
using TMPro;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Debug build only. Me = checks with another MC mod loaded in the same game (same class as SelfTests.cs). Soft: other
// mod found by GUID in the shared registry (FeatureRegistry) and looked at by reflection or by what the game shows,
// never through its types at compile time. Other mod not loaded and active = FAIL (a skipped check is no proof).
//   sailing.encyclopedia  X01: Encyclopedia window opened (its own Open, what its tab calls), a sword, a tool and a
//                         food page shown through its search and its own "next entry", with no Sailing skill and with
//                         Sailing 0: the entry is neither added nor removed, and the Skills panel shows no row / the
//                         level-0 row afterwards
//   sailing.sneak         X03: Sneak Ambush. Passenger crouched and still on the deck of a ship that lies still (hull
//                         held in place): its holding-still bonus comes on. Sail up, ship under way: the bonus ends
//                         and never comes back while the ship moves (its state, and its status icon when shown);
//                         Sneak XP and Sailing XP unchanged all the way
// sailing.input.dive (Swim Dive, X02) is in SelfTests.Input.cs: it holds a real key.
internal static partial class SelfTests
{
    private const string EncyclopediaName = "sailing.encyclopedia";
    private const string SneakName = "sailing.sneak";

    private const string EncyclopediaGuid = "MC.Exploration.Compendium.Encyclopedia";
    private const string SneakAmbushGuid = "MC.Combat.Sneak.Ambush";

    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // ---------- other mod, by GUID ----------

    private static bool OtherActive(string guid, out string state)
    {
        var view = FeatureRegistry.Find(guid);
        state = view == null ? "not loaded" : $"{view.Value.State}: {view.Value.Status}";
        return view != null && view.Value.IsActive;
    }

    // Type of another plugin by its short name (their namespace is their business). Null = plugin or type missing.
    private static Type OtherType(string guid, string typeName)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info == null || info.Instance == null)
        {
            return null;
        }
        Type[] types;
        try
        {
            types = info.Instance.GetType().Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types;
        }
        foreach (var t in types)
        {
            if (t != null && t.Name == typeName)
            {
                return t;
            }
        }
        return null;
    }

    // Static property or field of their type. Null = not there.
    private static object OtherValue(Type type, string member)
    {
        if (type == null)
        {
            return null;
        }
        try
        {
            var property = type.GetProperty(member, AnyStatic);
            if (property != null)
            {
                return property.GetValue(null, null);
            }
            var field = type.GetField(member, AnyStatic);
            return field != null ? field.GetValue(null) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? OtherBool(Type type, string member) => OtherValue(type, member) as bool?;

    // Static method of their type with these arguments. found false = no such method.
    private static object OtherCall(Type type, string method, out bool found, params object[] args)
    {
        found = false;
        if (type == null)
        {
            return null;
        }
        var types = new Type[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            types[i] = args[i].GetType();
        }
        var m = type.GetMethod(method, AnyStatic, null, types, null);
        if (m == null)
        {
            return null;
        }
        found = true;
        return m.Invoke(null, args);
    }

    // ---------- sailing.encyclopedia (X01) ----------

    private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);

    private static string Plain(string text) => Tags.Replace(text ?? "", "").Trim();

    // First craftable item of each kind whose page reads skill levels: a sword, a tool, a food. Only items the game
    // names (the Encyclopedia lists no item without a name) and that a recipe makes (no test or leftover item).
    private static List<ItemDrop.ItemData> PagesToRead(Localization loc)
    {
        var result = new List<ItemDrop.ItemData>();
        var db = ObjectDB.instance;
        if (db == null)
        {
            return result;
        }
        ItemDrop.ItemData weapon = null;
        ItemDrop.ItemData tool = null;
        ItemDrop.ItemData food = null;
        foreach (var go in db.m_items)
        {
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            var data = drop != null ? drop.m_itemData : null;
            var shared = data != null ? data.m_shared : null;
            if (shared == null || shared.m_icons == null || shared.m_icons.Length == 0 || shared.m_icons[0] == null
                || string.IsNullOrEmpty(shared.m_name) || !string.IsNullOrEmpty(shared.m_dlc))
            {
                continue;
            }
            var name = loc.Localize(shared.m_name);
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0 || name.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }
            var recipe = db.GetRecipe(data);
            if (recipe == null || !recipe.m_enabled)
            {
                continue;
            }
            if (weapon == null && shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon && shared.m_skillType == Skills.SkillType.Swords)
            {
                weapon = data;
            }
            else if (tool == null && shared.m_itemType == ItemDrop.ItemData.ItemType.Tool)
            {
                tool = data;
            }
            else if (food == null && shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && shared.m_food > 0f)
            {
                food = data;
            }
        }
        foreach (var item in new[] { weapon, tool, food })
        {
            if (item != null)
            {
                result.Add(item);
            }
        }
        return result;
    }

    // Title of the page the Encyclopedia shows now ("" = none or its title label was not found).
    private static string PageTitle(Type window)
    {
        var label = OtherValue(window, "TopicText") as TMP_Text;
        return label != null ? Plain(label.text) : "";
    }

    // Lines of the page it shows now (-1 = its line list was not found).
    private static int PageLines(Type window)
    {
        return OtherValue(window, "RenderedLines") is IEnumerable<string> lines ? lines.Count() : -1;
    }

    private static void CloseEncyclopedia(Type window, InventoryGui gui)
    {
        if (OtherBool(window, "IsOpen") == true)
        {
            OtherCall(window, "Close", out _, false);
        }
        if (gui != null && InventoryGui.IsVisible())
        {
            ClosePanel();
        }
    }

    private static IEnumerator RunEncyclopedia()
    {
        var c = new Checks(EncyclopediaName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var loc = Localization.instance;
        if (player == null || player.GetSkills() == null || gui == null || loc == null || ObjectDB.instance == null)
        {
            SelfTest.Fail(EncyclopediaName, "no local player or no inventory UI");
            yield break;
        }
        if (!OtherActive(EncyclopediaGuid, out var bookState))
        {
            // No pass without a check: a skipped test must not read as proof.
            SelfTest.Fail(EncyclopediaName, $"Encyclopedia is not loaded and active ({bookState}): nothing of TESTING.md X01 was checked");
            yield break;
        }
        var window = OtherType(EncyclopediaGuid, "CompendiumWindow");
        if (window == null)
        {
            SelfTest.Fail(EncyclopediaName, "the Encyclopedia's window (type CompendiumWindow) was not found: nothing of TESTING.md X01 was checked");
            yield break;
        }
        var skills = player.GetSkills();
        var type = SailingSkill.Type;
        var watch = LogMark();
        var backup = new ZPackage();
        skills.Save(backup);
        var known = new HashSet<string>(player.m_knownMaterial);
        TMP_InputField search = null;
        string searchWas = null;
        try
        {
            var items = PagesToRead(loc);
            if (!c.Check(items.Count == 3, $"no craftable sword, tool and food found in the game's items ({items.Count} of 3): no item page to read"))
            {
                c.Report();
                yield break;
            }
            // The character has held them once (the Encyclopedia lists only what the character knows).
            foreach (var item in items)
            {
                player.m_knownMaterial.Add(item.m_shared.m_name);
            }
            var names = string.Join(", ", items.Select(i => loc.Localize(i.m_shared.m_name)).ToArray());

            foreach (var present in new[] { false, true })
            {
                var when = present ? "Sailing at level 0" : "no Sailing skill";
                skills.ResetSkill(type);
                if (present)
                {
                    SetSailing(player, 0f, 0f);
                }
                HelmSkill.Invalidate();

                // Inventory shown (its slide-in over), then the Encyclopedia's own Open: what its tab calls.
                yield return ShowInventory(c);
                yield return new WaitForSecondsRealtime(0.8f);
                var opened = OtherCall(window, "Open", out var found) is bool o && o;
                var ready = new Box();
                yield return WaitFor(() => OtherBool(window, "IsOpen") == true && OtherBool(window, "Preparing") == false, 30f, ready);
                search = OtherValue(window, "SearchField") as TMP_InputField;
                if (!c.Check(found && opened && ready.Ok && search != null && search.gameObject.activeInHierarchy,
                        $"{when}: the Encyclopedia window did not open with its list and search field (Open found {found}, answered {opened}, "
                        + $"open {OtherBool(window, "IsOpen")}, still preparing {OtherBool(window, "Preparing")}, search field {search != null})"))
                {
                    CloseEncyclopedia(window, gui);
                    yield return null;
                    yield return null;
                    continue;
                }
                if (searchWas == null)
                {
                    searchWas = search.text ?? "";
                }

                // The three pages by name: text in its search field (as typing), the list shows what matches after
                // 0.1 s and the first match's page; a longer name may match first: its own "next entry" down the list.
                var pages = 0;
                foreach (var item in items)
                {
                    var name = Plain(loc.Localize(item.m_shared.m_name));
                    search.text = name;
                    yield return new WaitForSecondsRealtime(0.35f);
                    yield return null;
                    var title = "";
                    var shown = false;
                    for (var step = 0; step < 12; step++)
                    {
                        title = PageTitle(window);
                        if (string.Equals(title, name, StringComparison.OrdinalIgnoreCase))
                        {
                            shown = true;
                            break;
                        }
                        var moved = OtherCall(window, "MoveSelection", out found, 1) is bool m && m;
                        if (!found || !moved)
                        {
                            break;
                        }
                        yield return null;
                    }
                    var lines = PageLines(window);
                    if (c.Check(shown && lines > 0, $"{when}: the Encyclopedia did not show the page of '{name}' (page shown: '{title}', {lines} lines)"))
                    {
                        pages++;
                    }
                    c.Check(skills.m_skillData.ContainsKey(type) == present,
                        present
                            ? $"reading the Encyclopedia page of '{name}' removed a Sailing 0 entry from the character"
                            : $"reading the Encyclopedia page of '{name}' added a Sailing entry to a character without the skill");
                }

                // Browsing on without the search: down the list of the tab, entry by entry.
                search.text = "";
                yield return new WaitForSecondsRealtime(0.35f);
                yield return null;
                var browsed = 0;
                for (var i = 0; i < 8; i++)
                {
                    var moved = OtherCall(window, "MoveSelection", out found, 1) is bool m && m;
                    if (!found || !moved)
                    {
                        break;
                    }
                    browsed++;
                    yield return null;
                }
                c.Note($"{when}: {pages} of 3 item pages read by name ({names}), {browsed} more by stepping down the list");
                var entry = skills.m_skillData.TryGetValue(type, out var sailing) ? sailing : null;
                c.Check((entry != null) == present && (entry == null || (entry.m_level == 0f && entry.m_accumulator == 0f)),
                    present
                        ? $"after browsing the Encyclopedia the Sailing 0 entry is {(entry != null ? "level " + F(entry.m_level) + ", progress " + F(entry.m_accumulator) : "gone")}"
                        : "after browsing the Encyclopedia the character has a Sailing entry it did not have");

                // Its window closed, the Skills panel opened: the row is as before.
                if (searchWas.Length > 0)
                {
                    search.text = searchWas;
                    yield return new WaitForSecondsRealtime(0.35f);
                }
                OtherCall(window, "Close", out found, false);
                yield return null;
                yield return null;
                c.Check(found && OtherBool(window, "IsOpen") == false, $"{when}: the Encyclopedia window did not close");
                var rows = OpenPanel();
                var row = FindRow(rows, SailingSkill.DisplayName);
                if (present)
                {
                    c.Check(rows != null && row != null && row.Level == "0",
                        $"{when}, after browsing the Encyclopedia: the Skills panel shows Sailing '{(row != null ? row.Level : "no row (or two)")}', expected the row at level 0");
                }
                else
                {
                    c.Check(rows != null && rows.All(r => r.Name != SailingSkill.DisplayName),
                        $"{when}, after browsing the Encyclopedia: the Skills panel has a Sailing row");
                }
                yield return null;
                yield return null;
                ClosePanel();
                yield return null;
                yield return null;
            }
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            try
            {
                if (search != null && searchWas != null && OtherBool(window, "IsOpen") == true && search.text != searchWas)
                {
                    search.text = searchWas;
                }
                CloseEncyclopedia(window, gui);
            }
            catch (Exception e)
            {
                Log.Error($"Sailing self test clean-up failed: {e}");
            }
            player.m_knownMaterial.Clear();
            player.m_knownMaterial.UnionWith(known);
            backup.SetPos(0);
            skills.Load(backup);
            HelmSkill.Invalidate();
        }
    }

    // ---------- sailing.sneak (X03) ----------

    private static IEnumerator RunSneak()
    {
        var c = new Checks(SneakName);
        if (!OtherActive(SneakAmbushGuid, out var sneakState))
        {
            // No pass without a check: a skipped test must not read as proof.
            SelfTest.Fail(SneakName, $"Sneak Ambush is not loaded and active ({sneakState}): nothing of TESTING.md X03 was checked");
            yield break;
        }
        // Its holding-still state: bonus on (decided at its half-second look) and seconds held still so far.
        var stealth = OtherType(SneakAmbushGuid, "StealthState");
        var activeField = stealth != null ? stealth.GetField("StillActive", AnyStatic) : null;
        var timeField = stealth != null ? stealth.GetField("StillTime", AnyStatic) : null;
        if (activeField == null || timeField == null || activeField.FieldType != typeof(bool) || timeField.FieldType != typeof(float))
        {
            SelfTest.Fail(SneakName, "Sneak Ambush's holding-still state (StealthState.StillActive, StillTime) was not found: nothing of TESTING.md X03 was checked");
            yield break;
        }
        bool Still() => (bool)activeField.GetValue(null);
        float StillFor() => (float)timeField.GetValue(null);
        // Its status icon "Holding still": status effect named "<its guid>.HoldingStill" (only with its ShowStealthCues on).
        var cue = (SneakAmbushGuid + ".HoldingStill").GetStableHashCode();

        var rig = ShipRig.Create(SneakName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        var sea = new Sea { Rig = rig };
        var started = Time.realtimeSinceStartup;
        var crouched = false;
        Rigidbody held = null;
        var constraints = RigidbodyConstraints.None;
        try
        {
            ServerRules.TestRules = new SailingRules();
            yield return SetUpSea(sea, c, 0f, 0.3f, 90f);
            if (!sea.Ok)
            {
                yield return LeaveSea(sea, c);
                c.Report();
                yield break;
            }
            var player = rig.Player;
            var ship = rig.Boat;
            var controls = ship.m_shipControlls;
            var skills = player.GetSkills();
            var seman = player.GetSEMan();
            HelmSkill.TestLocalLevel = null;
            rig.BackupSkills();
            var sailing = SetSailing(player, 50f, 0f);
            var sneak = skills.GetSkill(Skills.SkillType.Sneak);

            // Passenger: helm let go the vanilla way.
            player.StopDoodadControl();
            var standing = new Box();
            yield return WaitFor(() => player.GetStandingOnShip() == ship, 3f, standing);
            if (!c.Check(standing.Ok && controls.GetUser() == 0L && player.GetControlledShip() == null,
                    "the player who let go of the helm does not stand on the deck as a passenger: nothing of the holding-still bonus was checked"))
            {
                yield return LeaveSea(sea, c);
                c.Report();
                yield break;
            }

            // Ship lying still: hull held in place (waves would rock a floating ship more than Sneak Ambush's limit
            // for "not moved"), sail already up so it pulls at once when the hull is let go.
            ResetBoat(ship, ship.transform.position, ship.transform.forward);
            held = ship.m_body;
            constraints = held.constraints;
            held.constraints = RigidbodyConstraints.FreezeAll;
            ship.m_speed = Ship.Speed.Full;
            SailingXp.Reset();
            var sneakLevel = sneak.m_level;
            var sneakXp = sneak.m_accumulator;
            var sailLevel = sailing.m_level;
            var sailXp = sailing.m_accumulator;
            player.SetCrouch(true);
            crouched = true;
            var on = new Box();
            yield return WaitFor(() => player.IsCrouching() && Still(), 9f, on);
            var cueOnDeck = seman != null && seman.HaveStatusEffect(cue);
            if (!c.Check(on.Ok,
                    "crouched and still on the deck of a ship that lies still, Sneak Ambush's holding-still bonus did not come on within 9 s "
                    + $"(crouching {player.IsCrouching()}, on the deck {player.GetStandingOnShip() == ship}, held still for {F(StillFor())} s; "
                    + "its StillBonus setting may be 0): the ship getting under way would prove nothing"))
            {
                held.constraints = constraints;
                held = null;
                player.SetCrouch(false);
                crouched = false;
                yield return LeaveSea(sea, c);
                c.Report();
                yield break;
            }

            // Hull let go: the ship gets under way with the passenger still crouched. The bonus must be gone at the
            // latest two of Sneak Ambush's half-second looks after the ship does 0.8 m/s (its limit: 0.25 m per look).
            held.constraints = constraints;
            held = null;
            var t0 = Time.time;
            var movingAt = -1f;
            var endedAt = -1f;
            var sneaking = 0;
            while (Time.time - t0 < 18f && ship != null)
            {
                yield return new WaitForFixedUpdate();
                if (ship == null)
                {
                    break;
                }
                var speed = Flat(ship.m_body.linearVelocity).magnitude;
                if (movingAt < 0f && speed > 0.8f)
                {
                    movingAt = Time.time;
                }
                if (endedAt < 0f && !Still())
                {
                    endedAt = Time.time;
                }
                if (player.IsSneaking())
                {
                    sneaking++;
                }
                if (movingAt >= 0f && Time.time - movingAt > 1.2f)
                {
                    break;
                }
            }
            if (c.Check(ship != null && movingAt >= 0f && player.m_crouchToggled && player.GetStandingOnShip() == ship,
                    $"check not valid: the ship did not get under way with the passenger crouched on its deck (0.8 m/s reached: {movingAt >= 0f}, "
                    + $"crouching {player.m_crouchToggled})"))
            {
                c.Check(endedAt >= 0f && !Still(),
                    $"the ship is under way with the passenger crouched and still on its deck, but Sneak Ambush's holding-still bonus is still on "
                    + $"({F(Time.time - movingAt)} s after the ship reached 0.8 m/s)");

                // Under way for 2.5 s more: the bonus never comes back.
                var from = ship.transform.position;
                var back = 0;
                var steps = 0;
                var longest = 0f;
                var until = Time.time + 2.5f;
                while (Time.time < until && ship != null)
                {
                    yield return new WaitForFixedUpdate();
                    steps++;
                    if (Still())
                    {
                        back++;
                    }
                    if (player.IsSneaking())
                    {
                        sneaking++;
                    }
                    longest = Mathf.Max(longest, StillFor());
                }
                var moved = ship != null ? Flat(ship.transform.position - from).magnitude : 0f;
                c.Check(ship != null && moved > 1.5f && player.m_crouchToggled && player.IsCrouching() && ship.IsPlayerInBoat(player),
                    $"check not valid: the ship moved {F(moved)} m in 2.5 s, passenger crouching {player.m_crouchToggled}, still aboard {ship != null && ship.IsPlayerInBoat(player)}");
                c.Check(back == 0,
                    $"crouched and still on the deck of the moving ship, the holding-still bonus was on in {back} of {steps} physics steps "
                    + $"(longest 'held still' time {F(longest)} s)");
                if (cueOnDeck)
                {
                    c.Check(seman != null && !seman.HaveStatusEffect(cue), "the Holding still icon is still shown on the moving ship");
                }
                else
                {
                    c.Note("Sneak Ambush showed no Holding still icon while the ship lay still (its ShowStealthCues setting is off): only its state was read");
                }
            }

            // Neither skill earned anything: Sneak Ambush pays only for sneak attacks, Sailing only the helmsman.
            c.Check(sneak.m_level == sneakLevel && sneak.m_accumulator == sneakXp,
                $"Sneak changed while crouching on the deck: level {F(sneakLevel)} -> {F(sneak.m_level)}, XP {F(sneakXp)} -> {F(sneak.m_accumulator)} "
                + $"(the game counted {sneaking} physics steps as sneaking, creature in stealth range {BaseAI.InStealthRange(player)})");
            c.Check(sailing.m_level == sailLevel && sailing.m_accumulator == sailXp,
                $"a passenger crouching on the deck earned Sailing: level {F(sailLevel)} -> {F(sailing.m_level)}, XP {F(sailXp)} -> {F(sailing.m_accumulator)}");
            var theirs = Problems(watch, "Sneak Ambush");
            c.Check(theirs.Count == 0, "Sneak Ambush logged warnings or errors while the player crouched on the ship: " + Join(theirs));

            player.SetCrouch(false);
            crouched = false;
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
                if (held != null)
                {
                    held.constraints = constraints;
                }
                if (crouched)
                {
                    rig.Player.SetCrouch(false);
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
