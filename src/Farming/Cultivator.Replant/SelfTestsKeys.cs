#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Self tests that press the real buttons (ZInput button state, read by Player.Update and by my
// UpdatePlacement prefix) with the plant really under the crosshair, and the dig tests per cultivator level:
//   replant.dig      T01: hint, yellow crosshair, E dig (berries, no wood, transplant, top-left line with icon, wear,
//                    stamina), same press never cycle the snap point
//   replant.swing    T01: dig ask the tool swing animation (own small test: animator trigger read)
//   replant.tier     T09: level 3 on cloudberry: muted hint, E = centre text only (no wear, no stamina, no swing)
//   replant.sapling  T07: own sapling hint and dig back; level 4 on a Yggdrasil sapling refused
//   replant.picked   T06: picked bush dug without berries; picked forage not under the crosshair; regrown = dug
//   replant.costs    T23: no stamina = flash and nothing dug; Use moved to another key; build menu open
//   replant.full     T25: full inventory = dropped with "no room" line, picked up later
//   replant.helm     T33: ship helm: no hint, E let go of the helm and dig nothing, next E dig
//   replant.pad      T22: gamepad X in the three layouts, alt keys and LB dig nothing, build menu button still open it
//   replant.ward     M07 / M04 (one game part): permitted on a ward, sapling inside a ward, two digs of one plant
//   replant.bronze   T02: the five other level 1 plants, own transplant name and produce each
//   replant.levels   T12 / T17 / T18 / T28 dig part: level too low refused, right level dig, produce count
internal static partial class SelfTests
{
    private const string DigName = "replant.dig";
    private const string SwingName = "replant.swing";
    private const string TierName = "replant.tier";
    private const string SaplingName = "replant.sapling";
    private const string PickedName = "replant.picked";
    private const string CostsName = "replant.costs";
    private const string FullName = "replant.full";
    private const string HelmName = "replant.helm";
    private const string PadName = "replant.pad";
    private const string WardName = "replant.ward";
    private const string BronzeName = "replant.bronze";
    private const string LevelsName = "replant.levels";

    private const string SnapKey = "TabRight";
    private const string MutedOpen = "<color=#A0A0A0>";
    private const string MutedClose = "</color>";

    private static readonly string[] UseAndSnap = { Replant.KeyboardButton, SnapKey };

    private static bool WorldReady(string name)
    {
        if (Player.m_localPlayer == null || ZNetScene.instance == null || Hud.instance == null || MessageHud.instance == null
            || ZInput.instance == null || Localization.instance == null || InventoryGui.instance == null)
        {
            SelfTest.Fail(name, "no player, network list, HUD, input or inventory window");
            return false;
        }
        return true;
    }

    private static string KeyHint(string name) =>
        name + "\n[<color=yellow><b>" + Localization.instance.GetBoundKeyString(Replant.KeyboardButton) + "</b></color>] Replant";

    private static string HoverNameOf(GameObject plant, PlantKind kind)
    {
        var pick = plant != null ? plant.GetComponent<Pickable>() : null;
        return pick != null ? L(pick.GetHoverName()) : kind.DisplayName;
    }

    // Tool swing asked right now (animator trigger set, not used up yet).
    private static bool SwingAsked(Player p, ItemDrop.ItemData tool)
    {
        var attack = tool != null ? tool.m_shared.m_attack : null;
        var anim = attack != null ? attack.m_attackAnimation : null;
        return !string.IsNullOrEmpty(anim) && p.m_animator != null && p.m_animator.GetBool(anim);
    }

    // Wait until view is gone (dug) or seconds passed.
    private static IEnumerator WaitGone(ZNetView view, float seconds)
    {
        var until = Time.time + seconds;
        while (Alive(view) && Time.time < until)
        {
            yield return null;
        }
    }

    // ---------- replant.dig (T01) ----------

