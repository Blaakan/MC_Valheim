#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.FishingFightMod;

// Debug build only. Staged self tests (same class as SelfTests.cs): through a REAL log out to the main menu and load
// of the same world (SelfTest.RegisterRelog), and in a game REALLY started with the mod off (RegisterDisabledStart).
// One small test each: the staged tests are new, each can be fixed or dropped alone.
//   fishing.relog.calm     T23: fish hooked, calm phase, log out: Debug line of the fight's end, at the main menu no
//                          fight and no bar object left; world loaded again: nothing on screen for 2 s, the Perch that
//                          was on the hook is free (hooked flag off); next fish hooked = bar back, in the new HUD
//   fishing.relog.fight    T23: the same from a fight with the arrow on (no arrow left; arrow back in the next fight)
//   fishing.disabled.reel  T21: game started with Enabled = false: state Disabled, no patch, a hooked Perch is reeled
//                          the normal game's way (no bar, line holds without Block, Block reels and costs stamina,
//                          caught)
// The relog tests do not use the rig of the live tests (its Restore would end the fight before the log out): rod
// marked with a crafter name (found again after the load, the character is saved with it), float and Perch on dry
// land 7 m ahead, rules only in memory. The "after" part takes everything away again. Player never moved.
internal static partial class SelfTests
{
    private const string RelogCalmName = "fishing.relog.calm";
    private const string RelogFightName = "fishing.relog.fight";
    private const string DisabledReelName = "fishing.disabled.reel";

    // Crafter name on the rod a relog test leaves in the inventory over the log out: found again by nothing else.
    private const string TestCrafter = "MC fishing test";
    private const string RodPrefabName = "FishingRod";
    private const string FloatPrefabName = "FishingRodFloat";
    private const string FishPrefabName = "Fish1";
    private const string BaitPrefabName = "FishingBait";

    private static void RegisterStaged()
    {
        SelfTest.RegisterRelog(RelogCalmName, () => RelogBefore(RelogCalmName, false), () => RelogAtMenu(RelogCalmName, false),
            () => RelogAfter(RelogCalmName, false));
        SelfTest.RegisterRelog(RelogFightName, () => RelogBefore(RelogFightName, true), () => RelogAtMenu(RelogFightName, true),
            () => RelogAfter(RelogFightName, true));
    }

    private static void UnregisterStaged()
    {
        SelfTest.UnregisterRelog(RelogCalmName);
        SelfTest.UnregisterRelog(RelogFightName);
    }

    // Plugin.BindConfig (Debug build): in the second game of a run the mod is off from the start, OnActivated never
    // runs there. Never unregistered. Also the log listener of fishing.log-clean (it must hear the whole game).
    private static void RegisterAlwaysBody()
    {
        StartBootTap();
        SelfTest.RegisterDisabledStart(DisabledReelName, null, RunDisabledReel);
    }

    // ---------- relog: what lives on over the log out (statics; the world's objects are gone) ----------

    private static LogTap _relogTap;
    private static int _relogMark;
    // Error lines BootTap had when the test began (the tap above only starts at the end of the first part).
    private static int _relogBootErrors;
    private static bool _relogArmed;
    private static bool _relogRecorded;
    private static bool _relogShown;
    private static Vector3 _relogSpot;
    private static Vector3 _relogDir;
    private static bool _relogHadSkill;
    private static float _relogSkillLevel;
    private static float _relogSkillPoints;
    private static bool _relogHadRight;
    private static Vector2i _relogRight;
    private static bool _relogHadLeft;
    private static Vector2i _relogLeft;

    private static ItemDrop.ItemData TestRod(Player p)
    {
        foreach (var item in p.GetInventory().GetAllItems())
        {
            if (item != null && item.m_crafterName == TestCrafter && item.m_dropPrefab != null && item.m_dropPrefab.name == RodPrefabName)
            {
                return item;
            }
        }
        return null;
    }

    // Float prefab like the live rig finds it: by name, else from the rod's projectile.
    private static GameObject FindFloatPrefab(ItemDrop.ItemData rod)
    {
        var prefab = ZNetScene.instance.GetPrefab(FloatPrefabName);
        if (prefab == null && rod != null)
        {
            var attack = rod.m_shared.m_attack;
            var projectile = attack != null && attack.m_attackProjectile != null ? attack.m_attackProjectile.GetComponent<Projectile>() : null;
            prefab = projectile != null ? projectile.m_spawnOnHit : null;
        }
        return prefab;
    }

