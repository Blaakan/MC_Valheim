#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.MusicInstrumentsMod;

// Debug build only. In-world self tests, second part: one test per item of TESTING.md where the game state can show
// it (single player list; the first twelve tests are in SelfTests.cs). Each test says in its first comment which
// items it checks. Checks that can fail for a reason outside the mod (weather, another mod, the game window not in
// front) live in their own small test, so one of them never blocks the items of another.
//   music.recipes        T01  recipes found by material and workbench level, "new recipe", icons, tooltip
//   music.drop           T02  dropped through the game, lies on the ground (round flute may roll slow), picked up again
//   music.hands          T03  both hands, torch / weapon put it away, R hangs it at the hip
//   music.songwindow     T04  click opens the window: cursor, list, hint, nothing moves, three ways to close
//   music.play           T05  Play from the window: status line, music fade, walk, no run / jump, clicks stop, Repeat
//   music.nocomfort      T11  a song playing by itself: no meter, no Encore, no Music
//   music.instruments    T06  Mead Hall Reel on lyre (chords) and tambourine (three kinds of hit), pose and pulses
//   music.midi           T07  MIDI file in the window: parts, length, part chooser, broken file
//   music.rhythm         T08  countdown, lanes, Perfect / Good / Miss, nothing else works, loop, right click, Esc
//   music.encore         T09  meter states, banner, Music icon and time, comfort, Rested +3 min, renewal
//   music.encore.easy    T29  every other note at the real 20 s, wrong key = miss, one in four = red
//   music.rest           T09 T10  by a real fire, seated: Resting comfort, Rested 8:00 / 11:00, messages, shelter
//   music.seated         T12  seated: clicks, stops and closes never stand you up or leave you blocking
//   music.stops          T13  hit, fire, R, weapon, swim; map / inventory / chat held back, Esc
//   music.click          T14  never punches, Block with fists; mod really off: click does nothing
// (the list goes on in SelfTests.Solo2.cs)
internal static partial class SelfTests
{
    private const string RecipesName = "music.recipes";
    private const string DropName = "music.drop";
    private const string HandsName = "music.hands";
    private const string SongWindowName = "music.songwindow";
    private const string PlayName = "music.play";
    private const string NoComfortName = "music.nocomfort";
    private const string InstrumentsName = "music.instruments";
    private const string MidiName = "music.midi";
    private const string RhythmName = "music.rhythm";
    private const string EncoreName = "music.encore";
    private const string EncoreEasyName = "music.encore.easy";
    private const string RestName = "music.rest";
    private const string SeatedName = "music.seated";
    private const string StopsName = "music.stops";
    private const string ClickName = "music.click";