    private static IEnumerator RunDig()
    {
        if (!WorldReady(DigName))
        {
            yield break;
        }
        var c = new Checks(DigName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, DigName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var inv = rig.Inv;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var carried = rig.Give(rasp.ItemName, 1);
            var saplingGo = TransplantContent.SaplingPrefab(rasp);
            if (!c.Check(tool != null && carried != null && saplingGo != null, "level 1 cultivator and one raspberry transplant given"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            c.Check(player.InPlaceMode() && ReferenceEquals(player.GetRightItem(), tool), "cultivator in hand, build mode");
            // A piece with a ghost that is not a ground tool, so the vanilla snap code run while me aim at the bush.
            player.UpdateKnownRecipesList();
            player.UpdateAvailablePiecesList();
            c.Check(player.SetSelectedPiece(saplingGo.GetComponent<Piece>()), "raspberry transplant picked in the build menu");

            var aimed = new Aimed();
            yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, aimed);
            if (!c.Check(aimed.Seen, "a ripe raspberry bush is under the crosshair within reach" + aimed.Why))
            {
                c.Report();
                yield break;
            }
            var pick = aimed.Go.GetComponent<Pickable>();
            var target = Replant.Target;
            c.Check(ReferenceEquals(target.Kind, rasp) && !target.IsSapling && target.Allowed, "target = raspberry bush, allowed at level 1");
            c.Check(pick != null && pick.CanBePicked(), "bush ripe");
            var wantHint = KeyHint(HoverNameOf(aimed.Go, rasp));
            var hint = HintText();
            c.Check(hint == wantHint, $"hint '{OneLine(hint)}' (want '{OneLine(wantHint)}')");
            c.Check(CrosshairYellow(), "crosshair yellow over the bush");
            c.Note($"Use key shown as '{Localization.instance.GetBoundKeyString(Replant.KeyboardButton)}'; Use bound to "
                   + $"{ZInput.instance.GetButtonDef(Replant.KeyboardButton).GetActionPath()}, {SnapKey} to {(HasButton(SnapKey) ? ZInput.instance.GetButtonDef(SnapKey).GetActionPath() : "(no such button)")}");
            SelfTest.Screenshot(DigName, "hint");
            yield return Frames(2);

            // Control: the snap key alone (no Use) cycles the snap point of the ghost, so me can see it when it does.
            var ghost = player.m_placementGhost;
            if (c.Check(ghost != null && HasButton(SnapKey), "placement ghost and snap button there"))
            {
                for (var i = 0; i < 2; i++)
                {
                    var snap = new GameObject("MC_SelfTest_snap" + i) { tag = "snappoint" };
                    snap.transform.SetParent(ghost.transform, false);
                    snap.transform.localPosition = new Vector3(i == 0 ? 0.3f : -0.3f, 0f, 0f);
                }
            }
            player.m_manualSnapPoint = -1;
            ClearCenter();
            yield return ReadyForKey(player);
            yield return Tap(SnapKey);
            var controlSnap = player.m_manualSnapPoint;
            var controlText = CenterText();
            c.Check(controlSnap == 0 && controlText.Length > 0 && Alive(aimed.View),
                $"control: the snap key alone cycles the snap point and digs nothing (snap {controlSnap}, '{controlText}')");
            player.m_manualSnapPoint = -1;
            ClearCenter();
            yield return rig.AimAt(aimed.AimAt);
            yield return Frames(2);

            // The press: E = Use and snap key together.
            var name = rasp.ItemDisplayName;
            var before = CountByName(inv, name);
            player.m_stamina = player.GetMaxStamina();
            var stamina0 = player.m_stamina;
            var wear0 = tool.m_durability;
            var wantWear = player.GetPlaceDurability(tool) * Game.m_durabilityRate;
            var wantStamina = player.GetBuildStamina();
            var use0 = player.m_lastToolUseTime;
            var wantBerries = PickAmount(pick);
            var watch = WatchTopLeft(L("$msg_added " + name), carried.GetIcon());
            yield return ReadyForKey(player);
            yield return Tap(UseAndSnap);
            yield return WaitGone(aimed.View, 1.5f);
            c.Check(!Alive(aimed.View), "E dug the bush up");
            c.Check(CountByName(inv, name) == before + 1, $"one '{name}' added ({before} -> {CountByName(inv, name)})");
            c.Check(name == "Raspberry bush transplant", $"transplant name '{name}'");
            c.Check(Arrived(watch), $"top-left line '{watch.Text}' with the transplant icon");
            var wear = wear0 - tool.m_durability;
            c.Check(Near(wear, wantWear, 0.01f) && wantWear > 0f && wantWear <= 1.01f,
                $"cultivator wear {F(wear)} (vanilla place wear {F(wantWear)}, about 1)");
            var used = stamina0 - player.m_stamina;
            c.Check(wantStamina > 0f && Near(used, wantStamina, 0.3f), $"stamina used {F(used)} (vanilla build stamina {F(wantStamina)})");
            c.Check(player.m_lastToolUseTime > use0, "tool use time set (vanilla place and remove wait for it)");
            c.Check(player.m_manualSnapPoint == -1 && (controlText.Length == 0 || CenterText() != controlText),
                $"same press did not cycle the snap point (snap {player.m_manualSnapPoint}, centre text '{CenterText()}')");
            yield return new WaitForSeconds(1f);
            var berries = StackSum(DropsNear(aimed.Spot, SharedName("Raspberry")));
            c.Check(berries == wantBerries && wantBerries >= 1, $"raspberries dropped like a hand pick ({berries}, hand pick gives {wantBerries})");
            c.Check(DropsNear(aimed.Spot, SharedName("Wood"), 4f).Count == 0, "no wood dropped");
            c.Check(FindNear(new[] { rasp.SaplingHash }, aimed.Spot, 2f) == null, "nothing planted by the same press");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.swing (T01, swing only) ----------

    private static IEnumerator RunSwing()
    {
        if (!WorldReady(SwingName))
        {
            yield break;
        }
        var c = new Checks(SwingName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, SwingName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            yield return Hold(rig, tool);
            var spot = Vector3.zero;
            if (!c.Check(tool != null && FindPlantSpot(rig, taken, 1.2f, out spot), "cultivator and a free spot"))
            {
                c.Report();
                yield break;
            }
            var bush = rig.Spawn("RaspberryBush", spot, Quaternion.identity);
            yield return Settle();
            yield return Stand(rig, spot);
            yield return ReadyForKey(player);
            var view = bush.GetComponent<ZNetView>();
            c.Note($"cultivator swing animation '{tool.m_shared.m_attack.m_attackAnimation}'");
            c.Check(!SwingAsked(player, tool), "no swing asked before the dig");
            var ok = Replant.TryReplant(player, tool, view, rasp, false, out var refusal);
            // Same step: animator did not run yet, the trigger is still set.
            var asked = SwingAsked(player, tool);
            c.Check(ok, $"bush dug ({refusal})");
            c.Check(asked, "the dig asks the cultivator swing (animator trigger set)");
            // Animator took it: a trigger stays set until a transition into the swing uses it up.
            var inAttack = false;
            var usedUp = false;
            var until = Time.time + 1f;
            while (Time.time < until)
            {
                inAttack |= player.InAttack();
                usedUp |= !SwingAsked(player, tool);
                yield return null;
            }
            c.Check(asked && usedUp, "the animator starts the swing (trigger used up within a second)");
            c.Note($"player in an attack animation within a second: {inAttack}");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.tier (T09) ----------

    private static IEnumerator RunTier()
    {
        if (!WorldReady(TierName))
        {
            yield break;
        }
        var c = new Checks(TierName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, TierName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var cloud = PlantCatalog.ByKey("CloudberryBush");
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 3);
            if (!c.Check(tool != null, "level 3 cultivator given"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            var aimed = new Aimed();
            yield return SpawnInReach(rig, taken, "CloudberryBush", 1.2f, aimed);
            if (!c.Check(aimed.Seen, "a cloudberry bush is under the crosshair within reach" + aimed.Why))
            {
                c.Report();
                yield break;
            }
            var t = Replant.Target;
            c.Check(ReferenceEquals(t.Kind, cloud) && !t.Allowed, "target = cloudberry bush, not allowed at level 3");
            var needs = "Needs a black metal cultivator (level 4)";
            var wantHint = HoverNameOf(aimed.Go, cloud) + "\n" + MutedOpen + needs + MutedClose;
            var hint = HintText();
            c.Check(hint == wantHint, $"hint '{OneLine(hint)}' (want '{OneLine(wantHint)}')");
            c.Check(!CrosshairYellow(), "crosshair not yellow");
            SelfTest.Screenshot(TierName, "hint");
            yield return Frames(2);

            player.m_stamina = player.GetMaxStamina();
            var stamina0 = player.m_stamina;
            var wear0 = tool.m_durability;
            var use0 = player.m_lastToolUseTime;
            var swung = false;
            ClearCenter();
            yield return ReadyForKey(player);
            yield return Tap(UseAndSnap, () => swung |= SwingAsked(player, tool));
            yield return new WaitForSeconds(0.5f);
            c.Check(Alive(aimed.View), "bush still there after E");
            c.Check(CenterText() == needs, $"centre text '{CenterText()}' (want '{needs}')");
            c.Check(Near(tool.m_durability, wear0) && player.m_stamina >= stamina0 - 0.01f, "no wear, no stamina used");
            c.Check(Near(player.m_lastToolUseTime, use0) && !swung, "no tool use, no swing asked");
            c.Check(CountByName(rig.Inv, cloud.ItemDisplayName) == 0, "no transplant given");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.sapling (T07) ----------

    private static IEnumerator RunSapling()
    {
        if (!WorldReady(SaplingName))
        {
            yield break;
        }
        var c = new Checks(SaplingName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, SaplingName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var ygg = PlantCatalog.ByKey("YggaShoot");
            var tool1 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var tool4 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 4);
            if (!c.Check(tool1 != null && tool4 != null, "cultivators level 1 and 4 given"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool1);
            var aimed = new Aimed();
            yield return SpawnInReach(rig, taken, rasp.SaplingName, 1.2f, aimed);
            if (c.Check(aimed.Seen, "a young raspberry transplant is under the crosshair within reach" + aimed.Why))
            {
                var t = Replant.Target;
                c.Check(ReferenceEquals(t.Kind, rasp) && t.IsSapling && t.Allowed, "target = our raspberry sapling, allowed");
                var wantHint = KeyHint("Raspberry bush transplant");
                c.Check(HintText() == wantHint, $"hint '{OneLine(HintText())}' (want '{OneLine(wantHint)}')");
                c.Check(CrosshairYellow(), "crosshair yellow over the sapling");
                var before = CountByName(rig.Inv, rasp.ItemDisplayName);
                yield return ReadyForKey(player);
                yield return Tap(UseAndSnap);
                yield return WaitGone(aimed.View, 1.5f);
                c.Check(!Alive(aimed.View) && CountByName(rig.Inv, rasp.ItemDisplayName) == before + 1, "E dug the sapling up: transplant back");
                yield return new WaitForSeconds(0.6f);
                c.Check(DropsNear(aimed.Spot, SharedName("Raspberry")).Count == 0, "no produce from a sapling");
            }

            // Level 4 on a young Yggdrasil transplant.
            yield return Hold(rig, tool4);
            var shoot = new Aimed();
            yield return SpawnInReach(rig, taken, ygg.SaplingName, 2.3f, shoot);
            if (c.Check(shoot.Seen, "a young Yggdrasil transplant is under the crosshair within reach" + shoot.Why))
            {
                var t = Replant.Target;
                c.Check(ReferenceEquals(t.Kind, ygg) && t.IsSapling && !t.Allowed, "target = our Yggdrasil sapling, not allowed at level 4");
                var needs = "Needs an eitr cultivator (level 5)";
                var wantHint = "Yggdrasil shoot transplant\n" + MutedOpen + needs + MutedClose;
                c.Check(HintText() == wantHint, $"hint '{OneLine(HintText())}' (want '{OneLine(wantHint)}')");
                ClearCenter();
                yield return ReadyForKey(player);
                yield return Tap(UseAndSnap);
                yield return new WaitForSeconds(0.5f);
                c.Check(Alive(shoot.View) && CenterText() == needs && CountByName(rig.Inv, ygg.ItemDisplayName) == 0,
                    $"E refused: sapling stays, centre text '{CenterText()}'");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.picked (T06) ----------

    private static IEnumerator RunPicked()
    {
        if (!WorldReady(PickedName))
        {
            yield break;
        }
        var c = new Checks(PickedName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, PickedName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            // c. moves the mushroom's picked time back by its regrow time: the world clock must be older than that.
            ClockOk(c);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var mush = PlantCatalog.ByKey("Mushroom");
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            yield return Hold(rig, tool);

            // a. Bush picked by hand, then dug: no more berries, one transplant.
            var bush = new Aimed();
            yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, bush);
            if (c.Check(tool != null && bush.Seen, "a ripe raspberry bush is under the crosshair within reach" + bush.Why))
            {
                var pick = bush.Go.GetComponent<Pickable>();
                var want = PickAmount(pick);
                pick.Interact(player, false, false);
                yield return new WaitForSeconds(0.7f);
                var berries = StackSum(DropsNear(bush.Spot, SharedName("Raspberry")));
                c.Check(pick.GetPicked() && berries == want, $"bush picked by hand: {berries} raspberries (want {want})");
                yield return rig.AimAt(bush.AimAt);
                yield return Frames(3);
                c.Check(Replant.Target.IsFresh && ReferenceEquals(Replant.Target.View, bush.View), "picked bush still under the crosshair");
                yield return ReadyForKey(player);
                yield return Tap(UseAndSnap);
                yield return WaitGone(bush.View, 1.5f);
                c.Check(!Alive(bush.View) && CountByName(rig.Inv, rasp.ItemDisplayName) == 1, "picked bush dug up: one transplant");
                yield return new WaitForSeconds(0.7f);
                c.Check(StackSum(DropsNear(bush.Spot, SharedName("Raspberry"))) == berries, "no more berries from the picked bush");
            }

            // b. Forage picked by hand: nothing under the crosshair, E does nothing. c. Regrown: dug with its produce.
            var forage = new Aimed();
            yield return SpawnInReach(rig, taken, "Pickable_Mushroom", 0.8f, forage);
            if (c.Check(forage.Seen, "a ripe mushroom is under the crosshair within reach" + forage.Why))
            {
                var pick = forage.Go.GetComponent<Pickable>();
                var want = PickAmount(pick);
                pick.Interact(player, false, false);
                yield return new WaitForSeconds(0.7f);
                var mushrooms = StackSum(DropsNear(forage.Spot, SharedName("Mushroom")));
                c.Check(pick.GetPicked() && Alive(forage.View) && mushrooms == want, $"mushroom picked by hand, plant stays to regrow ({mushrooms} dropped, want {want})");
                yield return rig.AimAt(forage.AimAt);
                yield return Frames(4);
                var t = Replant.Target;
                c.Check(!(t.IsFresh && ReferenceEquals(t.View, forage.View)), "picked mushroom is not under the crosshair");
                c.Check(HintText().Length == 0 && !CrosshairYellow(), $"no hint over the picked mushroom ('{OneLine(HintText())}')");
                yield return ReadyForKey(player);
                yield return Tap(UseAndSnap);
                yield return new WaitForSeconds(0.5f);
                c.Check(Alive(forage.View) && CountByName(rig.Inv, mush.ItemDisplayName) == 0, "E on the picked mushroom does nothing");

                yield return Regrow(pick);
                c.Check(pick.CanBePicked(), "mushroom ripe again after its regrow time");
                yield return rig.AimAt(forage.AimAt);
                yield return Frames(4);
                c.Check(Replant.Target.IsFresh && ReferenceEquals(Replant.Target.View, forage.View), "regrown mushroom under the crosshair again");
                yield return ReadyForKey(player);
                yield return Tap(UseAndSnap);
                yield return WaitGone(forage.View, 1.5f);
                c.Check(!Alive(forage.View) && CountByName(rig.Inv, mush.ItemDisplayName) == 1, "regrown mushroom dug up: 'Mushroom transplant'");
                yield return new WaitForSeconds(0.7f);
                var after = StackSum(DropsNear(forage.Spot, SharedName("Mushroom")));
                c.Check(after == mushrooms + want, $"and its mushroom dropped ({mushrooms} -> {after})");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.costs (T23) ----------

    // Stamina bar told "no stamina" (vanilla Hud.StaminaBarEmptyFlash: trigger, then a state tagged nostamina).
    private static bool NoStaminaFlash()
    {
        var a = Hud.instance != null ? Hud.instance.m_staminaAnimator : null;
        if (a == null || !a.isActiveAndEnabled)
        {
            return false;
        }
        return a.GetBool("NoStamina") || a.GetCurrentAnimatorStateInfo(0).IsTag("nostamina") || a.GetNextAnimatorStateInfo(0).IsTag("nostamina");
    }

    private static IEnumerator RunCosts()
    {
        if (!WorldReady(CostsName))
        {
            yield break;
        }
        var c = new Checks(CostsName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, CostsName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            yield return Hold(rig, tool);
            var bush = new Aimed();
            yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, bush);
            if (!c.Check(tool != null && bush.Seen, "a ripe raspberry bush is under the crosshair within reach" + bush.Why))
            {
                c.Report();
                yield break;
            }
            var name = rasp.ItemDisplayName;

            // a. No stamina: bar flashes, nothing dug.
            yield return ReadyForKey(player);
            var flashBefore = NoStaminaFlash();
            player.m_stamina = 0f;
            player.m_staminaRegenTimer = 600f;
            var use0 = player.m_lastToolUseTime;
            var flash = false;
            yield return Tap(UseAndSnap, () => flash |= NoStaminaFlash());
            var until = Time.time + 1f;
            while (Time.time < until)
            {
                flash |= NoStaminaFlash();
                yield return null;
            }
            c.Check(Alive(bush.View) && CountByName(rig.Inv, name) == 0 && Near(player.m_lastToolUseTime, use0), "no stamina: nothing dug");
            c.Check(!flashBefore && flash, $"no stamina: the stamina bar flashes (before {flashBefore}, after the press {flash})");
            player.m_stamina = player.GetMaxStamina();
            player.m_staminaRegenTimer = 0f;

            // b. Use moved to another key: hint shows it, it digs; the old key (still the snap key) does not.
            var def = ZInput.instance.GetButtonDef(Replant.KeyboardButton);
            var oldKey = Localization.instance.GetBoundKeyString(Replant.KeyboardButton);
            var oldOverride = def.ButtonAction.bindings.Count > 0 ? def.ButtonAction.bindings[0].overridePath : null;
            var oldPath = def.GetActionPath();
            var newPath = oldPath == "<Keyboard>/f" ? "<Keyboard>/g" : "<Keyboard>/f";
            rig.Undo(() =>
            {
                if (string.IsNullOrEmpty(oldOverride))
                {
                    def.ResetBinding();
                }
                else
                {
                    def.Rebind(oldOverride);
                }
                ReplantHint.Reset();
            });
            def.Rebind(newPath);
            // The settings menu clears the hint texts while it is open; no menu here.
            ReplantHint.Reset();
            yield return rig.AimAt(bush.AimAt);
            yield return Frames(4);
            var newKey = Localization.instance.GetBoundKeyString(Replant.KeyboardButton);
            c.Check(def.GetActionPath() == newPath && newKey != oldKey, $"Use now on {def.GetActionPath()} (shown '{newKey}', before '{oldKey}')");
            var wantHint = KeyHint(HoverNameOf(bush.Go, rasp));
            c.Check(HintText() == wantHint && wantHint.Contains("<b>" + newKey + "</b>"), $"hint shows the new key: '{OneLine(HintText())}'");
            yield return ReadyForKey(player);
            yield return Tap(SnapKey);
            yield return new WaitForSeconds(0.4f);
            c.Check(Alive(bush.View), "the old key (now only the snap key) digs nothing");
            player.m_manualSnapPoint = -1;
            yield return ReadyForKey(player);
            yield return Tap(Replant.KeyboardButton);
            yield return WaitGone(bush.View, 1.5f);
            c.Check(!Alive(bush.View) && CountByName(rig.Inv, name) == 1, "the new Use key digs the bush up");
            if (string.IsNullOrEmpty(oldOverride))
            {
                def.ResetBinding();
            }
            else
            {
                def.Rebind(oldOverride);
            }
            ReplantHint.Reset();
            c.Check(def.GetActionPath() == oldPath, $"Use key put back ({def.GetActionPath()})");

            // c. Build menu open: E and gamepad X dig nothing; the press right after it closes neither.
            var second = new Aimed();
            yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, second);
            if (c.Check(second.Seen, "a second bush is under the crosshair within reach" + second.Why))
            {
                yield return ReadyForKey(player);
                Hud.instance.TogglePieceSelection();
                yield return Frames(3);
                if (c.Check(Hud.IsPieceSelectionVisible(), "build menu open"))
                {
                    yield return Tap(UseAndSnap);
                    yield return Tap(new[] { Replant.PadButtonX, Replant.PadAlias });
                    yield return Frames(3);
                    c.Check(Alive(second.View) && CountByName(rig.Inv, name) == 1, "menu open: E and X dig nothing");
                    c.Check(HintText().Length == 0, $"menu open: no Replant hint ('{OneLine(HintText())}')");
                }
                if (Hud.IsPieceSelectionVisible())
                {
                    // Vanilla close: input delay 0.2 s. The same key press that closed it must not dig.
                    Hud.instance.TogglePieceSelection();
                }
                yield return Tap(UseAndSnap);
                yield return Frames(3);
                c.Check(!Hud.IsPieceSelectionVisible() && Alive(second.View), "press right after the menu closed digs nothing");
                yield return new WaitForSeconds(0.4f);
                yield return rig.AimAt(second.AimAt);
                yield return ReadyForKey(player);
                yield return Tap(UseAndSnap);
                yield return WaitGone(second.View, 1.5f);
                c.Check(!Alive(second.View) && CountByName(rig.Inv, name) == 2, "menu closed for a moment: E digs again");
            }
            c.Report();
        }
        finally
        {
            if (Hud.instance != null && Hud.IsPieceSelectionVisible())
            {
                Hud.HidePieceSelection();
            }
            rig.Restore();
        }
    }

    // ---------- replant.full (T25) ----------

    private static IEnumerator RunFull()
    {
        if (!WorldReady(FullName))
        {
            yield break;
        }
        var c = new Checks(FullName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, FullName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var blue = PlantCatalog.ByKey("BlueberryBush");
            var inv = rig.Inv;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var spot = Vector3.zero;
            if (!c.Check(tool != null && FindPlantSpot(rig, taken, 1.2f, out spot), "cultivator and a free spot"))
            {
                c.Report();
                yield break;
            }
            var bush = rig.Spawn("BlueberryBush", spot, Quaternion.identity);
            yield return Settle();
            yield return Stand(rig, spot);
            var view = bush.GetComponent<ZNetView>();
            var fill = new List<ItemDrop.ItemData>();
            var empty = inv.GetEmptySlots();
            for (var i = 0; i < empty; i++)
            {
                var club = rig.Give("Club");
                if (club == null)
                {
                    break;
                }
                fill.Add(club);
            }
            c.Check(inv.GetEmptySlots() == 0 && fill.Count > 0, $"inventory full ({fill.Count} clubs added)");
            ClearCenter();
            c.Check(Replant.TryReplant(player, tool, view, blue, false, out var refusal), $"bush dug with a full inventory ({refusal})");
            c.Check(CenterText() == L("$msg_noroom"), $"centre text '{CenterText()}' (the game's no room line '{L("$msg_noroom")}')");
            c.Check(CountByName(inv, blue.ItemDisplayName) == 0, "no transplant in the full inventory");
            yield return new WaitForSeconds(0.6f);
            var drops = DropsNear(spot, blue.ItemDisplayName);
            if (c.Check(drops.Count == 1 && drops[0].m_itemData.m_stack == 1, $"one transplant lies where the bush stood ({drops.Count})"))
            {
                inv.RemoveItem(fill[0]);
                c.Check(player.Pickup(drops[0].gameObject, false, false), "picked up after making room");
                c.Check(CountByName(inv, blue.ItemDisplayName) == 1, "transplant now in the inventory");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.helm (T33) ----------

    private static IEnumerator RunHelm()
    {
        if (!WorldReady(HelmName))
        {
            yield break;
        }
        var c = new Checks(HelmName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, HelmName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            yield return Hold(rig, tool);
            var bush = new Aimed();
            yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, bush);
            if (!c.Check(tool != null && bush.Seen, "a ripe raspberry bush is under the crosshair within reach (not steering yet)" + bush.Why))
            {
                c.Report();
                yield break;
            }
            c.Check(HintText().Contains("Replant") && CrosshairYellow(), "control: hint and yellow crosshair before taking the helm");

            // Ship frozen on land next to the player, helm about 2 m away to the side, hull pointing away.
            var shipName = ZNetScene.instance.GetPrefab("Raft") != null ? "Raft" : "Karve";
            var toBush = bush.Spot - player.transform.position;
            toBush.y = 0f;
            var side = Vector3.Cross(Vector3.up, toBush.normalized);
            var helmAt = player.transform.position + side * 2.2f + Vector3.up * 0.3f;
            var shipGo = rig.Spawn(shipName, helmAt + side * 3f, Quaternion.LookRotation(side));
            var ship = shipGo != null ? shipGo.GetComponent<Ship>() : null;
            if (!c.Check(ship != null, $"ship {shipName} spawned"))
            {
                c.Report();
                yield break;
            }
            if (ship.m_body != null)
            {
                ship.m_body.isKinematic = true;
            }
            yield return Frames(2);
            var controls = ship.m_shipControlls;
            if (!c.Check(controls != null, "ship has a helm"))
            {
                c.Report();
                yield break;
            }
            var shift = helmAt - controls.transform.position;
            shipGo.transform.position += shift;
            if (ship.m_body != null)
            {
                ship.m_body.position = shipGo.transform.position;
            }
            Physics.SyncTransforms();
            yield return Settle();
            c.Note($"{shipName} helm {F(Vector3.Distance(controls.transform.position, player.transform.position))} m from the player");

            player.StartDoodadControl(controls);
            yield return rig.AimAt(bush.AimAt);
            var steering = true;
            var hinted = false;
            for (var i = 0; i < 12; i++)
            {
                yield return null;
                steering &= player.m_doodadController != null;
                hinted |= Replant.Target.IsFresh || HintText().Length > 0 || CrosshairYellow();
            }
            c.Check(steering, "player holds the helm with the cultivator in hand");
            c.Check(player.InPlaceMode() && ReferenceEquals(player.GetRightItem(), tool), "still in build mode at the helm");
            c.Check(!hinted, $"at the helm: no target, no hint, crosshair not yellow ('{OneLine(HintText())}')");

            var before = CountByName(rig.Inv, rasp.ItemDisplayName);
            yield return ReadyForKey(player);
            yield return Tap(UseAndSnap);
            yield return Frames(3);
            c.Check(player.m_doodadController == null, "E lets go of the helm");
            c.Check(Alive(bush.View) && CountByName(rig.Inv, rasp.ItemDisplayName) == before, "the same E digs nothing");
            yield return rig.AimAt(bush.AimAt);
            yield return Frames(3);
            c.Check(Replant.Target.IsFresh && HintText().Contains("Replant"), "off the helm: hint back");
            yield return ReadyForKey(player);
            yield return Tap(UseAndSnap);
            yield return WaitGone(bush.View, 1.5f);
            c.Check(!Alive(bush.View) && CountByName(rig.Inv, rasp.ItemDisplayName) == before + 1, "the next E digs the bush up");

            // Gamepad X while steering: digs nothing. Second bush at the same place.
            yield return new WaitForSeconds(0.5f);
            var again = rig.Spawn("RaspberryBush", bush.Spot, Quaternion.identity);
            yield return Settle();
            var againView = again.GetComponent<ZNetView>();
            player.StartDoodadControl(controls);
            yield return rig.AimAt(AimPoint(again));
            yield return Frames(3);
            if (c.Check(player.m_doodadController != null, "at the helm again"))
            {
                yield return ReadyForKey(player);
                yield return Tap(new[] { Replant.PadButtonX, Replant.PadAlias });
                yield return Frames(3);
                c.Check(Alive(againView) && CountByName(rig.Inv, rasp.ItemDisplayName) == before + 1, "gamepad X at the helm digs nothing");
            }
            if (player.m_doodadController != null)
            {
                player.StopDoodadControl();
            }
            c.Report();
        }
        finally
        {
            if (player != null && player.m_doodadController != null)
            {
                player.StopDoodadControl();
            }
            rig.Restore();
        }
    }

    // ---------- replant.pad (T22) ----------

    private static IEnumerator RunPad()
    {
        if (!WorldReady(PadName))
        {
            yield break;
        }
        var c = new Checks(PadName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, PadName);
        var taken = new List<Vector3>();
        var zin = ZInput.instance;
        var layout0 = ZInput.InputLayout;
        var source0 = ZInput.m_inputSource;
        try
        {
            PrepareInput(rig);
            rig.Undo(() =>
            {
                // No preference written: layout static and the button set of that layout only.
                ZInput.m_inputSource = source0;
                if (ZInput.InputLayout != layout0)
                {
                    ZInput.InputLayout = layout0;
                    zin.UpdateGamepadInputLayout();
                }
                ReplantHint.Reset();
            });
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            yield return Hold(rig, tool);
            if (!c.Check(tool != null, "cultivator given"))
            {
                c.Report();
                yield break;
            }
            var keyboardKey = "<b>" + Localization.instance.GetBoundKeyString(Replant.KeyboardButton) + "</b>";
            var dug = 0;
            foreach (var layout in new[] { InputLayout.Default, InputLayout.Alternative1, InputLayout.Alternative2 })
            {
                var tag = layout.ToString();
                ZInput.m_inputSource = ZInput.InputSource.Gamepad;
                if (ZInput.InputLayout != layout)
                {
                    ZInput.InputLayout = layout;
                    zin.UpdateGamepadInputLayout();
                }
                ReplantHint.Reset();
                yield return Frames(2);
                var alias = Replant.PadAlias;
                c.Check(HasButton(Replant.PadButtonX) && HasButton(alias) && HasButton("JoyAltKeys"),
                    $"{tag}: buttons {Replant.PadButtonX}, {alias}, JoyAltKeys exist");
                var bush = new Aimed();
                yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, bush);
                if (!c.Check(bush.Seen, $"{tag}: a bush is under the crosshair within reach" + bush.Why))
                {
                    continue;
                }
                var hint = HintText();
                c.Note($"{tag}: hint '{OneLine(hint)}'");
                c.Check(hint.EndsWith("Replant", StringComparison.Ordinal) && !hint.Contains(keyboardKey)
                        && (hint.Contains("<sprite=") || hint.Contains("<b>X</b>")),
                    $"{tag}: hint shows a gamepad button, never the keyboard key ('{OneLine(hint)}')");
                c.Check(CrosshairYellow(), $"{tag}: crosshair yellow");

                // Alt keys held + X, and LB: nothing dug.
                yield return ReadyForKey(player);
                PressKey("JoyAltKeys");
                yield return Frames(3);
                yield return Tap(new[] { Replant.PadButtonX, alias });
                LetGo("JoyAltKeys");
                yield return Frames(3);
                c.Check(Alive(bush.View), $"{tag}: alt keys held + X digs nothing");
                if (HasButton("JoyLBumper"))
                {
                    yield return Tap("JoyLBumper");
                    yield return Frames(2);
                    c.Check(Alive(bush.View), $"{tag}: LB digs nothing");
                }
                // X: dug, player not sitting.
                yield return rig.AimAt(bush.AimAt);
                yield return ReadyForKey(player);
                yield return Tap(new[] { Replant.PadButtonX, alias });
                yield return WaitGone(bush.View, 1.5f);
                if (c.Check(!Alive(bush.View), $"{tag}: X digs the bush up"))
                {
                    dug++;
                }
                c.Check(CountByName(rig.Inv, rasp.ItemDisplayName) == dug, $"{tag}: transplant given ({CountByName(rig.Inv, rasp.ItemDisplayName)})");
                yield return new WaitForSeconds(0.5f);
                c.Check(!player.IsSitting(), $"{tag}: player did not sit down");

                // Build menu still opens with its own button (nothing under the crosshair).
                var menuButton = layout == InputLayout.Default ? "JoyUse" : "JoyBuildMenu";
                if (c.Check(HasButton(menuButton), $"{tag}: build menu button {menuButton} exists"))
                {
                    rig.Look(player.m_lookYaw.eulerAngles.y, -60f);
                    yield return Frames(3);
                    yield return ReadyForKey(player);
                    yield return Tap(menuButton);
                    yield return Frames(3);
                    c.Check(Hud.IsPieceSelectionVisible(), $"{tag}: {menuButton} opens the build menu");
                    Hud.HidePieceSelection();
                    yield return Frames(3);
                }
            }
            c.Report();
        }
        finally
        {
            if (Hud.instance != null && Hud.IsPieceSelectionVisible())
            {
                Hud.HidePieceSelection();
            }
            rig.Restore();
        }
    }

    // ---------- replant.ward (M07, M04: one game part) ----------

    private static IEnumerator RunWard()
    {
        if (!WorldReady(WardName))
        {
            yield break;
        }
        var c = new Checks(WardName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, WardName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var inv = rig.Inv;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var berry = SharedName("Raspberry");

            // Two digs of one bush in the same frame (two players pressing at once, seen from one game).
            var spotA = Vector3.zero;
            if (c.Check(tool != null && FindPlantSpot(rig, taken, 1.2f, out spotA), "cultivator and a free spot"))
            {
                var bush = rig.Spawn("RaspberryBush", spotA, Quaternion.identity);
                yield return Settle();
                var view = bush.GetComponent<ZNetView>();
                var want = PickAmount(bush.GetComponent<Pickable>());
                var first = Replant.TryReplant(player, tool, view, rasp, false, out var why1);
                var second = Replant.TryReplant(player, tool, view, rasp, false, out var why2);
                c.Check(first && !second && why2 == "the plant is gone", $"second dig of the same bush refused ({why1} / {why2})");
                c.Check(CountByName(inv, rasp.ItemDisplayName) == 1, "one transplant only");
                yield return new WaitForSeconds(0.7f);
                c.Check(StackSum(DropsNear(spotA, berry)) == want, $"berries dropped once ({StackSum(DropsNear(spotA, berry))}, want {want})");
            }

            // Ward of another player: refused with the game's line; permitted: allowed.
            if (c.Check(FindPlantSpot(rig, taken, 1.2f, out var spotB), "free spot for the ward checks"))
            {
                var bush = rig.Spawn("RaspberryBush", spotB, Quaternion.identity);
                var wardAt = spotB + Vector3.right * 2f;
                wardAt.y = Ground(wardAt);
                var ward = rig.Spawn("guard_stone", wardAt, Quaternion.identity);
                var inside = spotB + Vector3.left * 1.5f;
                inside.y = Ground(inside);
                var sapling = rig.Spawn(rasp.SaplingName, inside, Quaternion.identity);
                yield return Settle();
                var view = bush.GetComponent<ZNetView>();
                var sapView = sapling != null ? sapling.GetComponent<ZNetView>() : null;
                var area = ward != null ? ward.GetComponent<PrivateArea>() : null;
                if (c.Check(area != null && Alive(sapView), "ward and a young transplant spawned"))
                {
                    var piece = sapling.GetComponent<Piece>();
                    piece.m_creator = OtherPlayer;
                    sapView.GetZDO().Set(ZDOVars.s_creator, OtherPlayer);
                    area.m_nview.GetZDO().Set(ZDOVars.s_enabled, true);
                    c.Check(!PrivateArea.CheckAccess(spotB, 0f, false) && !PrivateArea.CheckAccess(inside, 0f, false), "ward active, not ours");
                    ClearCenter();
                    c.Check(!Replant.TryReplant(player, tool, view, rasp, false, out var why) && why == "a ward protects it" && Alive(view),
                        $"plant inside another player's ward: refused ({why})");
                    c.Check(CenterText() == L("$msg_privatezone"), $"centre text '{CenterText()}' (the game's ward line)");
                    c.Check(!Replant.TryReplant(player, tool, sapView, rasp, true, out why) && why == "a ward protects it" && Alive(sapView),
                        $"another player's young transplant inside the ward: refused ({why})");
                    area.AddPermitted(player.GetPlayerID(), player.GetPlayerName());
                    yield return Frames(2);
                    c.Check(PrivateArea.CheckAccess(spotB, 0f, false), "added to the ward's list");
                    var before = CountByName(inv, rasp.ItemDisplayName);
                    c.Check(Replant.TryReplant(player, tool, view, rasp, false, out why) && !Alive(view), $"permitted: plant dug ({why})");
                    c.Check(Replant.TryReplant(player, tool, sapView, rasp, true, out why) && !Alive(sapView), $"permitted: the other player's young transplant dug ({why})");
                    c.Check(CountByName(inv, rasp.ItemDisplayName) == before + 2, "two transplants given");
                }
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.bronze (T02) ----------

    private static IEnumerator RunBronze()
    {
        if (!WorldReady(BronzeName))
        {
            yield break;
        }
        var c = new Checks(BronzeName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, BronzeName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var names = new Dictionary<string, string>
            {
                { "BlueberryBush", "Blueberry bush transplant" },
                { "Mushroom", "Mushroom transplant" },
                { "MushroomYellow", "Yellow mushroom transplant" },
                { "Thistle", "Thistle transplant" },
                { "Dandelion", "Dandelion transplant" },
            };
            foreach (var pair in names)
            {
                var kind = PlantCatalog.ByKey(pair.Key);
                var spot = Vector3.zero;
                if (!c.Check(tool != null && kind != null && kind.Tier == 1 && FindPlantSpot(rig, taken, 1.2f, out spot),
                        $"{pair.Key}: level 1 plant and a free spot"))
                {
                    continue;
                }
                yield return DigCheck(rig, c, tool, kind, kind.WildPrefabs[0], spot, pair.Value, -1);
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Wild plant spawned at spot and dug with tool (the action itself, no button): transplant with this exact name,
    // produce on the ground = a hand pick (wantProduce -1) or this many.
    private static IEnumerator DigCheck(Rig rig, Checks c, ItemDrop.ItemData tool, PlantKind kind, string wild, Vector3 spot, string wantName, int wantProduce)
    {
        var go = rig.Spawn(wild, spot, Quaternion.identity);
        yield return Settle();
        var view = go != null ? go.GetComponent<ZNetView>() : null;
        if (!c.Check(Alive(view), $"{wild}: spawned"))
        {
            yield break;
        }
        var pick = go.GetComponent<Pickable>();
        var hand = pick != null && pick.CanBePicked() ? PickAmount(pick) : 0;
        var extra = pick != null ? pick.m_extraDrops : null;
        var before = CountByName(rig.Inv, wantName);
        // Dig below is the action called directly. What a press of E do before it: vanilla hover ray, then this same
        // look-up of the plant by its ZDO prefab (Replant.OnUpdatePlacement).
        c.Check(PlantCatalog.TryFind(view.GetZDO().GetPrefab(), out var found, out var asSapling) && ReferenceEquals(found, kind) && !asSapling,
            $"{wild}: the cultivator's target look-up knows it as a wild {kind.Key} ({(found != null ? found.Key : "unknown")}, sapling {asSapling})");
        c.Check(Replant.TryReplant(rig.Player, tool, view, kind, false, out var refusal) && !Alive(view), $"{wild}: dug with a level {tool.m_quality} cultivator ({refusal})");
        var given = rig.Inv.GetAllItems().FirstOrDefault(i => ReferenceEquals(TransplantContent.KindOfItem(i), kind));
        c.Check(CountByName(rig.Inv, wantName) == before + 1 && given != null && given.m_shared.m_name == wantName,
            $"{wild}: gives '{wantName}' ({(given != null ? given.m_shared.m_name : "nothing")})");
        if (pick == null)
        {
            yield break;
        }
        yield return new WaitForSeconds(0.7f);
        var produce = StackSum(DropsNear(spot, SharedName(kind.ProduceItem), 3f));
        var want = wantProduce >= 0 ? wantProduce : hand;
        c.Check(produce == want && want >= 1,
            $"{wild}: {produce} {kind.ProduceItem} dropped (want {want}; plant amount {pick.m_amount}, extra drops {(extra != null && !extra.IsEmpty() ? "yes" : "none")}, "
            + $"skill bonus chance of this plant {F(pick.m_maxLevelBonusChance)})");
    }

    // ---------- replant.levels (T12, T17, T18, T28: dig part) ----------

    private static IEnumerator RunLevels()
    {
        if (!WorldReady(LevelsName))
        {
            yield break;
        }
        var c = new Checks(LevelsName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, LevelsName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig);
            var tools = new Dictionary<int, ItemDrop.ItemData>();
            for (var q = 4; q <= 7; q++)
            {
                tools[q] = rig.Give(PlantCatalog.CultivatorPrefab, 1, q);
            }
            if (!c.Check(tools.Values.All(t => t != null), "cultivators level 4 to 7 given"))
            {
                c.Report();
                yield break;
            }
            c.Check(Replant.TierNeededText(5) == "Needs an eitr cultivator (level 5)" && Replant.TierNeededText(6) == "Needs a flametal cultivator (level 6)"
                    && Replant.TierNeededText(7) == "Needs a bloodgold cultivator (level 7)", "level needed texts");

            // T28: a level 4 cultivator that never went through our upgrade is a black metal cultivator.
            var vanillaIcon = CultivatorTiers.CultivatorDrop != null ? CultivatorTiers.CultivatorDrop.m_itemData.m_shared.m_icons[0] : null;
            var tip4 = tools[4].GetTooltip();
            c.Check(tip4.Contains("Tier: <color=orange>Black metal cultivator</color>"), "spawned level 4: tooltip says black metal cultivator");
            c.Check(vanillaIcon != null && ReferenceEquals(tools[4].GetIcon(), TierIcons.CultivatorIcon(vanillaIcon, 4)), "spawned level 4: shows the level 4 gem");

            // wild prefab, plant key, lowest level, transplant name, produce count (-1 = like a hand pick).
            var rows = new[]
            {
                new object[] { "CloudberryBush", "CloudberryBush", 4, "Cloudberry bush transplant", -1, 1.2f },
                new object[] { "YggaShoot_small1", "YggaShoot", 5, "Yggdrasil shoot transplant", -1, 2f },
                new object[] { "YggaShoot1", "YggaShoot", 5, "Yggdrasil shoot transplant", -1, 3f },
                new object[] { "Pickable_SmokePuff", "SmokePuff", 6, "Smoke puff transplant", 1, 1f },
                new object[] { "Pickable_Fiddlehead", "Fiddlehead", 6, "Fiddlehead transplant", 3, 1f },
                new object[] { "LingonberryBush", "LingonberryBush", 7, "Lingonberry bush transplant", -1, 1.2f },
            };
            foreach (var row in rows)
            {
                var wild = (string)row[0];
                var kind = PlantCatalog.ByKey((string)row[1]);
                var level = (int)row[2];
                var spot = Vector3.zero;
                if (!c.Check(kind != null && kind.Tier == level && FindPlantSpot(rig, taken, (float)row[5], out spot), $"{wild}: needs level {level}, free spot"))
                {
                    continue;
                }
                if (level > 4)
                {
                    // One level too low: refused with the level needed, plant stays.
                    var low = rig.Spawn(wild, spot, Quaternion.identity);
                    yield return Settle();
                    var lowView = low != null ? low.GetComponent<ZNetView>() : null;
                    if (c.Check(Alive(lowView), $"{wild}: spawned"))
                    {
                        ClearCenter();
                        c.Check(!Replant.TryReplant(player, tools[level - 1], lowView, kind, false, out var why) && why == Replant.TierNeededText(level)
                                && Alive(lowView) && CenterText() == Replant.TierNeededText(level),
                            $"{wild}: level {level - 1} refused ('{why}')");
                        rig.Destroy(low);
                        yield return Settle();
                    }
                }
                yield return DigCheck(rig, c, tools[level], kind, wild, spot, (string)row[3], (int)row[4]);
                if (kind.NeedsRoot)
                {
                    yield return new WaitForSeconds(0.5f);
                    c.Check(DropsNear(spot, SharedName("YggdrasilWood"), 5f).Count == 0 && DropsNear(spot, SharedName("Wood"), 5f).Count == 0,
                        $"{wild}: no wood dropped");
                }
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }
}
#endif