    private sealed class Catch
    {
        internal FishingFloat Float;
        internal Fish Fish;
        internal Fight Fight;
        internal string Detail = "";
    }

    // Float of this player (the game's own Setup) and a Perch on the dry ground at the spot, hooked with the
    // float's own SetCatch. No rig: the caller owns the clean-up.
    private static IEnumerator HookAt(Player p, ItemDrop.ItemData rod, Vector3 spot, Vector3 dir, Catch made)
    {
        var floatPrefab = FindFloatPrefab(rod);
        var fishPrefab = ZNetScene.instance.GetPrefab(FishPrefabName);
        var baitPrefab = ObjectDB.instance.GetItemPrefab(BaitPrefabName);
        var baitDrop = baitPrefab != null ? baitPrefab.GetComponent<ItemDrop>() : null;
        if (floatPrefab == null || fishPrefab == null || baitDrop == null)
        {
            made.Detail = "float, Fish1 or FishingBait prefab not found";
            yield break;
        }
        Transform rodTop = null;
        for (var i = 0; i < 100 && rodTop == null; i++)
        {
            yield return null;
            rodTop = Utils.FindChild(p.transform, "_RodTop");
        }
        if (rodTop == null)
        {
            made.Detail = "the rod's top (_RodTop) does not show";
            yield break;
        }
        var bait = baitDrop.m_itemData.Clone();
        bait.m_dropPrefab = baitPrefab;
        var ground = ZoneSystem.instance.GetGroundHeight(spot);
        var side = Vector3.Cross(Vector3.up, dir);
        var floatGo = UnityEngine.Object.Instantiate(floatPrefab, new Vector3(spot.x, ground + 0.6f, spot.z), Quaternion.identity);
        made.Float = floatGo.GetComponent<FishingFloat>();
        made.Float.Setup(p, Vector3.zero, 0f, null, rod, bait);
        var fishGo = UnityEngine.Object.Instantiate(fishPrefab, new Vector3(spot.x + side.x * 0.6f, ground + 0.4f, spot.z + side.z * 0.6f),
            Quaternion.identity);
        made.Fish = fishGo.GetComponent<Fish>();
        for (var i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        if (made.Float == null || made.Fish == null)
        {
            made.Detail = "the float or the Perch did not stay in the world";
            yield break;
        }
        made.Float.SetCatch(made.Fish);
        for (var i = 0; i < 5 && (Fight.Current == null || !ReferenceEquals(Fight.Current.Fish, made.Fish)); i++)
        {
            yield return new WaitForFixedUpdate();
        }
        var fight = Fight.Current;
        if (fight != null && ReferenceEquals(fight.Fish, made.Fish) && ReferenceEquals(fight.Float, made.Float))
        {
            made.Fight = fight;
        }
        else
        {
            made.Detail = "no fight started on the hooked Perch";
        }
    }

    // Perch and floats near the relog spot (what "before" left and the save brought back, and what "after" made).
    private static void SweepSpot(List<string> said)
    {
        var fishes = new List<GameObject>();
        foreach (var updater in Fish.Instances)
        {
            if (updater is Fish fish && fish != null && Utils.GetPrefabName(fish.gameObject) == FishPrefabName
                && Vector3.Distance(Flat(fish.transform.position), Flat(_relogSpot)) < 12f)
            {
                fishes.Add(fish.gameObject);
            }
        }
        var floats = new List<GameObject>();
        foreach (var ff in FishingFloat.GetAllInstances())
        {
            if (ff != null && Vector3.Distance(Flat(ff.transform.position), Flat(_relogSpot)) < 40f)
            {
                floats.Add(ff.gameObject);
            }
        }
        foreach (var go in fishes)
        {
            Kill(go);
        }
        foreach (var go in floats)
        {
            Kill(go);
        }
        if (said != null)
        {
            said.Add($"{fishes.Count} Perch and {floats.Count} float(s) taken out of the world");
        }
    }

    // Everything a relog test changed, back (no yield: also the finally of "after").
    private static void RelogCleanUp(Player p, string name)
    {
        try
        {
            ClearOverrides();
            Fight.Shutdown();
            if (_relogTap != null)
            {
                _relogTap.Stop();
                _relogTap = null;
            }
            _relogArmed = false;
            // First part never came to the point where it wrote down what to put back: nothing of the player or the
            // world was touched, and the notes are not this run's.
            if (p == null || !_relogRecorded)
            {
                return;
            }
            _relogRecorded = false;
            SweepSpot(null);
            var inv = p.GetInventory();
            for (var rod = TestRod(p); rod != null; rod = TestRod(p))
            {
                if (p.IsItemEquiped(rod) || ReferenceEquals(p.m_hiddenRightItem, rod) || ReferenceEquals(p.m_hiddenLeftItem, rod))
                {
                    p.UnequipItem(rod, false);
                }
                if (!inv.RemoveItem(rod))
                {
                    break;
                }
            }
            if (_relogHadRight)
            {
                var item = inv.GetItemAt(_relogRight.x, _relogRight.y);
                if (item != null && !p.IsItemEquiped(item))
                {
                    p.EquipItem(item, false);
                }
            }
            if (_relogHadLeft)
            {
                var item = inv.GetItemAt(_relogLeft.x, _relogLeft.y);
                if (item != null && !p.IsItemEquiped(item))
                {
                    p.EquipItem(item, false);
                }
            }
            var skills = p.m_skills;
            if (_relogHadSkill)
            {
                var skill = skills.GetSkill(Skills.SkillType.Fishing);
                skill.m_level = _relogSkillLevel;
                skill.m_accumulator = _relogSkillPoints;
            }
            else
            {
                skills.m_skillData.Remove(Skills.SkillType.Fishing);
            }
        }
        catch (Exception e)
        {
            SelfTest.Note(name, "clean-up after the relog failed: " + e.Message);
        }
    }

    // ---------- fishing.relog.calm / fishing.relog.fight (T23) ----------

    // In the world, mod on: rod in hand, fish hooked, the fight on (calm with the bar, or a fight with the arrow).
    // Nothing is cleaned here when it ran to its end: the probe logs out right after, with the fight still going.
    // Throw or probe time-out: the probe ends the test there and no "after" part comes, so me clean up here.
    private static IEnumerator RelogBefore(string name, bool fighting)
    {
        var ended = false;
        try
        {
            yield return RelogBeforeBody(name, fighting);
            ended = true;
        }
        finally
        {
            if (!ended)
            {
                RelogCleanUp(Player.m_localPlayer, name);
            }
        }
    }

    private static IEnumerator RelogBeforeBody(string name, bool fighting)
    {
        var c = new Checks(name);
        _relogArmed = false;
        _relogShown = false;
        _relogBootErrors = _boot != null ? _boot.Count : 0;
        var p = Player.m_localPlayer;
        var view = FeatureRegistry.Find(ModInfo.Guid);
        if (!c.Check(p != null && ZNetScene.instance != null && ObjectDB.instance != null && ZoneSystem.instance != null
                     && view != null && view.Value.IsActive, "in the world, the mod active"))
        {
            c.Report();
            yield break;
        }
        // What the "after" part puts back.
        var skills = p.m_skills;
        _relogHadSkill = skills.m_skillData.TryGetValue(Skills.SkillType.Fishing, out var old);
        _relogSkillLevel = _relogHadSkill ? old.m_level : 0f;
        _relogSkillPoints = _relogHadSkill ? old.m_accumulator : 0f;
        _relogHadRight = p.m_rightItem != null;
        _relogRight = _relogHadRight ? p.m_rightItem.m_gridPos : Vector2i.zero;
        _relogHadLeft = p.m_leftItem != null;
        _relogLeft = _relogHadLeft ? p.m_leftItem.m_gridPos : Vector2i.zero;
        var dir = Flat(p.transform.forward);
        if (dir.sqrMagnitude < 1e-4f)
        {
            dir = Vector3.forward;
        }
        dir.Normalize();
        _relogDir = dir;
        _relogSpot = p.transform.position + dir * 7f;
        _relogRecorded = true;

        var rod = p.GetInventory().AddItem(RodPrefabName, 1, 1, 0, 0L, TestCrafter, false);
        if (!c.Check(rod != null && p.EquipItem(rod, false), "a fishing rod given and taken in hand"))
        {
            c.Report();
            yield break;
        }
        // Rules of the test in memory only (never config): calm with the fish off the zone for free, or a fight to
        // the right with the arrow on. Phase pinned: on dry land the fish's own timer would never start a fight.
        ServerRules.TestRules = new FightRules { OffBarStamina = 0f, LineRunSpeed = 0f };
        Fight.TestPin = FishPin.Away;
        Fight.TestSide = FightLogic.Right;
        Fight.TestPhase = fighting ? FightPhase.Struggle : FightPhase.Calm;
        FightHud.TestDisplay = DefaultDisplay;
        FightHud.TestArrow = fighting ? true : (bool?)null;
        var made = new Catch();
        yield return HookAt(p, rod, _relogSpot, dir, made);
        if (!c.Check(made.Fight != null, "a Perch is hooked and the fight is on" + (made.Detail.Length > 0 ? " (" + made.Detail + ")" : "")))
        {
            c.Report();
            yield break;
        }
        yield return new WaitForSeconds(fighting ? 0.6f : 0.3f);
        yield return null;
        yield return null;
        var fight = made.Fight;
        var zdo = made.Fish != null && made.Fish.m_nview != null && made.Fish.m_nview.IsValid() ? made.Fish.m_nview.GetZDO() : null;
        _relogShown = fighting
            ? FightHud.ArrowShown && FightHud.TestArrowOnScreen && !FightHud.BarShown
            : FightHud.BarShown && FightHud.TestBarOnScreen && !FightHud.ArrowShown;
        c.Check(ReferenceEquals(Fight.Current, fight) && fight.Phase == (fighting ? FightPhase.Struggle : FightPhase.Calm) && _relogShown,
            fighting
                ? "before the log out: the fish fights, the arrow is on screen (arrow setting on), no bar"
                : "before the log out: calm phase, the catch bar is on screen");
        c.Check(zdo != null && zdo.GetInt(ZDOVars.s_hooked) == 1 && (!fighting || zdo.GetFloat(ZDOVars.s_escape) > 0f),
            fighting ? "the fish carries the hooked flag and a running escape time (what the world save keeps)" : "the fish carries the hooked flag");
        // From here to the end of "after": every line of the mod, and its errors.
        _relogTap = new LogTap(null);
        _relogTap.Start();
        _relogMark = _relogTap.Mark();
        _relogArmed = true;
        c.Report();
    }

    // At the main menu, the world gone.
    private static IEnumerator RelogAtMenu(string name, bool fighting)
    {
        var c = new Checks(name);
        yield return null;
        c.Check(FejdStartup.instance != null && Player.m_localPlayer == null, "logged out: at the main menu");
        if (c.Check(_relogArmed && _relogTap != null, "the fight was on when the log out began (the first part of the test)"))
        {
            c.Check(Fight.Current == null, "at the main menu no fight lives on");
            // The HUD went with the world: what was drawn is gone with it.
            c.Check(FightHud.TestRoot == null && !FightHud.TestBarOnScreen && !FightHud.TestArrowOnScreen,
                "at the main menu nothing of the catch bar or the arrow is left");
            var off = _relogTap.Has("Fight ended: the mod was turned off.", _relogMark);
            var gone = _relogTap.Has("Fight ended: its float or fish is gone.", _relogMark);
            c.Check(off || gone,
                "Debug line at the log out: \"Fight ended: the mod was turned off.\" or \"Fight ended: its float or fish is gone.\""
                + (off ? " (the first)" : gone ? " (the second)" : ""));
            c.Check(_relogTap.Count("Fight started:", _relogMark) == 0, "no new fight started during the log out");
            var view = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(view != null && view.Value.IsActive, "the mod is still active at the main menu");
        }
        // The pins of the first part must not reach the next world. The arrow setting stays on in the fight test
        // (the item: ShowStruggleArrow = true): the watch after the load must see no arrow with the setting on.
        ClearOverrides();
        FightHud.TestArrow = fighting && _relogArmed ? true : (bool?)null;
        c.Report();
    }

    // Same world loaded again (the fish and the rod came back with the save).
    private static IEnumerator RelogAfter(string name, bool fighting)
    {
        var c = new Checks(name);
        var p = Player.m_localPlayer;
        try
        {
            yield return RelogAfterBody(c, p, fighting);
        }
        finally
        {
            // Probe time-out or throw: this still runs (no yield here).
            RelogCleanUp(p, name);
        }
        c.Report();
    }

    private static IEnumerator RelogAfterBody(Checks c, Player p, bool fighting)
    {
        {
            if (!c.Check(p != null && ZNetScene.instance != null && Hud.instance != null, "the world is loaded again"))
            {
                yield break;
            }
            if (!c.Check(_relogArmed && _relogTap != null, "the fight was on when the log out began (the first part of the test)"))
            {
                yield break;
            }
            // Nothing on screen before the next fish is hooked: now, and for 2 s more.
            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }
            var clean = Fight.Current == null && !FightHud.Shown && !FightHud.BarShown && !FightHud.ArrowShown
                        && !FightHud.TestBarOnScreen && !FightHud.TestArrowOnScreen;
            var asked = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - asked < 2f)
            {
                yield return null;
                clean &= Fight.Current == null && !FightHud.Shown && !FightHud.BarShown && !FightHud.ArrowShown
                         && !FightHud.TestBarOnScreen && !FightHud.TestArrowOnScreen;
            }
            c.Check(clean, "world loaded again: no fight, no catch bar and no arrow on screen before a fish is hooked (watched 2 s)");
            c.Check(_relogTap.Count("Fight started:", _relogMark) == 0, "no fight started by itself since the log out (Debug lines)");

            // The Perch that was on the hook at the log out: back with the world save, and free.
            Fish left = null;
            foreach (var updater in Fish.Instances)
            {
                if (updater is Fish fish && fish != null && Utils.GetPrefabName(fish.gameObject) == FishPrefabName
                    && Vector3.Distance(Flat(fish.transform.position), Flat(_relogSpot)) < 12f)
                {
                    left = fish;
                    break;
                }
            }
            if (left != null && left.m_nview != null && left.m_nview.IsValid())
            {
                var zdo = left.m_nview.GetZDO();
                asked = Time.time;
                while (left != null && Time.time - asked < 3f
                       && !(zdo.GetInt(ZDOVars.s_hooked) == 0 && Near(zdo.GetFloat(ZDOVars.s_escape), 0f)))
                {
                    yield return new WaitForFixedUpdate();
                }
                c.Check(left != null && !left.IsHooked() && zdo.GetInt(ZDOVars.s_hooked) == 0 && Near(zdo.GetFloat(ZDOVars.s_escape), 0f),
                    $"the Perch that was on the hook at the log out came back with the world and is free: hooked flag and escape time off, so it does not splash (run by this game {left != null && left.m_nview.IsOwner()})");
            }
            else
            {
                c.Note("the Perch that was on the hook at the log out is not in the loaded world (not saved): nothing to free");
            }
            var floats = 0;
            foreach (var ff in FishingFloat.GetAllInstances())
            {
                floats += ff != null && Vector3.Distance(Flat(ff.transform.position), Flat(_relogSpot)) < 40f ? 1 : 0;
            }
            var said = new List<string>();
            SweepSpot(said);
            c.Note($"floats near the spot after the load: {floats}; before the next hook: {(said.Count > 0 ? said[0] : "nothing swept")}");
            yield return null;

            // The next fish hooked: the bar is back, built in the new HUD (and the arrow in a fight).
            var rod = TestRod(p);
            if (!c.Check(rod != null, "the rod of the first part is still in the inventory (the character was saved with it)"))
            {
                yield break;
            }
            if (!p.IsItemEquiped(rod))
            {
                p.EquipItem(rod, false);
            }
            ServerRules.TestRules = new FightRules { OffBarStamina = 0f, LineRunSpeed = 0f };
            Fight.TestPin = FishPin.Away;
            Fight.TestSide = FightLogic.Right;
            Fight.TestPhase = FightPhase.Calm;
            FightHud.TestDisplay = DefaultDisplay;
            FightHud.TestArrow = fighting ? true : (bool?)null;
            var made = new Catch();
            yield return HookAt(p, rod, _relogSpot, _relogDir, made);
            yield return null;
            yield return null;
            var hudRoot = Hud.instance != null ? Hud.instance.m_rootObject : null;
            if (c.Check(made.Fight != null && FightHud.BarShown && FightHud.TestBarOnScreen && hudRoot != null && FightHud.TestRoot != null
                        && FightHud.TestRoot.IsChildOf(hudRoot.transform),
                    "the next fish hooked: the catch bar is back, drawn inside the HUD of the loaded world"
                    + (made.Detail.Length > 0 ? " (" + made.Detail + ")" : "")) && fighting)
            {
                Fight.TestPhase = FightPhase.Struggle;
                yield return new WaitForSeconds(0.4f);
                yield return null;
                yield return null;
                c.Check(ReferenceEquals(Fight.Current, made.Fight) && made.Fight.Phase == FightPhase.Struggle && FightHud.ArrowShown
                        && FightHud.TestArrowOnScreen && !FightHud.BarShown,
                    "and in its fight the arrow shows again (arrow setting on)");
            }
            var errors = _relogTap.Errors();
            c.Check(errors.Count == 0,
                $"no error logged by {ModInfo.Name} from the log out to here ({errors.Count}{(errors.Count > 0 ? ", first: " + errors[0] : "")})");
            // The whole test, its first part too (the tap above started at that part's end), and errors of other
            // log sources that name the mod's code.
            var since = _boot != null ? _boot.Count - _relogBootErrors : -1;
            c.Check(since == 0,
                since < 0
                    ? "the log was watched from the start of the game"
                    : $"no error line of {ModInfo.Name} and none naming its code since this test began ({since})");
        }
    }

    // ---------- fishing.disabled.reel (T21) ----------

    private static IEnumerator RunDisabledReel() => RunLive(DisabledReelName, false, DisabledReelBody);

    // Second game of the run: started with Enabled = false. Fishing is the normal game's from the start.
    private static IEnumerator DisabledReelBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        var view = FeatureRegistry.Find(ModInfo.Guid);
        c.Check(SelfTest.Stage == SelfTest.DisabledStage, $"this is the game started with every MC mod turned off (stage '{SelfTest.Stage}')");
        if (!c.Check(view != null && view.Value.IsDisabledByUser && !view.Value.IsActive,
                $"the mod is off from the start ({(view != null ? view.Value.State + ": " + view.Value.Status : "not registered")})"))
        {
            yield break;
        }
        c.Check(!OwnPatchOn(typeof(FishingFloat), nameof(FishingFloat.FixedUpdate)) && !OwnPatchOn(typeof(Fish), nameof(Fish.CustomFixedUpdate))
                && !OwnPatchOn(typeof(Fish), nameof(Fish.SwimDirection)) && !OwnPatchOn(typeof(Fish), nameof(Fish.RandomizeWaypoint))
                && !OwnPatchOn(typeof(Hud), nameof(Hud.Update)),
            "none of the mod's patches is on the float, the fish or the HUD");

        // A hooked Perch: no fight, no bar; without Block the line holds, Block reels it in at a stamina cost, caught.
        var hook = new Hooked();
        yield return HookNew(rig, st, 1, hook);
        var ff = hook.Float;
        var fish = hook.Fish;
        if (!c.Check(ff != null && fish != null && ReferenceEquals(ff.GetCatch(), fish) && fish.IsHooked(), "a Perch is hooked on the player's float"))
        {
            yield break;
        }
        var caughtText = Localize("$msg_fishing_catched " + fish.GetHoverName());
        var before = rig.Inv.CountItems(st.FishName);
        var took = hook.Fight != null;
        var bar = false;
        var line0 = ff.m_lineLength;
        rig.Reel(false);
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForSeconds(0.6f);
        took |= Fight.Current != null;
        bar |= FightHud.Shown || FightHud.TestRoot != null;
        c.Check(ff != null && Near(ff.m_lineLength, line0, 0.01f), "without Block the line holds (the normal game's reel)");

        // Block: the line comes in and it costs stamina (summed tick by tick, stamina full before each tick).
        var used = 0f;
        var start = Time.time;
        rig.Reel(true);
        while (ff != null && Time.time - start < 25f)
        {
            rig.Fill(p.GetMaxStamina());
            var tick = p.GetStamina();
            yield return new WaitForFixedUpdate();
            used += Mathf.Max(0f, tick - p.GetStamina());
            took |= Fight.Current != null;
            bar |= FightHud.Shown || FightHud.TestRoot != null;
        }
        rig.Reel(false);
        yield return null;
        var reelTime = Time.time - start;
        c.Check(!took && !bar, "no fight and no catch bar at any time (nothing of the bar was even built)");
        c.Check(ff == null && rig.Inv.CountItems(st.FishName) == before + 1 && CenterText().StartsWith(caughtText, StringComparison.Ordinal),
            $"holding Block reels the fish in and it is caught as in the normal game ({F(reelTime)} s, message \"{CenterText()}\")");
        c.Check(used > 1f, $"reeling with Block costs stamina as in the normal game ({F(used)} used in {F(reelTime)} s)");
        c.Check(_boot != null && _boot.Count == 0,
            _boot != null
                ? $"no error line of {ModInfo.Name} and none naming its code since this game started ({_boot.Count}{(_boot.Count > 0 ? ", first: " + _boot.First : "")})"
                : "the log was watched from the start of this game");
    }
}
#endif