    private static readonly KeyValuePair<string, Func<IEnumerator>>[] SoloTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(RecipesName, RunRecipes),
        new KeyValuePair<string, Func<IEnumerator>>(DropName, RunDrop),
        new KeyValuePair<string, Func<IEnumerator>>(HandsName, RunHands),
        new KeyValuePair<string, Func<IEnumerator>>(SongWindowName, RunSongWindow),
        new KeyValuePair<string, Func<IEnumerator>>(PlayName, RunPlay),
        new KeyValuePair<string, Func<IEnumerator>>(NoComfortName, RunNoComfort),
        new KeyValuePair<string, Func<IEnumerator>>(InstrumentsName, RunInstruments),
        new KeyValuePair<string, Func<IEnumerator>>(MidiName, RunMidi),
        new KeyValuePair<string, Func<IEnumerator>>(RhythmName, RunRhythm),
        new KeyValuePair<string, Func<IEnumerator>>(EncoreName, RunEncore),
        new KeyValuePair<string, Func<IEnumerator>>(EncoreEasyName, RunEncoreEasy),
        new KeyValuePair<string, Func<IEnumerator>>(RestName, RunRest),
        new KeyValuePair<string, Func<IEnumerator>>(SeatedName, RunSeated),
        new KeyValuePair<string, Func<IEnumerator>>(StopsName, RunStops),
        new KeyValuePair<string, Func<IEnumerator>>(ClickName, RunClick),
    };

    private static void RegisterSolo()
    {
        foreach (var t in SoloTests)
        {
            SelfTest.Register(t.Key, t.Value);
        }
        RegisterSolo2();
    }

    private static void UnregisterSolo()
    {
        foreach (var t in SoloTests)
        {
            SelfTest.Unregister(t.Key);
        }
        UnregisterSolo2();
    }

    // ---------- helpers ----------

    private static ItemDrop.ItemData.SharedData SharedOf(InstrumentKind kind)
    {
        var prefab = InstrumentContent.Prefab(kind);
        return prefab != null ? prefab.GetComponent<ItemDrop>().m_itemData.m_shared : null;
    }

    private static string SharedName(string prefab)
    {
        var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
        var drop = go != null ? go.GetComponent<ItemDrop>() : null;
        return drop != null ? drop.m_itemData.m_shared.m_name : null;
    }

    // Left click with the instrument in hand, the way the game hands it to the player (Player.SetControls).
    private static IEnumerator Click(Rig rig)
    {
        rig.Attack();
        yield return Frames(3);
        rig.TakeControls();
    }

    // Block (right mouse button) held for a moment, same way.
    private static IEnumerator BlockClick(Rig rig, Player player)
    {
        player.SetControls(Vector3.zero, false, false, false, false, true, true, false, false, false, false);
        yield return Frames(3);
        rig.TakeControls();
    }

    private static IEnumerator OpenWindow(Rig rig)
    {
        // A menu or a window closed a moment ago still counts as open for a few frames.
        yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
        yield return Click(rig);
        yield return Frames(2);
    }

    // Feet pushed forward for a while through Player.SetControls. moved[0] = metres gone (flat), moved[1] = 1 when
    // the player never jumped (never shot upward).
    private static IEnumerator Push(Rig rig, Player player, float seconds, float[] moved, bool run = false, bool jump = false)
    {
        var from = player.transform.position;
        var jumped = false;
        var end = Time.time + seconds;
        var first = true;
        while (Time.time < end)
        {
            player.SetControls(new Vector3(0f, 0f, 1f), false, false, false, false, false, false, jump && first, false, run, false);
            first = false;
            yield return null;
            jumped |= player.m_body != null && player.m_body.linearVelocity.y > 3f;
        }
        rig.TakeControls();
        moved[0] = Flat(player.transform.position, from);
        moved[1] = jumped ? 0f : 1f;
    }

    // An inventory item on a hotbar key: its key ("Hotbar3"), moved to a free place of the top row when it is not
    // there. Null = the top row is full.
    private static string ToHotbar(Player player, ItemDrop.ItemData item)
    {
        var button = HotbarButton(item);
        if (button != null || item == null)
        {
            return button;
        }
        var inv = player.GetInventory();
        for (var slot = 0; slot < 8; slot++)
        {
            if (inv.GetItemAt(slot, 0) == null)
            {
                item.m_gridPos = new Vector2i(slot, 0);
                inv.Changed();
                return HotbarButton(item);
            }
        }
        return null;
    }

    // Hotbar key of an inventory item ("Hotbar3"), null when it is not in the top row.
    private static string HotbarButton(ItemDrop.ItemData item) =>
        item != null && item.m_gridPos.y == 0 && item.m_gridPos.x >= 0 && item.m_gridPos.x < 8 ? "Hotbar" + (item.m_gridPos.x + 1) : null;

    // "m:ss" at the end of a text ("... 0:07 / 0:42" gives the first clock when first is true).
    private static int ClockIn(string text, bool first)
    {
        if (string.IsNullOrEmpty(text))
        {
            return -1;
        }
        var slash = text.IndexOf(" / ", StringComparison.Ordinal);
        if (slash < 0)
        {
            return -1;
        }
        string part;
        if (first)
        {
            var start = text.LastIndexOf(' ', slash - 1);
            part = text.Substring(start + 1, slash - start - 1);
        }
        else
        {
            var end = text.IndexOf(' ', slash + 3);
            part = end < 0 ? text.Substring(slash + 3) : text.Substring(slash + 3, end - slash - 3);
        }
        var colon = part.IndexOf(':');
        if (colon <= 0 || !int.TryParse(part.Substring(0, colon), out var m) || !int.TryParse(part.Substring(colon + 1), out var s))
        {
            return -1;
        }
        return m * 60 + s;
    }

    // Is the next press of this chart note due? offset: seconds after the note's time the press should land (the press
    // is read on the next frame, so me look one frame ahead).
    private static bool Due(MiniGame game, float noteTime, float offset) =>
        game.Clock + Mathf.Min(0.05f, Time.unscaledDeltaTime) >= noteTime + offset;

    private static long NoteKey(KeyValuePair<int, int> v) => (long)v.Key * 100000 + v.Value;

    // ---------- music.recipes (T01) ----------

    // The game's own discovery: a material in the inventory (Player.OnInventoryChanged) and the known workbench level
    // decide when "new recipe" comes. Me give the materials one by one; what the character knew is put back after.
    private static IEnumerator RunRecipes()
    {
        var c = new Checks(RecipesName);
        var player = Player.m_localPlayer;
        var db = ObjectDB.instance;
        if (player == null || db == null || MessageHud.instance == null)
        {
            SelfTest.Fail(RecipesName, "no player, item database or message HUD");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            yield return new WaitForSeconds(InstrumentContent.RebuildDelaySeconds + 0.3f);
            var fluteName = SharedOf(InstrumentKind.Flute).m_name;
            var lyreName = SharedOf(InstrumentKind.Lyre).m_name;
            var tambourineName = SharedOf(InstrumentKind.Tambourine).m_name;
            var station = InstrumentContent.RecipeOf(InstrumentKind.Flute).m_craftingStation;
            if (!c.Check(station != null && station.name == MusicRules.Workbench, "flute recipe made at the workbench"))
            {
                c.Report();
                yield break;
            }
            c.Check(InstrumentContent.RecipeOf(InstrumentKind.Flute).m_minStationLevel == 1
                    && InstrumentContent.RecipeOf(InstrumentKind.Tambourine).m_minStationLevel == 2
                    && InstrumentContent.RecipeOf(InstrumentKind.Lyre).m_minStationLevel == 2, "workbench levels 1 / 2 / 2");
            var wanted = new Dictionary<InstrumentKind, string>
            {
                { InstrumentKind.Flute, "FineWood:4" },
                { InstrumentKind.Lyre, "LinenThread:8,Silver:2" },
                { InstrumentKind.Tambourine, "FineWood:3,LeatherScraps:4" },
            };
            foreach (var kind in InstrumentContent.Kinds)
            {
                var parts = new List<string>();
                foreach (var req in InstrumentContent.RecipeOf(kind).m_resources)
                {
                    parts.Add(req.m_resItem.name + ":" + req.m_amount);
                }
                parts.Sort(StringComparer.Ordinal);
                var got = string.Join(",", parts.ToArray());
                c.Check(got == wanted[kind], $"{kind} costs {got}");
            }

            // A character that never had the materials and knows the workbench at level 1.
            x.SaveKnown();
            foreach (var name in new[] { fluteName, lyreName, tambourineName })
            {
                player.m_knownRecipes.Remove(name);
            }
            var materials = new[] { "FineWood", "LeatherScraps", "Silver", "LinenThread" };
            var known = true;
            foreach (var m in materials)
            {
                var shared = SharedName(m);
                known &= shared != null;
                if (shared != null)
                {
                    player.m_knownMaterial.Remove(shared);
                }
            }
            c.Check(known, "the four materials are items of this game");
            player.m_knownStations[station.m_name] = 1;
            Seen.Clear();

            c.Check(rig.Give("FineWood") != null, "Fine Wood picked up");
            c.Check(player.IsRecipeKnown(fluteName) && Seen.Unlocked("$msg_newrecipe", fluteName),
                "Fine Wood: \"New recipe\" Wooden Flute");
            c.Check(!player.IsRecipeKnown(tambourineName) && !player.IsRecipeKnown(lyreName), "nothing else yet");

            c.Check(rig.Give("LeatherScraps") != null, "Leather Scraps picked up");
            c.Check(!player.IsRecipeKnown(tambourineName), "Leather Scraps at workbench level 1: no Tambourine yet");
            // What Player.AddKnownStation does when a level 2 workbench is used.
            player.m_knownStations[station.m_name] = 2;
            player.UpdateKnownRecipesList();
            c.Check(player.IsRecipeKnown(tambourineName) && Seen.Unlocked("$msg_newrecipe", tambourineName),
                "workbench level 2: \"New recipe\" Tambourine");
            c.Check(!player.IsRecipeKnown(lyreName), "no Silver Lyre without silver and linen thread");

            c.Check(rig.Give("Silver") != null, "Silver picked up");
            c.Check(!player.IsRecipeKnown(lyreName), "Silver alone: no Silver Lyre yet");
            c.Check(rig.Give("LinenThread") != null, "Linen Thread picked up");
            c.Check(player.IsRecipeKnown(lyreName) && Seen.Unlocked("$msg_newrecipe", lyreName),
                "Silver and Linen Thread: \"New recipe\" Silver Lyre");

            // Icons: each its own, none the flint knife's.
            var knifePrefab = db.GetItemPrefab(InstrumentContent.VanillaKnifeName);
            var knifeIcon = knifePrefab != null ? knifePrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons[0] : null;
            var icons = new List<Sprite>();
            foreach (var kind in InstrumentContent.Kinds)
            {
                var shared = SharedOf(kind);
                var icon = shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null;
                c.Check(icon != null && icon != knifeIcon && icon == InstrumentIcons.Get(kind), kind + " has its own icon");
                foreach (var other in icons)
                {
                    c.Check(icon != other && (icon == null || other == null || icon.texture != other.texture || icon.rect != other.rect),
                        kind + " icon differs from the others");
                }
                icons.Add(icon);
            }

            // Tooltip of each: two-handed, no damage, orange line with the Attack and Block keys.
            var attackKey = Localization.instance.Localize("$KEY_Attack");
            var blockKey = Localization.instance.Localize("$KEY_Block");
            c.Note($"Attack key shown as '{attackKey}', Block key as '{blockKey}'");
            c.Check(attackKey.Length > 0 && blockKey.Length > 0 && !attackKey.StartsWith("[", StringComparison.Ordinal)
                    && attackKey.IndexOf("MISSING", StringComparison.Ordinal) < 0 && blockKey.IndexOf("MISSING", StringComparison.Ordinal) < 0,
                "the game names the Attack and Block keys");
            foreach (var kind in InstrumentContent.Kinds)
            {
                var item = rig.Give(InstrumentContent.ItemName(kind));
                if (!c.Check(item != null, kind + " given"))
                {
                    continue;
                }
                var tip = item.GetTooltip();
                c.Check(tip.Contains("$item_twohanded"), kind + " tooltip says two-handed");
                c.Check(!tip.Contains("$inventory_") && !tip.Contains("$item_knockback") && !tip.Contains("$item_backstab")
                        && !tip.Contains("$item_blockarmor") && !tip.Contains("$item_staminause") && !tip.Contains("$item_durability"),
                    kind + " tooltip shows no damage, block or durability line");
                c.Check(tip.Contains("<color=orange>" + InstrumentContent.Controls + "</color>"), kind + " tooltip has the orange line");
                var shown = Localization.instance.Localize(tip);
                c.Check(shown.Contains("[<color=yellow><b>" + attackKey + "</b></color>] Songs")
                        && shown.Contains("[<color=yellow><b>" + blockKey + "</b></color>] Stop") && !shown.Contains("$KEY_"),
                    kind + " orange line shows the Attack and Block keys");
            }
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
            try
            {
                x.Known();
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (known recipes) failed: {e.Message}");
            }
        }
    }

    // ---------- music.drop (T02) ----------

    private static int CountKind(Player player, InstrumentKind kind)
    {
        var n = 0;
        foreach (var i in player.GetInventory().GetAllItems())
        {
            if (InstrumentContent.KindOf(i) == kind)
            {
                n++;
            }
        }
        return n;
    }

    // Height of what lies under a spot near the item (terrain, rock, floor): short ray from just above, so tree or
    // roof over it never count. Nothing hit = the spot's own height (no slope known there).
    private static float SolidBelow(Vector3 p)
    {
        return Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var hit, 2f, ZoneSystem.instance.m_solidRayMask)
            ? hit.point.y
            : p.y;
    }

    private static IEnumerator RunDrop()
    {
        var c = new Checks(DropName);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(DropName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            foreach (var kind in InstrumentContent.Kinds)
            {
                var item = rig.Give(InstrumentContent.ItemName(kind));
                if (!c.Check(item != null, kind + " given"))
                {
                    continue;
                }
                c.Check(player.DropItem(player.GetInventory(), item, 1), kind + " dropped through the game");
                yield return Frames(2);
                ItemDrop drop = null;
                foreach (var d in ItemDrop.s_instances)
                {
                    if (d != null && InstrumentContent.KindOf(d.m_itemData) == kind
                        && (d.transform.position - player.transform.position).magnitude < 12f)
                    {
                        drop = d;
                    }
                }
                if (!c.Check(drop != null && !player.GetInventory().ContainsItem(item), kind + " is an object in the world, gone from the inventory"))
                {
                    continue;
                }
                rig.Spawned(drop.gameObject);
                var body = drop.GetComponent<Rigidbody>();
                // Flute is round (capsule collider): on ground of random world it roll slow long after landing
                // (run: 0.105 m/s still, 6 s after drop, ground 6 mm lower over 0.56 m). So me wait longer for rest,
                // and slow roll down the slope still count as lying on the ground. Roll on level ground, roll up,
                // fast or falling after the wait = fail.
                var thrown = Time.time;
                yield return WaitFor(() => drop == null || body == null || body.IsSleeping() || body.linearVelocity.sqrMagnitude < 0.0004f, 16f);
                var waited = Time.time - thrown;
                yield return new WaitForSeconds(0.3f);
                if (!c.Check(drop != null, kind + " still there after landing"))
                {
                    continue;
                }
                var pos = drop.transform.position;
                var ground = ZoneSystem.instance.GetGroundHeight(pos);
                var velocity = body != null && !body.IsSleeping() ? body.linearVelocity : Vector3.zero;
                var speed = velocity.magnitude;
                var still = speed < 0.05f;
                var along = new Vector3(velocity.x, 0f, velocity.z);
                var flatSpeed = along.magnitude;
                // How much lower the ground is half a metre ahead than half a metre behind, the way it moves.
                var fall = 0f;
                if (!still && flatSpeed > 0.001f)
                {
                    along /= flatSpeed;
                    fall = SolidBelow(pos - along * 0.5f) - SolidBelow(pos + along * 0.5f);
                }
                var rollsDown = !still && flatSpeed < 0.3f && Mathf.Abs(velocity.y) < 0.1f && fall > 0.001f;
                c.Note($"{kind} lies at {F(pos)}, ground {F(ground)}, speed {F(speed)} m/s after {F(waited)} s"
                       + (still ? "" : $" (still moving: {F(flatSpeed)} m/s along the ground, {F(velocity.y)} m/s up, ground falls {F(fall)} m per metre that way)"));
                c.Check(still || rollsDown, kind + " lies on the ground: at rest, or only a slow roll down a slope");
                c.Check(pos.y > ground - 0.3f && pos.y < player.transform.position.y + 2.5f, kind + " did not fall through the ground");
                var collider = drop.GetComponentInChildren<Collider>();
                var renderer = drop.GetComponentInChildren<MeshRenderer>();
                c.Check(collider != null && collider.enabled && renderer != null && renderer.enabled && renderer.bounds.size.magnitude > 0.05f,
                    kind + " on the ground has its model and a collider");
                // Picked up again like the Use key does. Me count before and after: one more of this kind, and it is
                // the item the game knows under that name (same drop prefab, same name). Never compare m_shared by
                // reference: the game make each item from a copy of the prefab, shared data copied with it.
                var before = CountKind(player, kind);
                var picked = player.Pickup(drop.gameObject, false, false);
                yield return Frames(2);
                ItemDrop.ItemData back = null;
                foreach (var i in player.GetInventory().GetAllItems())
                {
                    if (InstrumentContent.KindOf(i) == kind)
                    {
                        back = i;
                    }
                }
                x.Track(back);
                var prefab = InstrumentContent.Prefab(kind);
                c.Check(picked, kind + " picked up (the game took it)");
                c.Check(back != null && CountKind(player, kind) == before + 1 && back.m_stack == 1 && prefab != null && back.m_dropPrefab == prefab
                        && back.m_shared != null && back.m_shared.m_name == SharedOf(kind).m_name
                        && ObjectDB.instance.GetItemPrefab(InstrumentContent.ItemName(kind)) == prefab,
                    $"{kind} picked up: the same instrument in the inventory ({CountKind(player, kind) - before} more, name '{(back != null && back.m_shared != null ? back.m_shared.m_name : "none")}')");
                c.Check(drop == null, kind + " gone from the ground");
            }
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.hands (T03, vanilla part of X05) ----------

    private static IEnumerator RunHands()
    {
        var c = new Checks(HandsName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(HandsName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            var torch = rig.Give("Torch");
            var knife = rig.Give(InstrumentContent.VanillaKnifeName);
            if (!c.Check(torch != null && knife != null, "torch and flint knife given"))
            {
                c.Report();
                yield break;
            }
            player.EquipItem(torch, false);
            yield return Frames(2);
            var flute = rig.Hold(InstrumentKind.Flute);
            yield return Frames(2);
            c.Check(player.GetRightItem() == flute && player.GetLeftItem() == null && !player.IsItemEquiped(torch),
                "torch then instrument: both hands taken, the torch is put away");
            player.EquipItem(torch, false);
            yield return Frames(2);
            c.Check(!player.IsItemEquiped(flute) && player.IsItemEquiped(torch) && Performance.Held == InstrumentKind.None,
                "a torch puts the instrument away");
            player.EquipItem(flute, false);
            yield return Frames(2);
            player.EquipItem(knife, false);
            yield return Frames(2);
            c.Check(!player.IsItemEquiped(flute) && player.GetRightItem() == knife, "a weapon puts the instrument away");
            // Two hands full (knife and torch), then the instrument: both go.
            player.EquipItem(torch, false);
            yield return Frames(2);
            c.Check(player.GetRightItem() == knife && player.GetLeftItem() == torch, "knife and torch in two hands");
            player.EquipItem(flute, false);
            yield return Frames(2);
            c.Check(player.GetRightItem() == flute && player.GetLeftItem() == null && !player.IsItemEquiped(knife)
                    && !player.IsItemEquiped(torch), "the instrument puts away what both hands held");

            // R (the game's Hide button): at the hip like the hammer, on the tool's back spot.
            c.Note($"player takes input: {player.TakeInput()}");
            yield return Tap("Hide");
            yield return Frames(3);
            var vis = player.m_visEquipment;
            c.Check(player.m_hiddenRightItem == flute && player.GetRightItem() == null, "R puts the instrument away, kept as the hidden right item");
            yield return WaitFor(() => vis.m_rightBackItemInstance != null, 2f);
            c.Check(vis.m_currentRightBackItemHash == InstrumentContent.ItemHash(InstrumentKind.Flute), "the game shows it as the item on the back");
            c.Check(vis.m_rightBackItemInstance != null && vis.m_backTool != null
                    && vis.m_rightBackItemInstance.transform.IsChildOf(vis.m_backTool), "it hangs at the tool spot (where the hammer hangs)");
            var hammer = ObjectDB.instance.GetItemPrefab("Hammer");
            c.Check(hammer != null && hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_itemType == SharedOf(InstrumentKind.Flute).m_itemType
                    && SharedOf(InstrumentKind.Flute).m_attachOverride == ItemDrop.ItemData.ItemType.None, "same item type as the hammer");
            yield return Tap("Hide");
            yield return Frames(3);
            c.Check(player.GetRightItem() == flute && player.m_hiddenRightItem == null, "R again takes it back in hand");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.songwindow (T04) ----------

    private static IEnumerator RunSongWindow()
    {
        var c = new Checks(SongWindowName);
        var player = Player.m_localPlayer;
        if (player == null || Menu.instance == null)
        {
            SelfTest.Fail(SongWindowName, "no player or menu");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.NoShare();
            var folder = x.SongsFolder("MC_MusicWindowTest");
            Performance.LastSongId = null;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            var cursorBefore = ZCursor.IsRequested;
            c.Note($"cursor asked for before the click: {cursorBefore}");
            yield return OpenWindow(rig);
            c.Check(Performance.WindowOpen && SongWindow.IsOpen, "left click opens the Songs window");
            c.Check(SongWindow.TitleText == "Wooden Flute songs", "title: " + SongWindow.TitleText);
            c.Check(!player.InAttack(), "the click did not punch");
            yield return Frames(2);
            c.Check(ZCursor.IsRequested && ZCursor.IsVisible == ZInput.IsMouseActive(),
                $"the window has the cursor (asked {ZCursor.IsRequested}, visible {ZCursor.IsVisible}, mouse in use {ZInput.IsMouseActive()})");

            // The list: Free play, the eight built-in songs, "Your MIDI songs", the folder hint.
            var items = SongWindow.TestItems();
            c.Note("list: " + string.Join(" / ", items.ToArray()));
            c.Check(SongLibrary.Presets.Count == 8, "eight built-in songs");
            var ok = items.Count == 11 && items[0] == "song|" + SongLibrary.FreePlayId + "|Free play";
            for (var i = 0; ok && i < SongLibrary.Presets.Count; i++)
            {
                ok = items[1 + i] == "song|" + SongLibrary.Presets[i].Id + "|" + SongLibrary.Presets[i].Title;
            }
            c.Check(ok, "Free play, then the eight built-in songs in order");
            c.Check(items.Count == 11 && items[9] == "header|Your MIDI songs" && items[10] == "hint|Put .mid files in " + folder,
                "then \"Your MIDI songs\" and the hint naming the folder");

            // Nothing moves: the game's own gate (keys and mouse look) is shut, and feet pushed anyway stay.
            var controller = x.ControllerObject;
            c.Check(KeyCapture.Active && Chat.instance != null && Chat.instance.HasFocus(), "game keys held back");
            c.Check(controller != null && !controller.TakeInput(false) && !controller.TakeInput(true),
                "the player controller takes no movement and no mouse look");
            var moved = new float[2];
            yield return Push(rig, player, 0.5f, moved);
            c.Check(moved[0] < 0.05f && SongWindow.IsOpen, $"feet pushed forward: the character stays ({F(moved[0])} m)");
            x.Controller(true);
            Press("Forward");
            var at = player.transform.position;
            var yaw = player.m_lookYaw;
            yield return new WaitForSeconds(0.5f);
            Let("Forward");
            x.Controller(false);
            rig.TakeControls();
            c.Check(Flat(player.transform.position, at) < 0.05f && player.m_lookYaw == yaw && SongWindow.IsOpen,
                "Forward key held with the controller on: no step, no turn");

            // Close button.
            c.Check(SongWindow.TestPress("Close"), "Close pressed");
            yield return Frames(3);
            c.Check(!SongWindow.IsOpen && !Performance.WindowOpen && !KeyCapture.Active, "Close closes it, keys given back");
            c.Check(ZCursor.IsRequested == cursorBefore, "cursor given back to the game");
            c.Check(controller != null && controller.TakeInput(false), "the player controller takes input again");

            // A file in the folder: listed instead of the hint.
            File.WriteAllBytes(Path.Combine(folder, "selftest tune.mid"), MidiOf(Preset("greensleeves")));
            yield return OpenWindow(rig);
            items = SongWindow.TestItems();
            c.Check(SongWindow.IsOpen && items.Count == 11 && items[9] == "header|Your MIDI songs"
                    && items[10] == "song|midi:selftest tune.mid|selftest tune", "a MIDI file of the folder is listed under \"Your MIDI songs\"");

            // Esc: the menu button of the game (Menu.Update reads Escape and this button in the same test): our window
            // closes and the menu stays shut.
            c.Check(!MenuShown, "menu shut before Esc");
            yield return Tap("JoyMenu");
            var menuSeen = false;
            for (var i = 0; i < 8; i++)
            {
                menuSeen |= MenuShown;
                yield return null;
            }
            c.Check(!SongWindow.IsOpen && !Performance.WindowOpen, "Esc closes the window");
            c.Check(!menuSeen && !Game.IsPaused(), "Esc does not open the menu");
            // The same button with no window: the menu opens (the press me fake is one the game obeys).
            yield return WaitReal(() => !Menu.IsVisible(), 2f);
            yield return Tap("JoyMenu");
            yield return WaitReal(() => MenuShown, 1f);
            c.Check(MenuShown, "without the window the same press opens the menu");
            if (MenuShown)
            {
                Menu.instance.Hide();
            }
            yield return Real(0.3f);

            // Right mouse button (raw read: test hook), Block held like the real button: closes, Block forgotten.
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen, "open again");
            Press("Block");
            Performance.TestRightClick = true;
            yield return Frames(3);
            c.Check(!SongWindow.IsOpen && !Performance.WindowOpen, "right mouse button closes the window");
            c.Check(!ZInput.GetButton("Block") && !player.IsBlocking(), "the same click does not raise the block");
            Let("Block");
            yield return Frames(3);
            c.Check(!KeyCapture.Active, "keys given back");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.play (T05, mixer part of T16) ----------

    private static IEnumerator RunPlay()
    {
        var c = new Checks(PlayName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(PlayName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.NoShare();
            var folder = x.SongsFolder("MC_MusicPlayTest");
            Plugin.TestGameMusicVolume = 0.3f;
            Performance.Repeat = false;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen && SongWindow.TestSelect("preset:greensleeves"), "Greensleeves picked");
            c.Check(SongWindow.TestPress("Play"), "Play pressed");
            yield return Frames(3);
            c.Check(!SongWindow.IsOpen && Performance.Mode == PerformanceMode.Auto, "the window closes and the song plays by itself");
            c.Check(PlayingFlag(player) == (int)InstrumentKind.Flute, "playing flag for the other games");
            yield return new WaitForSeconds(1.6f);
            var emitter = Performance.LocalEmitter;
            var rms = new float[1];
            yield return Measure(emitter, 0.8f, rms);
            c.Check(rms[0] > 0.002f, $"the flute sounds (rms {F(rms[0])})");
            c.Check(emitter != null && emitter.Source != null && emitter.Source.outputAudioMixerGroup == AudioKit.SfxGroup
                    && AudioKit.SfxGroup != null && emitter.Source.spatialBlend == 0f,
                "own sound goes through the game's sound effects group (its volume sliders), centred");
            c.Check(Performance.PoseWeight > 0.95f && InstrumentPose.Weight(player) > 0.95f, "arms in the playing pose");
            var line = MiniGameHud.AutoText;
            var shownTime = ClockIn(line, true);
            var shownLength = ClockIn(line, false);
            c.Check(MiniGameHud.AutoShown && line != null && line.StartsWith("Greensleeves   ", StringComparison.Ordinal)
                    && shownTime >= 1 && shownTime <= 4 && shownLength == Mathf.RoundToInt(Performance.SongLength),
                $"status line with title and time: '{line}'");
            c.Check(Listeners.Duck < 0.4f, $"the game's music fades (to {F(Listeners.Duck)} of its volume)");

            // Walk while it plays; Shift does not run, Space does not jump.
            var moved = new float[2];
            yield return Push(rig, player, 0.9f, moved, run: true, jump: true);
            c.Check(moved[0] > 0.5f && Performance.Mode == PerformanceMode.Auto, $"walking keeps it playing ({F(moved[0])} m)");
            c.Check(moved[1] > 0.5f, "Space does not jump");
            player.SetControls(new Vector3(0f, 0f, 1f), false, false, false, false, false, false, false, false, true, false);
            c.Check(!player.m_run, "Shift does not run");
            rig.TakeControls();

            // Left click stops: no punch, no block; arms down; game music back.
            yield return Click(rig);
            c.Check(Performance.Mode == PerformanceMode.None && !player.InAttack() && !player.IsBlocking(), "left click stops it, no punch, no block");
            c.Check(PlayingFlag(player) == 0, "playing flag cleared");
            yield return new WaitForSeconds(0.6f);
            c.Check(Performance.PoseWeight < 0.01f && InstrumentPose.Weight(player) < 0.01f, "the arms come down");
            yield return WaitFor(() => Listeners.Duck > 0.999f, 2.5f);
            c.Check(Listeners.Duck > 0.999f, "the game's music comes back");
            c.Check(!MiniGameHud.AutoShown, "status line gone");
            // Right click (Block) stops too.
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var error), "plays again: " + error);
            yield return new WaitForSeconds(0.5f);
            yield return BlockClick(rig, player);
            c.Check(Performance.Mode == PerformanceMode.None && !player.IsBlocking() && !player.m_blocking, "right click stops it and does not leave the block up");

            // Repeat with a short song made here: time starts over only after its end; off: it stops by itself.
            var notes = new List<Note>();
            AddRun(notes, 0.3f, 6, 0.5f, 0.4f);
            File.WriteAllBytes(Path.Combine(folder, "selftest short.mid"), MidiFrom(notes));
            var list = SongLibrary.ScanMidiFolder(out _);
            var shortSong = list.Count == 1 ? list[0] : null;
            if (c.Check(shortSong != null, "short song listed"))
            {
                Performance.Repeat = true;
                var playedAtStart = Performance.NotesPlayed;
                c.Check(Performance.StartAuto(shortSong, -1, out error), "short song plays: " + error);
                yield return Frames(2);
                var length = Performance.SongLength;
                var top = 0f;
                var restarted = false;
                var early = false;
                var playedAtRestart = 0;
                var began = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - began < length + 5f && Performance.Mode == PerformanceMode.Auto)
                {
                    var t = Performance.SongTime;
                    if (!restarted && t + 0.5f < top)
                    {
                        restarted = true;
                        early = top < length - 0.15f;
                        playedAtRestart = Performance.NotesPlayed;
                        c.Check(MiniGameHud.AutoText != null && MiniGameHud.AutoText.Contains("   0:00 / ") && MiniGameHud.AutoText.EndsWith("(repeat)", StringComparison.Ordinal),
                            "status line starts over at 0:00 and says repeat: " + MiniGameHud.AutoText);
                    }
                    if (restarted && t > 1.5f)
                    {
                        break;
                    }
                    top = Mathf.Max(top, t);
                    yield return null;
                }
                c.Check(restarted && !early, $"Repeat: the time starts over after the song's end, not before (reached {F(top)} of {F(length)} s)");
                // Notes are queued half a second ahead: at the restart the first note of the new pass is already in.
                c.Check(Performance.Mode == PerformanceMode.Auto && playedAtRestart - playedAtStart >= notes.Count
                        && playedAtRestart - playedAtStart <= notes.Count + 2 && Performance.NotesPlayed - playedAtStart >= notes.Count + 3,
                    $"Repeat: the whole song played, then again from its first note ({playedAtRestart - playedAtStart} notes, then {Performance.NotesPlayed - playedAtRestart} more)");
                Performance.Repeat = false;
                Performance.TestRequestStop();
                yield return Frames(3);
                c.Check(Performance.StartAuto(shortSong, -1, out error), "short song without Repeat: " + error);
                began = Time.realtimeSinceStartup;
                yield return WaitReal(() => Performance.Mode == PerformanceMode.None, length + 3f);
                var lasted = Time.realtimeSinceStartup - began;
                c.Check(Performance.Mode == PerformanceMode.None && lasted > length - 0.2f && lasted < length + 1.5f,
                    $"Repeat off: it stops by itself at the end ({F(lasted)} s for a {F(length)} s song)");
            }
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.nocomfort (T11) ----------

    private static IEnumerator RunNoComfort()
    {
        var c = new Checks(NoComfortName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(NoComfortName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            // The Encore bar as low as the rules allow: a song playing by itself runs twice that long here.
            ServerRules.TestRules = TestRules(MusicRules.SuccessSecondsMin);
            player.GetSEMan().RemoveStatusEffect(InstrumentContent.EffectHash, true);
            Listeners.ForgetEncores();
            Seen.Clear();
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            c.Check(Performance.StartAuto(Preset("kjerringa-med-staven"), -1, out var error), "plays by itself: " + error);
            var meter = false;
            var encore = false;
            var effect = false;
            var began = Time.realtimeSinceStartup;
            var wanted = MusicRules.SuccessSecondsMin * 2f + 1f;
            while (Time.realtimeSinceStartup - began < wanted && Performance.Mode == PerformanceMode.Auto)
            {
                meter |= MiniGameHud.MiniShown || Performance.Game != null;
                encore |= (Performance.LastFlags & BatchFlags.Encore) != 0 || MiniGameHud.EncoreShown.Length > 0;
                effect |= player.GetSEMan().HaveStatusEffect(InstrumentContent.EffectHash);
                yield return null;
            }
            c.Check(Performance.Mode == PerformanceMode.Auto, $"still playing after {F(wanted)} s");
            c.Check(!meter, "no meter, no rhythm game");
            c.Check(!encore, "no Encore (none shown, none sent)");
            c.Check(!effect && Seen.Count(MessageHud.MessageType.TopLeft, "warms") == 0, "no Music effect, no message");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.instruments (T06) ----------

    private static IEnumerator RunInstruments()
    {
        var c = new Checks(InstrumentsName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(InstrumentsName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            var song = Preset("mead-hall-reel");
            // Lyre: the tune with chords under it (more notes than the tune, several struck together).
            c.Check(SongLibrary.Arrange(song, InstrumentKind.Flute, -1, out var tune, out _, out _), "tune arranged");
            c.Check(SongLibrary.Arrange(song, InstrumentKind.Lyre, -1, out var lyre, out _, out _), "lyre arranged");
            var chords = 0;
            for (var i = 0; i < lyre.Length;)
            {
                var j = i + 1;
                while (j < lyre.Length && lyre[j].Time - lyre[i].Time < 0.12f)
                {
                    j++;
                }
                if (j - i >= 2)
                {
                    chords++;
                }
                i = j;
            }
            c.Check(lyre.Length >= tune.Length + 8 && chords >= 4, $"lyre: the tune ({tune.Length} notes) with chords under it ({lyre.Length} notes, {chords} chords)");
            // Tambourine: thumps, hits and jingles.
            c.Check(SongLibrary.Arrange(song, InstrumentKind.Tambourine, -1, out var drum, out _, out _), "tambourine arranged");
            var kinds = new HashSet<byte>();
            foreach (var n in drum)
            {
                kinds.Add(n.Pitch);
            }
            c.Check(kinds.Contains((byte)TambourineHit.Thump) && kinds.Contains((byte)TambourineHit.Hit) && kinds.Contains((byte)TambourineHit.Jingle),
                $"tambourine: thumps, hits and jingles ({kinds.Count} kinds of hit)");
            var steady = true;
            for (var i = 1; i < drum.Length; i++)
            {
                steady &= drum[i].Time - drum[i - 1].Time < 1.5f;
            }
            c.Check(drum.Length > 30 && steady, "tambourine: a steady rhythm over the whole song");

            foreach (var kind in new[] { InstrumentKind.Lyre, InstrumentKind.Tambourine })
            {
                rig.Hold(kind);
                yield return new WaitForSeconds(0.5f);
                c.Check(Performance.StartAuto(song, -1, out var error), kind + " plays Mead Hall Reel: " + error);
                yield return new WaitForSeconds(1.4f);
                var rms = new float[1];
                yield return Measure(Performance.LocalEmitter, 0.9f, rms);
                c.Check(rms[0] > 0.002f, $"{kind} sounds (rms {F(rms[0])})");
                c.Check(Performance.LocalEmitter != null && Performance.LocalEmitter.Kind == kind, kind + " has its own sound");
                c.Check(InstrumentPose.Weight(player) > 0.95f, kind + " pose fully in");
                c.Check(InstrumentPose.PulseAge(player) < 1.2f, $"{kind}: the notes move the pose (last one {F(InstrumentPose.PulseAge(player))} s ago)");
                if (kind == InstrumentKind.Lyre)
                {
                    c.Check(InstrumentPose.InstanceMoved(player), "lyre held against the chest");
                }
                c.Note(kind + ": " + InstrumentPose.Describe(player));
                Performance.TestRequestStop();
                yield return new WaitForSeconds(0.5f);
                player.UnequipItem(player.GetRightItem(), false);
                yield return Frames(3);
            }
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.midi (T07) ----------

    private static bool SameNotes(Note[] a, Note[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i].Pitch != b[i].Pitch || Mathf.Abs(a[i].Time - b[i].Time) > 0.002f)
            {
                return false;
            }
        }
        return true;
    }

    private static IEnumerator RunMidi()
    {
        var c = new Checks(MidiName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(MidiName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.NoShare();
            var folder = x.SongsFolder("MC_MusicMidiTest");
            File.WriteAllBytes(Path.Combine(folder, "selftest tune.mid"), MidiOf(Preset("greensleeves")));
            File.WriteAllBytes(Path.Combine(folder, "selftest broken.mid"), System.Text.Encoding.ASCII.GetBytes("this is not a MIDI file, only text"));
            const string tuneId = "midi:selftest tune.mid";
            const string brokenId = "midi:selftest broken.mid";
            var problems = LogTap.ProblemMark;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWindow(rig);
            var items = SongWindow.TestItems();
            c.Check(SongWindow.IsOpen && items.Contains("song|" + tuneId + "|selftest tune") && items.Contains("song|" + brokenId + "|selftest broken"),
                "both files are listed");
            c.Check(SongWindow.TestSelect(tuneId), "file picked");
            yield return Frames(2);
            var details = SongWindow.DetailsText ?? "";
            c.Check(details.Contains("2 parts") && details.Contains(":") && details.StartsWith("selftest tune", StringComparison.Ordinal),
                "picking it shows its parts and length: " + details);
            var entry = SongLibrary.ScanMidiFolder(out _).Find(e => e.Id == tuneId);
            if (!c.Check(entry != null && entry.Parts.Count == 2, "file read: two parts"))
            {
                c.Report();
                yield break;
            }
            c.Check(SongWindow.PartText != null && SongWindow.PartText.StartsWith("Part: Automatic (", StringComparison.Ordinal),
                "part chooser shown, on Automatic: " + SongWindow.PartText);
            // Flute, automatic: the tune.
            c.Check(SongWindow.TestPress("Play"), "Play pressed");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastPart == -1, "Play plays it");
            var auto = (Note[])Performance.TestNotes.Clone();
            c.Check(SongLibrary.Arrange(entry, InstrumentKind.Flute, SongLibrary.AutoPart(entry, InstrumentKind.Flute), out var best, out _, out _)
                    && SameNotes(auto, best), "on the flute: the part that looks most like the tune");
            Performance.TestRequestStop();
            yield return Frames(3);
            // Another part with the chooser: that part plays.
            yield return OpenWindow(rig);
            c.Check(SongWindow.TestSelect(tuneId), "picked again");
            var other = SongLibrary.AutoPart(entry, InstrumentKind.Flute) == 0 ? 1 : 0;
            for (var i = 0; i <= other; i++)
            {
                c.Check(SongWindow.TestPress("Part"), "part chooser pressed");
            }
            c.Check(SongWindow.SelectedPart == other && SongWindow.PartText == "Part: " + entry.Parts[other], "chooser on the other part: " + SongWindow.PartText);
            c.Check(SongWindow.TestPress("Play"), "Play pressed");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastPart == other, "that part is the one playing");
            c.Check(SongLibrary.Arrange(entry, InstrumentKind.Flute, other, out var chosen, out _, out _)
                    && SameNotes(Performance.TestNotes, chosen) && !SameNotes(Performance.TestNotes, auto), "its notes, not the automatic ones");
            Performance.TestRequestStop();
            yield return Frames(3);
            // Lyre: the tune with a bass line; tambourine: hits.
            var tuneOnly = SongLibrary.Arrange(entry, InstrumentKind.Lyre, SongLibrary.BestMelody(entry.Score), out var lyreTune, out _, out _);
            c.Check(tuneOnly && SongLibrary.Arrange(entry, InstrumentKind.Lyre, -1, out var lyre, out _, out _) && lyre.Length > lyreTune.Length,
                "on the lyre: the tune with a bass line");
            c.Check(SongLibrary.Arrange(entry, InstrumentKind.Tambourine, -1, out var drum, out _, out _) && drum.Length > 8
                    && Array.TrueForAll(drum, n => n.Pitch <= (byte)TambourineHit.Shake), "on the tambourine: hits on the tune's rhythm");

            // A broken file says why; nothing breaks.
            yield return OpenWindow(rig);
            c.Check(SongWindow.TestSelect(brokenId), "broken file picked");
            yield return Frames(2);
            details = SongWindow.DetailsText ?? "";
            var rowInfo = SongWindow.TestRowInfo(brokenId) ?? "";
            c.Check(details.StartsWith("selftest broken: The file ", StringComparison.Ordinal) && rowInfo.StartsWith("The file ", StringComparison.Ordinal),
                "it shows why it cannot play: " + details);
            c.Check(SongWindow.PartText == null && !SongWindow.TestPress("Play") && !SongWindow.TestPress("Perform"), "Play and Perform are off for it");
            SongWindow.TestDownKey = KeyCode.Return;
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.None && SongWindow.IsOpen, "Enter on it starts nothing, the window stays");
            c.Check(SongWindow.TestSelect("preset:greensleeves") && SongWindow.TestPress("Play"), "another song still plays");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto, "playing");
            c.Check(LogTap.ProblemsSince(problems).Count == 0, "no warning or error in the log");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.rhythm (T08) ----------

    private static IEnumerator RunRhythm()
    {
        var c = new Checks(RhythmName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(RhythmName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(20f);
            x.NoShare();
            var folder = x.SongsFolder("MC_MusicRhythmTest");
            // The default keys, whatever the player's own are (memory only).
            LaneKeys.TestSet(0, KeyCode.D);
            LaneKeys.TestSet(1, KeyCode.F);
            LaneKeys.TestSet(2, KeyCode.J);
            LaneKeys.TestSet(3, KeyCode.K);
            var torch = rig.Give("Torch");
            var flute = rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen && SongWindow.TestSelect("preset:kjerringa-med-staven") && SongWindow.TestPress("Perform"), "Kjerringa med staven, Perform");
            yield return Frames(2);
            var game = Performance.Game;
            if (!c.Check(game != null && Performance.Mode == PerformanceMode.MiniGame && MiniGameHud.MiniShown, "the rhythm game runs"))
            {
                c.Report();
                yield break;
            }
            c.Check(Chart.Lanes == 4 && LaneKeys.Key(0) == KeyCode.D && LaneKeys.Key(1) == KeyCode.F && LaneKeys.Key(2) == KeyCode.J
                    && LaneKeys.Key(3) == KeyCode.K, "four lanes on D F J K");
            var lanesUsed = new HashSet<int>();
            var lanesOk = true;
            foreach (var n in game.Chart.Notes)
            {
                lanesUsed.Add(n.Lane);
                lanesOk &= n.Lane < Chart.Lanes;
            }
            c.Check(lanesOk && lanesUsed.Count >= 3, $"the notes are spread over the lanes ({lanesUsed.Count} used)");

            // Lead-in: countdown 3 2 1; meanwhile nothing else works (walk, inventory, hotbar).
            var counts = new List<string>();
            var controller = x.ControllerObject;
            var blockedOk = true;
            var hotbar = ToHotbar(player, torch);
            var from = player.transform.position;
            var step = 0;
            while (game.LeadInLeft > 0f && Performance.Mode == PerformanceMode.MiniGame)
            {
                var shown = MiniGameHud.CountdownShown;
                if (shown.Length > 0 && (counts.Count == 0 || counts[counts.Count - 1] != shown))
                {
                    counts.Add(shown);
                }
                blockedOk &= KeyCapture.Active && Chat.instance.HasFocus() && controller != null && !controller.TakeInput(false);
                step++;
                if (step == 5)
                {
                    Press("Inventory");
                    player.SetControls(new Vector3(0f, 0f, 1f), false, false, false, false, false, false, true, false, false, false);
                }
                else if (step == 9)
                {
                    Let("Inventory");
                    if (hotbar != null)
                    {
                        Press(hotbar);
                    }
                }
                else if (step == 13 && hotbar != null)
                {
                    Let(hotbar);
                }
                if (step > 5)
                {
                    blockedOk &= !InventoryGui.IsVisible();
                }
                yield return null;
            }
            rig.TakeControls();
            c.Check(counts.Count == 3 && counts[0] == "3" && counts[1] == "2" && counts[2] == "1", "countdown 3 2 1: " + string.Join(" ", counts.ToArray()));
            c.Check(MiniGameHud.CountdownShown.Length == 0, "countdown gone when the song starts");
            c.Check(blockedOk, "keys held back, the inventory key opens nothing");
            c.Check(Flat(player.transform.position, from) < 0.05f && player.IsOnGround(), "no walking, no jumping");
            c.Check(hotbar != null, "torch on a hotbar key: " + hotbar);
            c.Check(player.GetRightItem() == flute && !player.IsItemEquiped(torch), "the hotbar key equips nothing");
            for (var lane = 0; lane < Chart.Lanes; lane++)
            {
                c.Check(MiniGameHud.LaneCaption(lane) == LaneKeys.Label(lane) && LaneKeys.Label(lane).Length > 0 && LaneKeys.Label(lane) != "-",
                    $"lane {lane + 1} shows its key ({MiniGameHud.LaneCaption(lane)})");
            }

            // Notes: 0-2 on time, 3 a bit late (Good), 6 not played (Miss), the rest on time.
            var order = new Dictionary<long, int>();
            var pressed = new HashSet<long>();
            var times = new Dictionary<int, float>();
            var checkFrame = -1;
            var checkWhat = "";
            string[] checkTexts = null;
            var perfects = 0;
            var playedBeforeMiss = -1;
            var missChecked = false;
            var fallKey = -1L;
            var fallLeft = 0f;
            var fallAt = 0f;
            var fallNow = 0f;
            var fallOk = false;
            var began = Time.realtimeSinceStartup;
            var emitter = Performance.LocalEmitter;
            if (emitter != null)
            {
                emitter.ResetMeasure();
            }
            while (Time.realtimeSinceStartup - began < 25f && Performance.Mode == PerformanceMode.MiniGame && (order.Count < 10 || pressed.Count < 9 || !missChecked))
            {
                if (checkFrame >= 0 && Time.frameCount >= checkFrame)
                {
                    var text = MiniGameHud.JudgementShown;
                    c.Check(Array.IndexOf(checkTexts, text) >= 0, $"{checkWhat}: shows '{text}'");
                    if (text == "Perfect")
                    {
                        perfects++;
                    }
                    checkFrame = -1;
                }
                if (!missChecked && times.ContainsKey(6) && game.Clock > times[6] + Judge.GoodWindow + 0.03f && checkFrame < 0)
                {
                    missChecked = true;
                    c.Check(MiniGameHud.JudgementShown == "Miss", $"a note left unplayed shows 'Miss' ('{MiniGameHud.JudgementShown}')");
                    c.Check(playedBeforeMiss >= 0 && Performance.ChartNotesPlayed == playedBeforeMiss, "and plays nothing");
                }
                var fallSeen = false;
                foreach (var v in game.Visible)
                {
                    var key = NoteKey(v);
                    var t = game.NoteTime(v.Key, v.Value);
                    if (!order.ContainsKey(key))
                    {
                        order[key] = order.Count;
                        times[order[key]] = t;
                    }
                    // One note still well above the line is watched coming down: in the list the HUD draws from, a
                    // third of a second later it is still there and nearer the line. Not the song's first note (the
                    // old test took that one): the lead-in is over by now and it is at the line already. Tried again
                    // with a later note when a hitch spoiled the reading.
                    if (!fallOk && fallKey < 0 && t - game.Clock > 0.5f)
                    {
                        fallKey = key;
                        fallLeft = t - game.Clock;
                        fallAt = game.Clock;
                    }
                    if (key == fallKey)
                    {
                        fallSeen = true;
                        if (game.Clock >= fallAt + 0.3f)
                        {
                            fallNow = t - game.Clock;
                            fallOk = fallLeft <= game.LookAhead + 0.1f && fallNow < fallLeft - 0.25f && fallNow > 0f;
                            fallKey = -1;
                        }
                    }
                    var n = order[key];
                    if (pressed.Contains(key) || checkFrame >= 0)
                    {
                        continue;
                    }
                    if (n == 6)
                    {
                        if (playedBeforeMiss < 0 && game.Clock > t - 0.12f)
                        {
                            playedBeforeMiss = Performance.ChartNotesPlayed;
                        }
                        continue;
                    }
                    if (n > 6 && !missChecked)
                    {
                        continue; // the Miss is read first
                    }
                    if (Due(game, t, n == 3 ? 0.075f : -0.005f))
                    {
                        pressed.Add(key);
                        LaneKeys.TestPress[game.Lane(v.Value)] = true;
                        checkFrame = Time.frameCount + 1;
                        checkWhat = n == 3 ? "a late press" : "a press on time";
                        checkTexts = n == 3 ? new[] { "Good" } : new[] { "Perfect", "Good" };
                    }
                }
                if (fallKey >= 0 && !fallSeen)
                {
                    fallKey = -1; // the watched note left the list before its time: no pass, another one is tried
                }
                yield return null;
            }
            yield return Frames(2);
            c.Check(fallOk && times.Count >= 10,
                $"notes are shown ahead of their time and come nearer the line ({F(fallLeft)} s -> {F(fallNow)} s before it, {times.Count} notes seen)");
            c.Check(missChecked, "the unplayed note was judged");
            c.Check(perfects >= 3, $"presses on time show 'Perfect' ({perfects} of {pressed.Count})");
            c.Check(game.Judge.Hits == pressed.Count && game.Judge.Misses == 1, $"every press hit ({game.Judge.Hits} hits, {game.Judge.Misses} miss)");
            c.Check(emitter != null && emitter.Rms > 0.001f, $"the hit notes sound (rms {F(emitter != null ? emitter.Rms : 0f)})");
            c.Check(Performance.ChartNotesPlayed > 0 && (Performance.LastFlags & BatchFlags.Live) != 0, "and are sent as live notes");

            // Right mouse button stops it (raw read: test hook).
            Performance.TestRightClick = true;
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.None && Performance.Game == null && !MiniGameHud.MiniShown, "right click stops it");
            yield return Frames(2);
            c.Check(!KeyCapture.Active && !Chat.instance.HasFocus(), "keys given back");
            // Keys work again: the inventory key opens the inventory now.
            yield return Tap("Inventory");
            yield return Frames(3);
            c.Check(InventoryGui.IsVisible(), "after the stop the inventory key works again");
            InventoryGui.instance.Hide();
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);

            // The song repeats: a short song made here comes round again and can be hit again.
            var notes = new List<Note>();
            AddRun(notes, 0.3f, 6, 0.4f);
            File.WriteAllBytes(Path.Combine(folder, "selftest loop.mid"), MidiFrom(notes));
            var list = SongLibrary.ScanMidiFolder(out _);
            string error = null;
            c.Check(list.Count == 1 && Performance.StartMiniGame(list[0], -1, out error), "short song in the rhythm game: " + error);
            yield return Frames(2);
            game = Performance.Game;
            var loopHit = false;
            pressed.Clear();
            began = Time.realtimeSinceStartup;
            while (game != null && !loopHit && Time.realtimeSinceStartup - began < 14f && Performance.Mode == PerformanceMode.MiniGame)
            {
                foreach (var v in game.Visible)
                {
                    var key = NoteKey(v);
                    if (v.Key >= 1 && !pressed.Contains(key) && Due(game, game.NoteTime(v.Key, v.Value), -0.005f))
                    {
                        pressed.Add(key);
                        var hits = game.Judge.Hits;
                        LaneKeys.TestPress[game.Lane(v.Value)] = true;
                        yield return Frames(2);
                        loopHit = game.Judge.Hits == hits + 1;
                        break;
                    }
                }
                yield return null;
            }
            c.Check(loopHit, "the song repeats: its first note comes again and plays when hit");

            // Esc: the menu opens (the game is paused) and the rhythm game is over.
            yield return Tap("JoyMenu");
            yield return WaitReal(() => MenuShown, 1f);
            yield return Frames(3);
            c.Check(MenuShown, "Esc opens the menu");
            c.Check(Performance.Mode == PerformanceMode.None && !MiniGameHud.MiniShown, "and stops the rhythm game");
            if (MenuShown)
            {
                Menu.instance.Hide();
            }
            yield return Real(0.3f);
            c.Check(!KeyCapture.Active, "keys given back after Esc");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.encore (T09, banner of T23) ----------

    private static IEnumerator RunEncore()
    {
        var c = new Checks(EncoreName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(EncoreName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            var rules = TestRules(6f);
            rules.BonusMinutes = 10f;
            ServerRules.TestRules = rules;
            // Me read (never write) what untouched settings give: the +3 and the 10 minutes below are the real defaults.
            c.Check(MusicRules.Default.ComfortBonus == 3 && Mathf.Approximately(MusicRules.Default.BonusMinutes, 10f)
                    && Plugin.ComfortBonus != null && (int)Plugin.ComfortBonus.DefaultValue == 3
                    && Plugin.BonusMinutes != null && Mathf.Approximately((float)Plugin.BonusMinutes.DefaultValue, 10f),
                "default settings: +3 comfort for 10 minutes");
            var seman = player.GetSEMan();
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);
            yield return WaitFor(() => player.m_comfortLevel > 0, 4f);
            var baseComfort = player.m_comfortLevel;
            c.Check(baseComfort > 0 && player.GetComfortLevel() == baseComfort, $"comfort without Music: {baseComfort}");
            // Rested already running, at today's comfort.
            seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
            var rested = seman.AddStatusEffect(SEMan.s_statusEffectRested, resetTime: true) as SE_Rested;
            if (!c.Check(rested != null, "Rested running"))
            {
                c.Report();
                yield break;
            }
            var restedBefore = rested.m_ttl - rested.m_time;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            Seen.Clear();
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), "rhythm game starts: " + error);
            yield return Frames(2);
            var game = Performance.Game;
            if (!c.Check(game != null, "game running"))
            {
                c.Report();
                yield break;
            }
            // First five notes left unplayed (the meter waits, red), then every note hit (it fills, gold) to two Encores.
            var pressed = new HashSet<long>();
            var order = new Dictionary<long, int>();
            var states = new List<string>();
            var redSeen = false;
            var goldSeen = false;
            var filledAtRed = -1f;
            var dropped = false;
            var lastFilled = 0f;
            var banner = "";
            var lastEncores = 0;
            var encoreOne = false;
            var iconOne = "";
            var comfortOne = 0;
            var timeBeforeTwo = 0f;
            var restedAfter = 0f;
            var began = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - began < 45f && Performance.Mode == PerformanceMode.MiniGame && game.Encores < 2)
            {
                foreach (var v in game.Visible)
                {
                    var key = NoteKey(v);
                    if (!order.ContainsKey(key))
                    {
                        order[key] = order.Count;
                    }
                    if (order[key] >= 5 && !pressed.Contains(key) && Due(game, game.NoteTime(v.Key, v.Value), -0.005f))
                    {
                        pressed.Add(key);
                        LaneKeys.TestPress[game.Lane(v.Value)] = true;
                    }
                }
                var state = MiniGameHud.MeterStateText ?? "";
                if (states.Count == 0 || states[states.Count - 1] != state)
                {
                    states.Add(state);
                }
                var colour = (Color32)MiniGameHud.MeterStateColor;
                if (state == "Hit more notes: the meter waits" && game.Encores == 0)
                {
                    redSeen |= colour.r > 200 && colour.g < 150;
                    filledAtRed = Mathf.Max(filledAtRed, game.Meter.Filled);
                }
                else if (state == "In tune: the meter fills")
                {
                    goldSeen |= colour.r == NoteField.MeterFlow.r && colour.g == NoteField.MeterFlow.g && colour.b == NoteField.MeterFlow.b;
                }
                // Never down, except when a full meter starts again.
                if (game.Encores != lastEncores)
                {
                    lastEncores = game.Encores;
                }
                else if (game.Meter.Filled < lastFilled - 0.001f)
                {
                    dropped = true;
                }
                lastFilled = game.Meter.Filled;
                if (game.Encores == 1 && !encoreOne)
                {
                    encoreOne = true;
                    yield return null;
                    banner = MiniGameHud.EncoreShown;
                    var effect = seman.GetStatusEffect(InstrumentContent.EffectHash);
                    iconOne = effect != null ? effect.GetIconText() : "(no effect)";
                    comfortOne = player.GetComfortLevel();
                    // What the Resting effect does every frame by the fire: Rested started again at today's comfort.
                    seman.AddStatusEffect(SEMan.s_statusEffectRested, resetTime: true);
                    restedAfter = rested.m_ttl - rested.m_time;
                }
                if (game.Encores == 1)
                {
                    var effect = seman.GetStatusEffect(InstrumentContent.EffectHash);
                    timeBeforeTwo = effect != null ? effect.m_time : 0f;
                }
                yield return null;
            }
            c.Note("meter states: " + string.Join(" > ", states.ToArray()));
            c.Check(states.Count > 0 && states[0] == "Hit a few notes to start", "meter says how to start");
            c.Check(redSeen && filledAtRed < 0.01f, "missed notes: the meter waits (red), nothing filled");
            c.Check(goldSeen, "notes hit: the meter fills (gold)");
            c.Check(!dropped, "the meter never goes down");
            c.Check(encoreOne && banner == "Encore! +3 comfort", $"Encore banner: '{banner}'");
            c.Check(Seen.Count(MessageHud.MessageType.TopLeft, "Your music warms everyone near you: +3 comfort.") == 1, "message: " + Seen.Tail());
            c.Check(iconOne == "+3  10:00" || iconOne == "+3  9:59" || iconOne == "+3  9:58", $"Music icon shows +3 and ten minutes: '{iconOne}'");
            c.Check(comfortOne == baseComfort + 3, $"comfort 3 higher at once ({baseComfort} -> {comfortOne})");
            c.Check(Mathf.Abs(restedAfter - (restedBefore + 3f * rested.m_TTLPerComfortLevel)) < 1.5f && Mathf.Approximately(rested.m_TTLPerComfortLevel, 60f),
                $"Rested 3 minutes longer ({F(restedBefore)} s -> {F(restedAfter)} s)");
            c.Check(Seen.Count(MessageHud.MessageType.Center, "$se_rested_start") == 0, "no new \"You feel rested\" message");
            c.Check(game.Encores == 2, $"a second Encore after another stretch ({game.Encores} in {F(Time.realtimeSinceStartup - began)} s)");
            if (game.Encores == 2)
            {
                yield return null;
                var effect = seman.GetStatusEffect(InstrumentContent.EffectHash);
                c.Check(effect != null && timeBeforeTwo > 4f && effect.m_time < 1f && Mathf.Approximately(effect.m_ttl, 600f),
                    $"it renews Music to 10:00 (ran {F(timeBeforeTwo)} s, now {F(effect != null ? effect.m_time : -1f)} s)");
                c.Check(Seen.Count(MessageHud.MessageType.TopLeft, "warms") == 1, "no second message for the renewal");
            }
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.encore.easy (T29) ----------

    private static IEnumerator RunEncoreEasy()
    {
        var c = new Checks(EncoreEasyName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(EncoreEasyName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            // The real rule: half the notes for 20 seconds.
            ServerRules.TestRules = TestRules(MusicRules.Default.SuccessSeconds);
            c.Check(Mathf.Approximately(MusicRules.Default.SuccessSeconds, 20f) && Mathf.Approximately(MusicRules.Default.SuccessAccuracy, 0.5f),
                "default rule: 20 s at half the notes");
            c.Check(Plugin.SuccessAccuracy != null && Plugin.SuccessAccuracy.Definition.Key == "RequiredAccuracy"
                    && Mathf.Approximately((float)Plugin.SuccessAccuracy.DefaultValue, 0.5f), "setting RequiredAccuracy, default 0.5");
            player.GetSEMan().RemoveStatusEffect(InstrumentContent.EffectHash, true);
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            var song = Preset("kjerringa-med-staven");

            // Every other note, each second hit a bit late (Good): Encore between 20 and 45 s.
            c.Check(Performance.StartMiniGame(song, -1, out var error), "run starts: " + error);
            yield return Frames(2);
            var game = Performance.Game;
            var pressed = new HashSet<long>();
            var count = 0;
            var accuracySum = 0f;
            var accuracyN = 0;
            var shownOk = true;
            var lastFilled = 0f;
            var dropped = false;
            var began = Time.realtimeSinceStartup;
            while (game != null && game.Encores == 0 && Time.realtimeSinceStartup - began < 50f && Performance.Mode == PerformanceMode.MiniGame)
            {
                foreach (var v in game.Visible)
                {
                    var key = NoteKey(v);
                    if (pressed.Contains(key))
                    {
                        continue;
                    }
                    var play = pressed.Count % 2 == 0;
                    var late = pressed.Count % 4 == 0;
                    if (Due(game, game.NoteTime(v.Key, v.Value), late ? 0.075f : -0.005f))
                    {
                        pressed.Add(key);
                        if (play)
                        {
                            LaneKeys.TestPress[game.Lane(v.Value)] = true;
                            count++;
                        }
                    }
                }
                if (game.Accuracy >= 0f && game.Clock > Judge.LeadIn + 6f)
                {
                    accuracySum += game.Accuracy;
                    accuracyN++;
                    var text = MiniGameHud.AccuracyText ?? "";
                    shownOk &= text == "Accuracy  " + Mathf.RoundToInt(game.Accuracy * 100f) + "%";
                }
                dropped |= game.Meter.Filled < lastFilled - 0.001f;
                lastFilled = game.Meter.Filled;
                yield return null;
            }
            var took = Time.realtimeSinceStartup - began - Judge.LeadIn;
            var mean = accuracyN > 0 ? accuracySum / accuracyN : -1f;
            c.Note($"every other note: {count} presses, hits {game.Judge.Hits} ({game.Judge.Perfects} perfect), misses {game.Judge.Misses}, mean accuracy {F(mean)}, Encore after {F(took)} s");
            c.Check(game != null && game.Encores == 1 && took >= 19.5f && took <= 46f, $"every other note: the Encore comes after 20 to 45 s ({F(took)} s)");
            c.Check(game != null && game.Judge.Hits > game.Judge.Perfects && game.Judge.Hits >= count - 2, "a Good counts as much as a Perfect");
            c.Check(mean > 0.4f && mean < 0.62f && shownOk, $"the Accuracy shown is about 50 % ({F(mean * 100f)} % on average)");
            c.Check(!dropped, "the meter waits but never goes down");
            Performance.TestRequestStop();
            yield return Frames(3);

            // A wrong key now and then instead of a miss costs no more: same Encore (shorter bar to save time).
            ServerRules.TestRules = TestRules(6f);
            player.GetSEMan().RemoveStatusEffect(InstrumentContent.EffectHash, true);
            c.Check(Performance.StartMiniGame(song, -1, out error), "wrong-key run starts: " + error);
            yield return Frames(2);
            game = Performance.Game;
            pressed.Clear();
            began = Time.realtimeSinceStartup;
            while (game != null && game.Encores == 0 && Time.realtimeSinceStartup - began < 22f && Performance.Mode == PerformanceMode.MiniGame)
            {
                foreach (var v in game.Visible)
                {
                    var key = NoteKey(v);
                    if (!pressed.Contains(key) && Due(game, game.NoteTime(v.Key, v.Value), -0.005f))
                    {
                        pressed.Add(key);
                        var lane = game.Lane(v.Value);
                        LaneKeys.TestPress[pressed.Count % 2 == 0 ? lane : (lane + 1) % Chart.Lanes] = true;
                    }
                }
                yield return null;
            }
            c.Check(game != null && game.Encores == 1 && game.Judge.Strays >= 5 && game.Accuracy >= 0.45f,
                $"a wrong key instead of every other note: still the Encore (strays {(game != null ? game.Judge.Strays : 0)}, accuracy {F(game != null ? game.Accuracy : -1f)})");
            Performance.TestRequestStop();
            yield return Frames(3);

            // One note in four: red almost all the time, no Encore in three times the bar.
            player.GetSEMan().RemoveStatusEffect(InstrumentContent.EffectHash, true);
            c.Check(Performance.StartMiniGame(song, -1, out error), "one-in-four run starts: " + error);
            yield return Frames(2);
            game = Performance.Game;
            pressed.Clear();
            var red = 0;
            var frames = 0;
            began = Time.realtimeSinceStartup;
            while (game != null && Time.realtimeSinceStartup - began < 20f && Performance.Mode == PerformanceMode.MiniGame)
            {
                PressDue(game, pressed, 0, hitEvery: 4);
                if (game.Accuracy >= 0f && !game.Meter.Waiting)
                {
                    frames++;
                    if (MiniGameHud.MeterStateText == "Hit more notes: the meter waits")
                    {
                        red++;
                    }
                }
                yield return null;
            }
            c.Check(game != null && game.Encores == 0 && frames > 100 && red > frames * 0.9f,
                $"one note in four: no Encore, the meter is red almost all the time ({red} of {frames} frames, filled {F(game != null ? game.Meter.Filled : -1f)} s)");
            c.Check(!player.GetSEMan().HaveStatusEffect(InstrumentContent.EffectHash), "and no Music effect");
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.rest (T09, T10: a real fire, the game's own Resting and Rested) ----------

    // Needs things outside the mod: dry weather (forced here), a campfire that burns, and no creature that has
    // noticed the player. When one is missing the test says which.
    private static IEnumerator RunRest()
    {
        var c = new Checks(RestName);
        var player = Player.m_localPlayer;
        if (player == null || ZNetScene.instance == null || EnvMan.instance == null)
        {
            SelfTest.Fail(RestName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            x.Weather("Clear");
            var rules = TestRules();
            rules.BonusMinutes = 10f;
            ServerRules.TestRules = rules;
            var seman = player.GetSEMan();
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);
            seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
            seman.RemoveStatusEffect(SEMan.s_statusEffectWet, true);
            var prefab = ZNetScene.instance.GetPrefab("fire_pit");
            if (!c.Check(prefab != null, "campfire prefab fire_pit"))
            {
                c.Report();
                yield break;
            }
            var forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            var spot = player.transform.position + forward * 1.6f;
            spot.y = ZoneSystem.instance.GetGroundHeight(spot);
            var fireObject = Object.Instantiate(prefab, spot, Quaternion.identity);
            rig.Spawned(fireObject);
            yield return new WaitForSeconds(1.2f);
            var fire = fireObject != null ? fireObject.GetComponent<Fireplace>() : null;
            c.Check(fire != null && fire.IsBurning(), "the campfire burns");
            c.Check(!player.InShelter(), "outdoors (no shelter)");
            Seen.Clear();
            c.Check(player.StartEmote("sit", false), "sit down (X)");
            yield return WaitFor(() => player.IsSitting(), 4f);
            yield return WaitFor(() => seman.HaveStatusEffect(SEMan.s_statusEffectResting), 6f);
            var cozy = seman.GetStatusEffect(SEMan.s_statusEffectResting) as SE_Cozy;
            if (!c.Check(cozy != null,
                    $"Resting by the fire (sitting {player.IsSitting()}, noticed by a creature {player.IsSensed()}, near fire {F(player.m_nearFireTimer)} s ago, "
                    + $"wet {seman.HaveStatusEffect(SEMan.s_statusEffectWet)}, burning {seman.HaveStatusEffect(SEMan.s_statusEffectBurning)})"))
            {
                c.Report();
                yield break;
            }
            yield return WaitFor(() => player.m_comfortLevel > 0, 4f);
            string Comfort(int level) => Localization.instance.Localize("$se_rested_comfort:" + level);
            c.Check(player.GetComfortLevel() == 1 && cozy.GetIconText() == Comfort(1), $"outdoors: Resting shows comfort 1 ('{cozy.GetIconText()}')");
            // The game waits about 20 s before Rested: me skip the wait (its own timer), nothing else.
            c.Note($"Resting delay {F(cozy.m_delay)} s");
            cozy.m_time = Mathf.Max(cozy.m_time, cozy.m_delay - 0.3f);
            yield return WaitFor(() => seman.HaveStatusEffect(SEMan.s_statusEffectRested), 3f);
            var rested = seman.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Rested;
            if (!c.Check(rested != null, "Rested starts while resting"))
            {
                c.Report();
                yield break;
            }
            c.Check(Mathf.Abs(rested.m_ttl - 480f) < 0.5f && rested.GetIconText() == "8:00", $"Rested starts at 8:00 ({F(rested.m_ttl)} s, '{rested.GetIconText()}')");
            c.Check(Seen.Count(MessageHud.MessageType.Center, "$se_rested_start ($se_rested_comfort:1)") == 1, "\"You feel rested (Comfort: 1)\": " + Seen.Tail());

            // The Encore comes (what the rhythm game calls when the meter is full).
            var mark = Seen.Messages.Count;
            c.Check(MusicBonus.Grant(player, false), "Encore: Music given");
            yield return Frames(3);
            c.Check(player.GetComfortLevel() == 4 && cozy.GetIconText() == Comfort(4), $"Resting shows comfort 4 at once ('{cozy.GetIconText()}')");
            yield return WaitFor(() => rested.m_ttl > 659f, 2f);
            c.Check(Mathf.Abs(rested.m_ttl - rested.m_time - 660f) < 1.5f, $"the Rested timer jumps to 11:00 ({F(rested.m_ttl - rested.m_time)} s)");
            yield return new WaitForSeconds(1.5f);
            c.Check(Mathf.Abs(rested.m_ttl - rested.m_time - 660f) < 1.5f && rested.GetIconText() == "11:00",
                $"and stays there while resting ('{rested.GetIconText()}')");
            c.Check(Seen.Count(MessageHud.MessageType.Center, "$se_rested_start", mark) == 0, "no new \"You feel rested\" message");

            // Not Rested, Encore first, then sit by the fire: "You feel rested (Comfort: 4)" and 11:00.
            player.StopEmote();
            yield return WaitFor(() => !seman.HaveStatusEffect(SEMan.s_statusEffectResting), 4f);
            c.Check(!seman.HaveStatusEffect(SEMan.s_statusEffectResting), "standing up outdoors ends Resting");
            seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
            c.Check(seman.HaveStatusEffect(InstrumentContent.EffectHash), "Music still on");
            mark = Seen.Messages.Count;
            // Standing up takes a moment: the game refuses a new emote until the character can move again
            // (Player.StartEmote asks CanMove). Me ask until it says yes, like a player pressing X again.
            var satAgain = false;
            var sitUntil = Time.time + 5f;
            while (!satAgain && Time.time < sitUntil)
            {
                satAgain = player.StartEmote("sit", false);
                if (!satAgain)
                {
                    yield return null;
                }
            }
            c.Check(satAgain, "sit down again");
            yield return WaitFor(() => player.IsSitting(), 4f);
            yield return WaitFor(() => seman.HaveStatusEffect(SEMan.s_statusEffectResting), 8f);
            cozy = seman.GetStatusEffect(SEMan.s_statusEffectResting) as SE_Cozy;
            if (c.Check(cozy != null,
                    $"Resting again (sitting {player.IsSitting()}, noticed by a creature {player.IsSensed()}, near fire {F(player.m_nearFireTimer)} s ago, "
                    + $"wet {seman.HaveStatusEffect(SEMan.s_statusEffectWet)}, burning {seman.HaveStatusEffect(SEMan.s_statusEffectBurning)})"))
            {
                cozy.m_time = Mathf.Max(cozy.m_time, cozy.m_delay - 0.3f);
                yield return WaitFor(() => seman.HaveStatusEffect(SEMan.s_statusEffectRested), 3f);
                rested = seman.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Rested;
                c.Check(rested != null && Mathf.Abs(rested.m_ttl - 660f) < 0.5f, $"Rested starts at 11:00 ({F(rested != null ? rested.m_ttl : -1f)} s)");
                c.Check(Seen.Count(MessageHud.MessageType.Center, "$se_rested_start ($se_rested_comfort:4)", mark) == 1,
                    "\"You feel rested (Comfort: 4)\": " + Seen.Tail());
            }

            // In a shelter (roof and walls: me give the player the cover a house gives; the game then counts shelter
            // comfort itself): the same +3 on top, Rested 3 minutes longer, no new message.
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);
            var began = Time.time;
            var sheltered = 0;
            while (Time.time - began < 6f && (sheltered < 2 || !player.InShelter()))
            {
                player.m_underRoof = true;
                player.m_coverPercentage = 1f;
                player.m_updateCoverTimer = 0f;
                if (player.InShelter() && player.m_comfortLevel >= 2)
                {
                    sheltered++;
                }
                yield return null;
            }
            var inside = player.m_comfortLevel;
            // Both said out loud: a shelter part that never ran must not look like a pass.
            if (c.Check(player.InShelter() && inside >= 2 && player.GetComfortLevel() == inside, $"in a shelter the game counts comfort {inside}")
                && c.Check(cozy != null && seman.HaveStatusEffect(SEMan.s_statusEffectResting), "still Resting by the fire in the shelter"))
            {
                seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
                for (var i = 0; i < 20 && !seman.HaveStatusEffect(SEMan.s_statusEffectRested); i++)
                {
                    player.m_underRoof = true;
                    player.m_coverPercentage = 1f;
                    player.m_updateCoverTimer = 0f;
                    yield return null;
                }
                rested = seman.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Rested;
                var plain = rested != null ? rested.m_ttl - rested.m_time : -1f;
                c.Check(rested != null && Mathf.Abs(plain - (480f + (inside - 1) * 60f)) < 1.5f, $"Rested without Music in the shelter: {F(plain)} s");
                mark = Seen.Messages.Count;
                c.Check(MusicBonus.Grant(player, false), "Encore in the shelter");
                for (var i = 0; i < 30; i++)
                {
                    player.m_underRoof = true;
                    player.m_coverPercentage = 1f;
                    player.m_updateCoverTimer = 0f;
                    yield return null;
                }
                c.Check(player.GetComfortLevel() == inside + 3 && cozy.GetIconText() == Comfort(inside + 3),
                    $"the Resting icon shows comfort 3 higher ('{cozy.GetIconText()}')");
                var with = rested != null ? rested.m_ttl - rested.m_time : -1f;
                c.Check(rested != null && Mathf.Abs(with - (plain + 180f)) < 2f, $"the Rested timer jumps 3 minutes higher ({F(plain)} s -> {F(with)} s)");
                c.Check(Seen.Count(MessageHud.MessageType.Center, "$se_rested_start", mark) == 0, "no new \"You feel rested\" message");
                var music = seman.GetStatusEffect(InstrumentContent.EffectHash);
                c.Check(music != null && music.GetIconText().StartsWith("+3  ", StringComparison.Ordinal), "Music icon with +3");
            }
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.seated (T12) ----------

    private static IEnumerator RunSeated()
    {
        var c = new Checks(SeatedName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(SeatedName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        var toggleBlock = player.m_toggleBlock;
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.NoShare();
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            foreach (var toggle in new[] { false, true })
            {
                // Settings, Accessibility, Toggle block: the game keeps it on the player.
                player.m_toggleBlock = toggle;
                player.m_blocking = false;
                var label = toggle ? "Toggle block on: " : "";
                c.Check(player.StartEmote("sit", false), label + "sit down (X)");
                yield return WaitFor(() => player.IsSitting(), 4f);
                c.Check(player.IsSitting(), label + "seated");
                yield return OpenWindow(rig);
                c.Check(SongWindow.IsOpen && player.InEmote(), label + "the click opens the window, still seated");
                c.Check(SongWindow.TestSelect("preset:hearthfire-lullaby") && SongWindow.TestPress("Play"), label + "Play");
                yield return new WaitForSeconds(0.4f);
                c.Check(Performance.Mode == PerformanceMode.Auto && player.InEmote(), label + "playing, seated");
                yield return Click(rig);
                c.Check(Performance.Mode == PerformanceMode.None && player.InEmote() && !player.m_blocking, label + "left click stops: seated, not blocking");
                c.Check(Performance.StartAuto(Preset("hearthfire-lullaby"), -1, out var error), label + "plays again: " + error);
                yield return new WaitForSeconds(0.3f);
                yield return BlockClick(rig, player);
                yield return Frames(3);
                c.Check(Performance.Mode == PerformanceMode.None && player.InEmote() && !player.m_blocking && !player.IsBlocking(),
                    label + "right click stops: seated, not blocking");
                // The real Block button held through the stop, the game reading the keys itself.
                c.Check(Performance.StartAuto(Preset("hearthfire-lullaby"), -1, out error), label + "plays a third time: " + error);
                yield return new WaitForSeconds(0.3f);
                x.Controller(true);
                Press("Block");
                yield return new WaitForSeconds(0.3f);
                c.Check(Performance.Mode == PerformanceMode.None, label + "the Block button stops the song");
                yield return new WaitForSeconds(0.3f);
                c.Check(player.InEmote() && !player.m_blocking && !player.IsBlocking(), label + "still seated and not left in the block stance");
                Let("Block");
                x.Controller(false);
                rig.TakeControls();
                // Window closed with the right mouse button (Block held like the real button).
                yield return OpenWindow(rig);
                c.Check(SongWindow.IsOpen, label + "window open");
                x.Controller(true);
                Press("Block");
                Performance.TestRightClick = true;
                yield return new WaitForSeconds(0.4f);
                c.Check(!SongWindow.IsOpen && player.InEmote() && !player.m_blocking && !player.IsBlocking(),
                    label + "right mouse button closes the window: seated, not blocking");
                Let("Block");
                x.Controller(false);
                rig.TakeControls();
                // Rhythm game: start, stop with the right mouse button.
                c.Check(Performance.StartMiniGame(Preset("hearthfire-lullaby"), -1, out error), label + "performs: " + error);
                yield return new WaitForSeconds(0.4f);
                c.Check(Performance.Mode == PerformanceMode.MiniGame && player.InEmote(), label + "performing, seated");
                x.Controller(true);
                Press("Block");
                Performance.TestRightClick = true;
                yield return new WaitForSeconds(0.4f);
                c.Check(Performance.Mode == PerformanceMode.None && player.InEmote() && !player.m_blocking && !player.IsBlocking(),
                    label + "right click stops the rhythm game: seated, not blocking");
                Let("Block");
                x.Controller(false);
                rig.TakeControls();
                player.StopEmote();
                yield return new WaitForSeconds(0.8f);
            }
            player.m_toggleBlock = toggleBlock;
            player.m_blocking = false;

            // Lyre: sit down mid-song = onto the left thigh; stand up (walk) = back against the body.
            player.UnequipItem(player.GetRightItem(), false);
            yield return Frames(2);
            rig.Hold(InstrumentKind.Lyre);
            yield return new WaitForSeconds(0.5f);
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var lyreError), "lyre plays: " + lyreError);
            yield return new WaitForSeconds(1.2f);
            yield return new WaitForEndOfFrame();
            var seatStanding = InstrumentPose.Seat(player);
            var standingOk = InstrumentPose.TryGetMarker(player, InstrumentModels.BodyMarker, out var standing);
            var standingGap = ThighGap(player, standing);
            c.Check(seatStanding >= 0f && seatStanding < 0.05f && InstrumentPose.InstanceMoved(player), $"standing: against the body (seat {F(seatStanding)})");
            c.Check(player.StartEmote("sit", false), "sit down mid-song");
            yield return WaitFor(() => player.IsSitting(), 4f);
            yield return new WaitForSeconds(1.2f);
            yield return new WaitForEndOfFrame();
            var seatedOk = InstrumentPose.TryGetMarker(player, InstrumentModels.BodyMarker, out var seated);
            var seatedGap = ThighGap(player, seated);
            c.Note($"lyre body from the middle of the left thigh: standing {F(standingGap)} m, seated {F(seatedGap)} m; " + InstrumentPose.Describe(player));
            c.Check(Performance.Mode == PerformanceMode.Auto && player.IsSitting(), "still playing, seated");
            c.Check(InstrumentPose.Seat(player) > 0.95f, $"seated pose fully in ({F(InstrumentPose.Seat(player))})");
            c.Check(standingOk && seatedOk && seatedGap >= 0f && seatedGap < 0.4f && seatedGap < standingGap - 0.05f, "the lyre moved onto the left thigh");
            var gap = InstrumentPose.LeftHandGap(player);
            c.Check(gap >= 0f && gap < 0.08f, $"the left hand is still on it ({F(gap)} m)");
            // Walk: the game stands the player up, the song goes on, the lyre comes back up.
            var moved = new float[2];
            yield return Push(rig, player, 0.4f, moved);
            yield return new WaitForSeconds(1.2f);
            c.Check(!player.IsSitting() && Performance.Mode == PerformanceMode.Auto, "walking stands up, the song goes on");
            c.Check(InstrumentPose.Seat(player) < 0.05f, $"the lyre is back against the body (seat {F(InstrumentPose.Seat(player))})");
            c.Report();
        }
        finally
        {
            player.m_toggleBlock = toggleBlock;
            player.m_blocking = false;
            x.Restore();
            rig.Restore();
        }
    }

    // Metres from a point to the middle of the player's left thigh (-1 = no such bones).
    private static float ThighGap(Player player, Vector3 point)
    {
        var animator = player.m_animator;
        if (animator == null || !animator.isHuman)
        {
            return -1f;
        }
        var hip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        var knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        if (hip == null || knee == null)
        {
            return -1f;
        }
        return (point - (hip.position + knee.position) * 0.5f).magnitude;
    }

    // ---------- music.stops (T13) ----------

    private static HitData Hit(Player player, HitData.HitType type, float blunt = 0f, float fire = 0f, float poison = 0f, float plain = 0f)
    {
        var hit = new HitData
        {
            m_hitType = type,
            m_point = player.GetCenterPoint(),
            m_dir = player.transform.forward,
            m_blockable = false,
            m_dodgeable = false,
            m_pushForce = 0f,
            m_staggerMultiplier = 0f,
        };
        hit.m_damage.m_blunt = blunt;
        hit.m_damage.m_fire = fire;
        hit.m_damage.m_poison = poison;
        hit.m_damage.m_damage = plain;
        return hit;
    }

    private static IEnumerator RunStops()
    {
        var c = new Checks(StopsName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(StopsName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        var startHealth = player.GetHealth();
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            var song = Preset("drunken-sailor");
            var knife = rig.Give(InstrumentContent.VanillaKnifeName);
            var flute = rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            var seman = player.GetSEMan();

            // (a) a real hit. Full health first: the test world runs in god mode, where the game never lets health go
            // under 1 (Character.ApplyDamage), so a player an earlier test left at 1 could not be seen to be hurt.
            player.SetHealth(player.GetMaxHealth());
            c.Check(Performance.StartAuto(song, -1, out var error), "plays: " + error);
            yield return new WaitForSeconds(0.5f);
            Seen.Clear();
            var health = player.GetHealth();
            player.Damage(Hit(player, HitData.HitType.EnemyHit, blunt: 8f));
            yield return Frames(3);
            c.Check(health > 1.5f && player.GetHealth() < health, $"the hit hurt ({F(health)} -> {F(player.GetHealth())})");
            c.Check(Performance.Mode == PerformanceMode.None, "a hit stops the song");
            c.Check(Seen.Count(MessageHud.MessageType.TopLeft, "The hit cut your song short.") == 1, "message: " + Seen.Tail());
            player.SetHealth(health);

            // (b) fire and the other damage over time: it keeps playing.
            yield return new WaitForSeconds(1.6f); // the stop message is said at most every 1.5 s
            c.Check(Performance.StartAuto(song, -1, out error), "plays again: " + error);
            yield return new WaitForSeconds(0.3f);
            player.Damage(Hit(player, HitData.HitType.Undefined, fire: 12f)); // standing in a fire: fire damage only
            yield return Frames(3);
            var burning = seman.HaveStatusEffect(SEMan.s_statusEffectBurning);
            c.Check(burning, "the fire sets the player burning");
            var before = player.GetHealth();
            yield return new WaitForSeconds(2.4f);
            c.Check(!burning || player.GetHealth() < before || player.GetHealth() <= 1.01f, "burning hurts over time");
            c.Check(Performance.Mode == PerformanceMode.Auto, "burning does not stop the song");
            seman.RemoveStatusEffect(SEMan.s_statusEffectBurning, true);
            // The ticks the game's own effects deal (same call, same hit types).
            player.ApplyDamage(Hit(player, HitData.HitType.Burning, fire: 3f), true, false);
            player.ApplyDamage(Hit(player, HitData.HitType.Poisoned, poison: 3f), true, false);
            player.ApplyDamage(Hit(player, HitData.HitType.Freezing, plain: 3f), true, false);
            player.ApplyDamage(Hit(player, HitData.HitType.Smoke, plain: 3f), true, false);
            player.ApplyDamage(Hit(player, HitData.HitType.Drowning, plain: 3f), true, false);
            player.ApplyDamage(Hit(player, HitData.HitType.Water, plain: 3f), true, false);
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto, "fire, poison, frost, smoke, water and drowning ticks do not stop it");
            c.Check(Seen.Count(MessageHud.MessageType.TopLeft, "The hit cut your song short.") == 1, "no second message");
            player.SetHealth(health);

            // (c) R: stops at once.
            c.Note($"player takes input: {player.TakeInput()}");
            Press("Hide");
            yield return WaitFor(() => player.GetRightItem() == null, 1f);
            yield return Frames(2);
            Let("Hide");
            c.Check(player.m_hiddenRightItem == flute, "R puts the flute away");
            c.Check(Performance.Mode == PerformanceMode.None && PlayingFlag(player) == 0, "and the song stops at once");
            yield return Tap("Hide");
            yield return Frames(3);
            c.Check(player.GetRightItem() == flute, "flute back in hand");

            // (d) a weapon from the hotbar.
            c.Check(Performance.StartAuto(song, -1, out error), "plays a third time: " + error);
            yield return new WaitForSeconds(0.3f);
            var hotbar = ToHotbar(player, knife);
            c.Check(hotbar != null && !KeyCapture.Active, "the knife is on a hotbar key, and the keys are free while a song plays by itself");
            if (hotbar != null)
            {
                yield return Tap(hotbar);
            }
            yield return WaitFor(() => player.GetRightItem() == knife, 4f);
            var playingAtSwap = Performance.Mode;
            yield return Frames(2);
            c.Check(player.GetRightItem() == knife, "the hotbar key equips the knife");
            c.Check(Performance.Mode == PerformanceMode.None, $"and the song stops at once (mode {playingAtSwap} in the same frame)");
            player.UnequipItem(knife, false);
            player.EquipItem(flute, false);
            yield return new WaitForSeconds(0.4f);

            // (e) swimming (the game's own swim state, as in deep water).
            c.Check(Performance.StartAuto(song, -1, out error), "plays a fourth time: " + error);
            yield return new WaitForSeconds(0.3f);
            player.m_swimTimer = 0f;
            c.Check(player.IsSwimming(), "the player swims");
            yield return Frames(2);
            c.Check(Performance.Mode == PerformanceMode.None, "swimming stops the song at once");
            player.m_swimTimer = 999f;
            yield return Frames(2);

            // While performing: map, inventory and chat keys do nothing; Esc stops.
            c.Check(Performance.StartMiniGame(song, -1, out error), "performs: " + error);
            yield return new WaitForSeconds(0.4f);
            var map = Minimap.instance != null ? Minimap.instance.m_mode : Minimap.MapMode.None;
            c.Check(KeyCapture.Active && Chat.instance.HasFocus(), "performing: the game's keys are held back");
            if (ChatTyping)
            {
                // Chat left open by something before this test: shut it, so the check below is about our press.
                CloseChat();
                yield return Frames(3);
            }
            var typing = false;
            yield return Tap("Map");
            yield return Tap("Inventory");
            Press("Chat");
            for (var i = 0; i < 8; i++)
            {
                if (i == 3)
                {
                    Let("Chat");
                }
                typing |= ChatTyping;
                yield return null;
            }
            c.Check(Minimap.instance == null || Minimap.instance.m_mode == map, "the map key does not open the map");
            c.Check(!InventoryGui.IsVisible(), "the inventory key does not open the inventory");
            c.Check(!typing, "the chat key does not open the chat");
            c.Check(Performance.Mode == PerformanceMode.MiniGame, "still performing");
            yield return Tap("JoyMenu");
            yield return WaitReal(() => MenuShown, 1f);
            yield return Frames(3);
            c.Check(MenuShown && Performance.Mode == PerformanceMode.None, "Esc opens the menu and stops the rhythm game");
            if (MenuShown)
            {
                Menu.instance.Hide();
            }
            yield return Real(0.3f);

            // The same presses with no rhythm game do open the map and the chat: the presses me fake are ones the game
            // obeys, so "nothing opened" above was the mod's doing (the inventory key is tried this way in music.rhythm).
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            yield return Frames(3);
            if (Minimap.instance != null)
            {
                yield return Tap("Map");
                yield return Frames(2);
                c.Check(Minimap.instance.m_mode != map, $"without the rhythm game the same press changes the map ({map} -> {Minimap.instance.m_mode})");
                Minimap.instance.SetMapMode(map);
                yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
                yield return Frames(3);
            }
            yield return Tap("Chat");
            yield return Frames(2);
            c.Check(ChatTyping, "without the rhythm game the same press opens the chat");
            CloseChat();
            yield return Frames(3);
            c.Check(!ChatTyping, "chat shut again");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
            // Health as the test found it (me filled it for the hit).
            try
            {
                if (!player.IsDead() && startHealth > 0f)
                {
                    player.SetHealth(startHealth);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (health) failed: {e.Message}");
            }
        }
    }

    // ---------- music.click (T14) ----------

    private static IEnumerator RunClick()
    {
        var c = new Checks(ClickName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(ClickName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        var toggleBlock = player.m_toggleBlock;
        try
        {
            rig.TakeControls();
            x.RealStamina();
            x.NoShare();
            player.m_toggleBlock = false;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.6f);
            var stamina = player.GetStamina();
            // Attack held for half a second: the window, never a punch.
            var punched = false;
            for (var i = 0; i < 30; i++)
            {
                player.SetControls(Vector3.zero, i == 0, true, false, false, false, false, false, false, false, false);
                punched |= player.InAttack();
                yield return null;
            }
            rig.TakeControls();
            c.Check(SongWindow.IsOpen, "left click opens the window");
            c.Check(!punched && player.GetStamina() >= stamina - 0.01f, $"and never punches (stamina {F(stamina)} -> {F(player.GetStamina())})");
            Performance.CloseWindow();
            yield return Frames(3);
            // Block: with the fists, as with the hammer.
            var blocked = false;
            for (var i = 0; i < 30; i++)
            {
                player.SetControls(Vector3.zero, false, false, false, false, i == 0, true, false, false, false, false);
                blocked |= player.IsBlocking();
                yield return null;
            }
            c.Check(blocked && !SongWindow.IsOpen, "Block blocks (with the fists, as with the hammer)");
            rig.TakeControls();
            yield return new WaitForSeconds(0.3f);
            c.Check(!player.IsBlocking(), "block let go");

            // The mod really off (framework: patches removed): the click does nothing at all.
            RealOff();
            yield return Frames(2);
            c.Check(!ModActive && !Plugin.FeatureActive, "mod turned off");
            stamina = player.GetStamina();
            punched = false;
            for (var i = 0; i < 40; i++)
            {
                player.SetControls(Vector3.zero, i == 0 || i == 20, true, false, false, false, false, false, false, false, false);
                punched |= player.InAttack();
                yield return null;
            }
            rig.TakeControls();
            c.Check(!punched && !SongWindow.IsOpen && !Performance.WindowOpen, "mod off: left click does nothing (no punch, no window)");
            c.Check(player.GetStamina() >= stamina - 0.01f, $"mod off: no stamina used ({F(stamina)} -> {F(player.GetStamina())})");
            c.Check(InstrumentContent.KindOf(player.GetRightItem()) == InstrumentKind.Flute, "the flute stays in the hand");
            RealOn();
            yield return Frames(2);
            c.Check(ModActive && Plugin.FeatureActive, "mod back on");
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen, "back on: the click opens the window again");
            c.Report();
        }
        finally
        {
            player.m_toggleBlock = toggleBlock;
            x.Restore();
            rig.Restore();
        }
    }
}
#endif
