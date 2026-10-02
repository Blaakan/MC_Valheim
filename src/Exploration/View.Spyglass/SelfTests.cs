using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Exploration.ViewSpyglassMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod View.Spyglass). Design 8:
//   spyglass.network  rules on the wire (round trip, wild values clamped, unknown layout and cut-off refused), which
//                     rules apply (own / server / pending), push debounce, join check verdicts (pure)
//   spyglass.item     item and recipe registered (database, network list, not in the weapon achievement list),
//                     item data (tool like the hammer, no build pieces, no attack, no durability, icon), equip
//                     takes both hands (a torch put away, a torch next replaces it), a click never punches,
//                     hand copy carries the model; screenshots held and dropped; NOTE build report
//   spyglass.view     live: raise (state, zoom, camera at the eye, own body hidden, overlay, crosshair off, ZDO flag),
//                     feet locked, aim slowed by the zoom, wheel zoom cap, Distant Horizons slot and fog (when it
//                     runs), lower (everything back), pending rules = no raise, unequip = instant drop; screenshots
//   spyglass.pose     live: arm pose seen from outside (camera parked), eyepiece at the eye, also the way other
//                     players' games pose it; screenshots
//   spyglass.far      live: travel to the highest ground near the spawn, look over open land, raise x8 and x3;
//                     screenshots (far land and objects come from Distant Horizons when it runs), travel back
//   spyglass.export   writes the item and package icons (PNG) next to the screenshots; NOTE dumps of rig, camera,
//                     canvases
// Me force rules only with ServerRules.TestRules / TestPending and drive the spyglass with Scope.Test*: never config.
// Rig put back controls, look, time, inventory and equipment.
internal static class SelfTests
{
    private const string NetworkName = "spyglass.network";
    private const string ItemName = "spyglass.item";
    private const string ViewName = "spyglass.view";
    private const string PoseName = "spyglass.pose";
    private const string FarName = "spyglass.far";
    private const string ExportName = "spyglass.export";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(NetworkName, RunNetwork);
        SelfTest.Register(ItemName, RunItem);
        SelfTest.Register(ViewName, RunView);
        SelfTest.Register(PoseName, RunPose);
        SelfTest.Register(FarName, RunFar);
        SelfTest.Register(ExportName, RunExport);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(NetworkName);
        SelfTest.Unregister(ItemName);
        SelfTest.Unregister(ViewName);
        SelfTest.Unregister(PoseName);
        SelfTest.Unregister(FarName);
        SelfTest.Unregister(ExportName);
        ClearOverrides();
#endif
    }

#if DEBUG
    private static void ClearOverrides()
    {
        ServerRules.TestPending = false;
        ServerRules.TestRules = null;
        ScopeCamera.TestPoseView = false;
        ArmPose.TestAsRemote = false;
        Plugin.TestInactive = false;
    }

    // ---------- helpers ----------

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
            if (_failures.Count == 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK{extra}");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string F(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

    private static bool Near(float a, float b, float tolerance = 0.001f) => Mathf.Abs(a - b) <= tolerance;

    // Me = what the live tests change on the player and world. Restore (finally, no yield) put everything back.
    private sealed class SpyRig
    {
        internal readonly Player Player;
        private readonly float _pitch;
        private readonly Quaternion _yaw;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly List<ItemDrop.ItemData> _added = new List<ItemDrop.ItemData>();
        private PlayerController _controller;
        private bool _controllerEnabled;
        private bool _envSaved;
        private bool _todOn;
        private float _tod;
        private readonly string _name;
        private readonly Quaternion _originRotation;
        private string _debugEnv;

        internal SpyRig(Player player, string name)
        {
            Player = player;
            Origin = player.transform.position;
            _originRotation = player.transform.rotation;
            _name = name;
            _pitch = player.m_lookPitch;
            _yaw = player.m_lookYaw;
            _right = player.GetRightItem();
            _left = player.GetLeftItem();
        }

        internal ItemDrop.ItemData Give(string prefab)
        {
            var item = Player.GetInventory().AddItem(prefab, 1, 1, 0, 0L, "", false);
            if (item != null)
            {
                _added.Add(item);
            }
            return item;
        }

        // Me drive the player (no keyboard in between).
        internal void TakeControls()
        {
            if (_controller == null)
            {
                _controller = Player.GetComponent<PlayerController>();
                if (_controller != null)
                {
                    _controllerEnabled = _controller.enabled;
                    _controller.enabled = false;
                }
            }
            Drive(Vector3.zero);
        }

        internal void Drive(Vector3 move) =>
            Player.SetControls(move, false, false, false, false, false, false, false, false, false, false);

        // Noon for the screenshots (like console "tod 0.5").
        internal void Noon()
        {
            var env = EnvMan.instance;
            if (!_envSaved)
            {
                _envSaved = true;
                _todOn = env.m_debugTimeOfDay;
                _tod = env.m_debugTime;
            }
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0.5f;
        }

        // Clear weather for far views (like console "env Clear"); put back by Restore.
        internal void ClearWeather()
        {
            var env = EnvMan.instance;
            if (_debugEnv == null)
            {
                _debugEnv = env.m_debugEnv ?? "";
            }
            env.m_debugEnv = "Clear";
        }

        internal void Look(float yaw, float pitch)
        {
            Player.m_lookYaw = Quaternion.Euler(0f, yaw, 0f);
            Player.m_lookPitch = pitch;
            Player.UpdateEyeRotation();
            Player.m_lookDir = Player.m_eye.forward;
        }

        internal Vector3 Origin { get; } = Vector3.zero;
        internal bool Travelled;
        internal bool Back;

        // Vanilla teleport (loads the far zones, waits for the floor). True when there.
        internal IEnumerator Travel(Vector3 target, Quaternion rotation, Checks c, float timeout)
        {
            Travelled = true;
            var start = Time.time;
            while (!Player.TeleportTo(target, rotation, true))
            {
                if (Time.time - start > 6f)
                {
                    c.Check(false, "Player.TeleportTo refused for 6 s");
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }
            yield return null;
            while (Player != null && Player.IsTeleporting())
            {
                if (Time.time - start > timeout)
                {
                    c.Check(false, $"still teleporting after {F(timeout)} s");
                    yield break;
                }
                yield return new WaitForSeconds(0.25f);
            }
            c.Note($"travel took {F(Time.time - start)} s");
        }

        internal void Restore()
        {
            ClearOverrides();
            Try("spyglass down", Scope.Abort);
            Try("controls", () =>
            {
                if (_controller != null)
                {
                    Drive(Vector3.zero);
                    _controller.enabled = _controllerEnabled;
                }
            });
            Try("look", () =>
            {
                Player.m_lookYaw = _yaw;
                Player.m_lookPitch = _pitch;
                Player.UpdateEyeRotation();
            });
            Try("time", () =>
            {
                if (_envSaved && EnvMan.instance != null)
                {
                    EnvMan.instance.m_debugTimeOfDay = _todOn;
                    EnvMan.instance.m_debugTime = _tod;
                }
            });
            Try("weather", () =>
            {
                if (_debugEnv != null && EnvMan.instance != null)
                {
                    EnvMan.instance.m_debugEnv = _debugEnv;
                }
            });
            Try("items", () =>
            {
                var inv = Player.GetInventory();
                foreach (var item in _added)
                {
                    if (Player.IsItemEquiped(item))
                    {
                        Player.UnequipItem(item, false);
                    }
                    inv.RemoveItem(item);
                }
                if (_right != null && inv.ContainsItem(_right) && !Player.IsItemEquiped(_right))
                {
                    Player.EquipItem(_right, false);
                }
                if (_left != null && inv.ContainsItem(_left) && !Player.IsItemEquiped(_left))
                {
                    Player.EquipItem(_left, false);
                }
            });
            Try("travel back", () =>
            {
                if (!Travelled || Back || Player == null)
                {
                    return;
                }
                // Test cut short: send the player home (vanilla finish the teleport after the test).
                if (Player.IsTeleporting())
                {
                    Player.m_teleportTargetPos = Origin;
                    Player.m_teleportTargetRot = _originRotation;
                }
                else
                {
                    Player.TeleportTo(Origin, _originRotation, true);
                }
            });
        }

        internal IEnumerator GoHome(Checks c)
        {
            if (!Travelled || Back)
            {
                yield break;
            }
            yield return Travel(Origin, _originRotation, c, 40f);
            Back = true;
        }

        private void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                SelfTest.Note(_name, $"restore {what} failed: {e.Message}");
            }
        }
    }

    private static IEnumerator WaitFor(Func<bool> done, float timeout)
    {
        var start = Time.time;
        while (!done() && Time.time - start < timeout)
        {
            yield return null;
        }
    }

    private static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return null;
        }
    }

    // ---------- spyglass.network ----------

    private static IEnumerator RunNetwork()
    {
        var c = new Checks(NetworkName);

        var own = new SpyglassRules
        {
            RecipeResources = "Bronze:3,Crystal:1",
            RecipeStation = "piece_workbench",
            RecipeStationLevel = 2,
            MaxMagnification = 12f,
            FogClearing = 0.5f,
        };
        var pkg = new ZPackage();
        own.Write(pkg);
        pkg.SetPos(0);
        c.Check(SpyglassRules.TryRead(pkg, out var back, out var clamped) && !clamped, "round trip read");
        if (back != null)
        {
            c.Check(back.Describe() == own.Describe(), $"round trip same rules ({back.Describe()})");
        }

        var wild = new ZPackage();
        wild.Write(SpyglassRules.Layout);
        wild.Write(new string('x', SpyglassRules.TextMax + 50));
        wild.Write("forge");
        wild.Write(99);
        wild.Write(float.NaN);
        wild.Write(7f);
        wild.SetPos(0);
        c.Check(SpyglassRules.TryRead(wild, out var w, out var wc) && wc, "wild values read and flagged");
        if (w != null)
        {
            c.Check(w.RecipeResources.Length == SpyglassRules.TextMax, "long recipe cut");
            c.Check(w.RecipeStationLevel == SpyglassRules.StationLevelMax, "station level clamped");
            c.Check(Near(w.MaxMagnification, SpyglassRules.Default.MaxMagnification), "NaN zoom = default");
            c.Check(Near(w.FogClearing, 1f), "fog clearing clamped to 1");
        }

        var other = new ZPackage();
        other.Write(SpyglassRules.Layout + 1);
        other.SetPos(0);
        c.Check(!SpyglassRules.TryRead(other, out _, out _), "unknown layout refused");
        var cut = new ZPackage();
        cut.Write(SpyglassRules.Layout);
        cut.Write("Bronze:2");
        cut.SetPos(0);
        c.Check(!SpyglassRules.TryRead(cut, out _, out _), "cut-off package refused");

        var mine = SpyglassRules.Default;
        var server = new SpyglassRules { MaxMagnification = 3f };
        c.Check(ReferenceEquals(ServerRules.Select(false, server, mine), mine), "not client = own");
        c.Check(ReferenceEquals(ServerRules.Select(true, server, mine), server), "client = server's");
        c.Check(ServerRules.Select(true, null, mine).IsPending, "client without server rules = pending");
        c.Check(SpyglassRules.Pending.RecipeResources == "", "pending has no recipe");
        c.Check(ServerRules.Settled(10f, 10f - ServerRules.PushDelay) && !ServerRules.Settled(10f, 9.9f), "push debounce");

        c.Check(PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.Compatible, "join: compatible");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse, "join: refuse");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed, "join: allowed");
        c.Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip, "join: not server");
        c.Check(PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip, "join: being kicked");

        // Zoom maths: x4 on 65 degrees.
        var fov4 = ScopeCamera.ZoomedFov(65f, 4f);
        var ratio = Mathf.Tan(65f * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(fov4 * 0.5f * Mathf.Deg2Rad);
        c.Check(Near(ratio, 4f, 0.001f), $"x4 field of view {F(fov4)} (ratio {F(ratio)})");
        c.Check(Near(ScopeCamera.ZoomedFov(65f, 1f), 65f, 0.001f), "x1 keeps the field of view");

        c.Report();
        yield break;
    }

    // ---------- spyglass.item ----------

    private static IEnumerator RunItem()
    {
        var c = new Checks(ItemName);
        var player = Player.m_localPlayer;
        var db = ObjectDB.instance;
        if (player == null || db == null || ZNetScene.instance == null)
        {
            SelfTest.Fail(ItemName, "no player, item database or network list");
            yield break;
        }
        c.Note("build: " + SpyglassContent.BuildReport);
        c.Check(SpyglassContent.Built, "item built");
        c.Check(!SpyglassContent.MissingReported, "no missing-knife error");
        var prefab = db.GetItemPrefab(SpyglassContent.ItemName);
        c.Check(prefab != null && ReferenceEquals(prefab, SpyglassContent.ItemPrefab), "in the item database");
        c.Check(ZNetScene.instance.GetPrefab(SpyglassContent.ItemHash) != null, "in the network prefab list");
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        c.Check(drop != null && db.m_itemByData.ContainsKey(drop.m_itemData.m_shared), "known by its data");
        c.Check(!db.GetAllCraftableWeapons().Contains(drop), "not in the craft-every-weapon list");

        var recipe = SpyglassContent.CraftRecipe;
        c.Check(recipe != null && db.m_recipes.Contains(recipe), "recipe registered");
        if (recipe != null)
        {
            var rules = ServerRules.Current;
            c.Check(recipe.m_enabled, "recipe enabled");
            c.Check(recipe.m_craftingStation != null && recipe.m_craftingStation.name == rules.RecipeStation,
                $"station {rules.RecipeStation}");
            c.Check(recipe.m_minStationLevel == rules.RecipeStationLevel, "station level");
            var names = new List<string>();
            foreach (var r in recipe.m_resources)
            {
                names.Add(r.m_resItem.name + ":" + r.m_amount);
            }
            c.Check(string.Join(",", names.ToArray()) == rules.RecipeResources.Replace(" ", ""),
                $"materials {string.Join(",", names.ToArray())}");
        }

        if (drop != null)
        {
            var s = drop.m_itemData.m_shared;
            c.Check(s.m_itemType == ItemDrop.ItemData.ItemType.Tool, "a tool like the hammer");
            c.Check(s.m_attachOverride == ItemDrop.ItemData.ItemType.None && s.m_buildPieces == null,
                "tool back slot, no build pieces");
            c.Check(string.IsNullOrEmpty(s.m_attack.m_attackAnimation) && !drop.m_itemData.HavePrimaryAttack(), "no attack");
            c.Check(!drop.m_itemData.HaveSecondaryAttack(), "no secondary attack");
            c.Check(!s.m_useDurability, "no durability");
            c.Check(s.m_skillType == Skills.SkillType.None, "no skill");
            c.Check(s.m_icons != null && s.m_icons.Length == 1 && s.m_icons[0] != null && s.m_icons[0].name == "MC_Spyglass", "own icon");
            c.Check(drop.m_itemData.m_dropPrefab == prefab, "drop prefab");
            var knife = db.GetItemPrefab(SpyglassContent.VanillaKnifeName);
            var knifeShared = knife != null ? knife.GetComponent<ItemDrop>().m_itemData.m_shared : null;
            c.Check(knifeShared != null && !ReferenceEquals(knifeShared, s) && knifeShared.m_name != s.m_name,
                "vanilla knife untouched");
            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            var ours = 0;
            foreach (var r in renderers)
            {
                if (r.gameObject.name == SpyglassModel.ModelName)
                {
                    ours++;
                }
            }
            c.Check(ours == renderers.Length && ours == (SpyglassContent.GroundModelSeparate ? 2 : 1),
                $"only our model ({ours} of {renderers.Length} renderers), no knife mesh");
            var col = prefab.GetComponentInChildren<Collider>(true);
            c.Check(col != null && LayerMask.LayerToName(col.gameObject.layer) == "item", "dropped copy has a collider on the item layer");
        }

        var rig = new SpyRig(player, ItemName);
        GameObject dropped = null;
        bool? autoPickup = null;
        try
        {
            rig.Noon();
            var torch = rig.Give("Torch");
            var spy = rig.Give(SpyglassContent.ItemName);
            c.Check(spy != null && SpyglassContent.IsSpyglass(spy), "spyglass added to the inventory");
            if (spy != null && torch != null)
            {
                player.UnequipItem(player.GetLeftItem(), false);
                player.UnequipItem(player.GetRightItem(), false);
                player.EquipItem(torch, false);
                var torchHand = player.GetLeftItem() == torch ? "left" : player.GetRightItem() == torch ? "right" : "none";
                player.EquipItem(spy, false);
                c.Check(player.GetRightItem() == spy && player.GetLeftItem() == null && !player.IsItemEquiped(torch),
                    $"equipped in the right hand, both hands taken (the torch in the {torchHand} hand was put away)");
                player.EquipItem(torch, false);
                c.Check(player.GetRightItem() == torch && !player.IsItemEquiped(spy),
                    "a torch equipped next replaces it, like with the hammer");
                player.EquipItem(spy, false);
                c.Check(player.GetRightItem() == spy, "spyglass back in hand");
                // A tool's click would punch with the fists: the always-on guard skips the attack input.
                player.m_queuedAttackTimer = 0f;
                player.m_attack = true;
                player.PlayerAttackInput(Time.fixedDeltaTime);
                player.m_attack = false;
                c.Check(player.m_queuedAttackTimer <= 0f && !player.InAttack(), "a click with it never punches");
                yield return Frames(5);
                var inst = player.m_visEquipment.m_rightItemInstance;
                c.Check(inst != null && FindDeep(inst.transform, SpyglassModel.ModelName) != null, "hand copy shows the model");
                c.Check(player.m_visEquipment.m_currentRightItemHash == SpyglassContent.ItemHash, "visual hash");
                rig.Look(player.transform.eulerAngles.y + 150f, 5f);
                yield return Frames(30);
                SelfTest.Screenshot(ItemName, "held-front");
                yield return Frames(2);
                rig.Look(player.transform.eulerAngles.y, 0f);
                player.UnequipItem(torch, false);
                yield return Frames(30);
                SelfTest.Screenshot(ItemName, "held-back");
                yield return Frames(2);

                // Out of auto-pickup reach (and auto-pickup off meanwhile), in front of the camera.
                autoPickup = Player.m_enableAutoPickup;
                Player.m_enableAutoPickup = false;
                var at = player.transform.position + player.transform.forward * 3.5f + Vector3.up * 0.5f;
                var dropItem = ItemDrop.DropItem(spy, 1, at, Quaternion.Euler(0f, 30f, 0f));
                dropped = dropItem != null ? dropItem.gameObject : null;
                c.Check(dropped != null, "dropped copy made");
                rig.Look(player.transform.eulerAngles.y, 30f);
                yield return new WaitForSeconds(2f);
                c.Check(dropped != null, "dropped copy still there");
                if (dropped != null)
                {
                    var r = dropped.GetComponentInChildren<MeshRenderer>();
                    c.Check(r != null && r.isVisible, "dropped copy drawn");
                    var fall = at.y - dropped.transform.position.y;
                    c.Check(fall > 0.1f && dropped.transform.position.y > ZoneSystem.instance.GetGroundHeight(dropped.transform.position) - 0.3f,
                        $"dropped copy fell and lies on the ground ({F(dropped.transform.position)})");
                    // Close look (camera parked next to it).
                    var p = dropped.transform.position;
                    ScopeCamera.TestCameraPosition = p + Vector3.up * 0.7f - player.transform.forward * 0.9f;
                    ScopeCamera.TestCameraRotation = Quaternion.LookRotation(p - ScopeCamera.TestCameraPosition);
                    ScopeCamera.TestPoseView = true;
                    yield return Frames(5);
                }
                SelfTest.Screenshot(ItemName, "dropped");
                yield return Frames(2);
                ScopeCamera.TestPoseView = false;
            }
            c.Report();
        }
        finally
        {
            if (dropped != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(dropped);
            }
            if (autoPickup.HasValue)
            {
                Player.m_enableAutoPickup = autoPickup.Value;
            }
            rig.Restore();
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
        {
            return root;
        }
        for (var i = 0; i < root.childCount; i++)
        {
            var f = FindDeep(root.GetChild(i), name);
            if (f != null)
            {
                return f;
            }
        }
        return null;
    }

    // ---------- spyglass.view ----------

    private static IEnumerator RunView()
    {
        var c = new Checks(ViewName);
        var player = Player.m_localPlayer;
        var cam = GameCamera.instance;
        if (player == null || cam == null)
        {
            SelfTest.Fail(ViewName, "no player or camera");
            yield break;
        }
        var rig = new SpyRig(player, ViewName);
        try
        {
            rig.TakeControls();
            rig.Noon();
            var spy = rig.Give(SpyglassContent.ItemName);
            player.EquipItem(spy, false);
            yield return Frames(5);
            var yaw = OpenYaw(player, out var free);
            c.Note($"looking toward yaw {F(yaw)} (free line of sight {F(free)} m)");
            rig.Look(yaw, -1f);
            yield return new WaitForSeconds(1.5f);
            var vanillaFov = cam.m_camera.fieldOfView;
            SelfTest.Screenshot(ViewName, "before");
            yield return Frames(2);

            Scope.TestSetMagnification(4f);
            Scope.TestRequestRaise();
            yield return Frames(2);
            c.Check(Scope.State == ScopeState.Raising, $"raise starts ({Scope.State})");
            c.Check(player.m_nview.GetZDO().GetBool(Scope.RaisedKey), "ZDO flag up for other players");
            yield return new WaitForSeconds(Scope.RaiseSeconds * 0.45f);
            SelfTest.Screenshot(ViewName, "raising");
            yield return WaitFor(() => Scope.State == ScopeState.Raised, 3f);
            c.Check(Scope.State == ScopeState.Raised, "raised");
            yield return Frames(10);
            var expect = ScopeCamera.ZoomedFov(vanillaFov, 4f);
            c.Check(Near(cam.m_camera.fieldOfView, expect, 0.05f), $"zoom x4: fov {F(cam.m_camera.fieldOfView)} (want {F(expect)}, vanilla {F(vanillaFov)})");
            c.Check(Near(cam.m_skyCamera.fieldOfView, expect, 0.05f), "sky camera zoomed too");
            var gap = (cam.transform.position - player.m_eye.position).magnitude;
            c.Check(gap < 0.05f, $"camera at the eye ({F(gap)} m)");
            c.Check(ScopeCamera.BodyHidden && player.m_lodGroup.localReferencePoint.x > 99999f, "own body hidden");
            c.Check(ScopeOverlay.Shown, "overlay shown");
            c.Check(ScopeHud.Hidden && Hud.instance != null && !Hud.instance.m_crosshair.enabled, "crosshair off");
            c.Note("overlay: " + ScopeOverlay.Placement);
            var slot = ViewBoost.Peek();
            if (ScopeCamera.DistantHorizonsSeen)
            {
                c.Check(slot != null && slot[3] > 0.99 && Math.Abs(slot[2] - 4) < 0.01 && Time.frameCount - slot[1] <= 2,
                    "Distant Horizons slot written (zoom 4, full weight)");
                c.Check(ScopeCamera.FogApplied || RenderSettings.fogDensity > 0.006f, $"fog cleared in clear weather (density {RenderSettings.fogDensity:0.#####})");
            }
            else
            {
                c.Note("Distant Horizons not active: no far-view slot, no fog clearing");
            }
            SelfTest.Screenshot(ViewName, "raised-x4");
            yield return Frames(2);

            // Feet locked: walking input does nothing while up.
            var pos = player.transform.position;
            var until = Time.time + 1f;
            while (Time.time < until)
            {
                rig.Drive(Vector3.forward);
                yield return new WaitForFixedUpdate();
            }
            var moved = (player.transform.position - pos).magnitude;
            c.Check(moved < 0.05f, $"walking blocked (moved {F(moved)} m)");
            rig.Drive(Vector3.zero);

            // Aim slowed by the zoom.
            var yaw0 = player.m_lookYaw.eulerAngles.y;
            player.SetMouseLook(new Vector2(8f, 0f));
            var turned = Mathf.DeltaAngle(yaw0, player.m_lookYaw.eulerAngles.y);
            var aim = Plugin.AimSensitivity != null ? Plugin.AimSensitivity.Value : 1f;
            c.Check(Near(turned, 8f * aim / 4f, 0.05f), $"aim slowed: 8 deg input turned {F(turned)} deg");
            player.SetMouseLook(new Vector2(-8f, 0f));

            // Wheel zoom capped by the rules.
            ServerRules.TestRules = new SpyglassRules { MaxMagnification = 6f };
            Scope.TestSetMagnification(20f);
            yield return Frames(3);
            c.Check(Near(Scope.Magnification, 6f, 0.001f), $"zoom capped by the rules ({F(Scope.Magnification)})");
            ServerRules.TestRules = null;
            Scope.TestSetMagnification(8f);
            yield return Frames(5);
            SelfTest.Screenshot(ViewName, "raised-x8");
            yield return Frames(2);

            // Lower: all back.
            Scope.TestRequestLower();
            yield return Frames(2);
            c.Check(Scope.State == ScopeState.Lowering, "lowering starts");
            c.Check(!player.m_nview.GetZDO().GetBool(Scope.RaisedKey), "ZDO flag down");
            yield return WaitFor(() => Scope.State == ScopeState.Idle, 3f);
            yield return Frames(3);
            c.Check(Scope.State == ScopeState.Idle, "lowered");
            c.Check(Near(cam.m_camera.fieldOfView, vanillaFov, 0.01f), $"field of view back ({F(cam.m_camera.fieldOfView)})");
            c.Check(!ScopeCamera.BodyHidden && player.m_lodGroup.localReferencePoint.x < 99999f, "body shown");
            c.Check(!ScopeOverlay.Shown, "overlay hidden");
            c.Check(!ScopeHud.Hidden && Hud.instance.m_crosshair.enabled, "crosshair back");
            slot = ViewBoost.Peek();
            c.Check(slot == null || slot[3] <= 0.0, "Distant Horizons slot cleared");
            c.Check(!ScopeCamera.FogApplied, "fog given back");

            // Unequip while up: instant drop.
            Scope.TestRequestRaise();
            yield return WaitFor(() => Scope.State == ScopeState.Raised, 3f);
            player.UnequipItem(spy, false);
            yield return Frames(2);
            c.Check(Scope.State == ScopeState.Idle && !ScopeOverlay.Shown, "put away while up: view back at once");
            c.Check(!player.m_nview.GetZDO().GetBool(Scope.RaisedKey), "ZDO flag down after put away");

            // Pending rules (client waiting for the server): no raise.
            player.EquipItem(spy, false);
            yield return Frames(3);
            ServerRules.TestPending = true;
            Scope.TestRequestRaise();
            yield return Frames(3);
            c.Check(Scope.State == ScopeState.Idle, "pending rules: stays down");
            ServerRules.TestPending = false;
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Yaw (degrees, 15 steps) with the longest free line of sight at eye height, for far-view screenshots.
    private static float OpenYaw(Player player, out float free)
    {
        var mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
        var eye = player.m_eye.position;
        var best = player.transform.eulerAngles.y;
        free = -1f;
        for (var yaw = 0f; yaw < 360f; yaw += 15f)
        {
            var dir = Quaternion.Euler(-1f, yaw, 0f) * Vector3.forward;
            var d = Physics.Raycast(eye, dir, out var hit, 3000f, mask) ? hit.distance : 3000f;
            if (d > free)
            {
                free = d;
                best = yaw;
            }
        }
        return best;
    }

    // ---------- spyglass.pose ----------

    private static IEnumerator RunPose()
    {
        var c = new Checks(PoseName);
        var player = Player.m_localPlayer;
        if (player == null || GameCamera.instance == null)
        {
            SelfTest.Fail(PoseName, "no player or camera");
            yield break;
        }
        var rig = new SpyRig(player, PoseName);
        try
        {
            rig.TakeControls();
            rig.Noon();
            var spy = rig.Give(SpyglassContent.ItemName);
            player.EquipItem(spy, false);
            rig.Look(player.transform.eulerAngles.y, 0f);
            yield return new WaitForSeconds(0.5f);
            var head = player.m_eye.position;
            var fwd = player.transform.forward;
            var right = player.transform.right;
            ScopeCamera.TestPoseView = true;
            ScopeCamera.TestCameraPosition = head + fwd * 1.5f + right * 0.9f + Vector3.up * 0.05f;
            ScopeCamera.TestCameraRotation = Quaternion.LookRotation(head - ScopeCamera.TestCameraPosition);
            yield return Frames(5);
            SelfTest.Screenshot(PoseName, "down-front");
            yield return Frames(2);
            Scope.TestRequestRaise();
            yield return WaitFor(() => Scope.State == ScopeState.Raised, 3f);
            yield return new WaitForSeconds(0.4f);
            // Pose is written in LateUpdate: measure at the end of the frame (what is drawn).
            yield return new WaitForEndOfFrame();
            c.Note("rig: " + ArmPose.Describe(player) + "; " + ArmPose.EyepieceOffset(player));
            var gap = ArmPose.EyepieceGap(player);
            c.Check(gap >= 0f && gap < 0.12f, $"eyepiece at the eye ({F(gap)} m from the eyes)");
            c.Check(ArmPose.TubeAlignment(player) > 0.97f, $"tube along the look ({F(ArmPose.TubeAlignment(player))})");
            SelfTest.Screenshot(PoseName, "up-front");
            yield return Frames(2);
            ScopeCamera.TestCameraPosition = head + right * 1.6f + Vector3.up * 0.05f;
            ScopeCamera.TestCameraRotation = Quaternion.LookRotation(head - ScopeCamera.TestCameraPosition);
            yield return Frames(5);
            SelfTest.Screenshot(PoseName, "up-side");
            yield return Frames(2);
            // Looking up: pose follows.
            rig.Look(player.transform.eulerAngles.y, -30f);
            yield return new WaitForSeconds(0.5f);
            yield return new WaitForEndOfFrame();
            gap = ArmPose.EyepieceGap(player);
            c.Note("looking up: " + ArmPose.EyepieceOffset(player));
            c.Check(gap >= 0f && gap < 0.12f, $"eyepiece at the eye looking up ({F(gap)} m)");
            c.Check(ArmPose.TubeAlignment(player) > 0.97f, $"tube along the look, up ({F(ArmPose.TubeAlignment(player))})");
            SelfTest.Screenshot(PoseName, "up-side-high");
            yield return Frames(2);

            // Same pose the way other players' games make it: ZDO flag, vanilla head look direction, own blend.
            rig.Look(player.transform.eulerAngles.y, 0f);
            ArmPose.TestAsRemote = true;
            yield return new WaitForSeconds(Scope.RaiseSeconds + 1f);
            yield return new WaitForEndOfFrame();
            gap = ArmPose.EyepieceGap(player);
            c.Note("as another player: " + ArmPose.EyepieceOffset(player));
            c.Check(gap >= 0f && gap < 0.12f, $"other players: eyepiece at the eye ({F(gap)} m)");
            c.Check(ArmPose.TubeAlignment(player) > 0.97f, $"other players: tube along the head look ({F(ArmPose.TubeAlignment(player))})");
            SelfTest.Screenshot(PoseName, "remote-side");
            yield return Frames(2);
            ArmPose.TestAsRemote = false;
            Scope.TestRequestLower();
            yield return WaitFor(() => Scope.State == ScopeState.Idle, 3f);
            yield return Frames(3);
            c.Check(Scope.State == ScopeState.Idle, "lowered");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.far ----------

    // Highest ground within 3 km of origin (WorldGenerator heights, no zone needed; mountains have few trees and no
    // grass in the way) and the yaw whose 3 km line stays most below the eye.
    private static bool FindVantage(Vector3 origin, out Vector3 spot, out float yaw, out float pitch)
    {
        pitch = -2f;
        const float radius = 3000f;
        var gen = WorldGenerator.instance;
        var level = ZoneSystem.instance.m_waterLevel;
        spot = Vector3.zero;
        yaw = 0f;
        var bestH = level + 5f;
        for (var dx = -radius; dx <= radius; dx += 100f)
        {
            for (var dz = -radius; dz <= radius; dz += 100f)
            {
                if (dx * dx + dz * dz > radius * radius)
                {
                    continue;
                }
                var biome = gen.GetBiome(origin.x + dx, origin.z + dz);
                if (biome == Heightmap.Biome.AshLands || biome == Heightmap.Biome.DeepNorth)
                {
                    continue;
                }
                var h = gen.GetHeight(origin.x + dx, origin.z + dz);
                if (h > bestH)
                {
                    bestH = h;
                    spot = new Vector3(origin.x + dx, h, origin.z + dz);
                }
            }
        }
        if (spot == Vector3.zero)
        {
            return false;
        }
        // Stand where the land toward that high point ends (a shore or the foot of the slope) and look at the high
        // point: a big landmark far away, nothing close in the way.
        var summit = spot;
        var dir = summit - origin;
        dir.y = 0f;
        var total = dir.magnitude;
        dir /= Mathf.Max(1f, total);
        var stand = origin;
        for (var d = 20f; d < total - 300f; d += 20f)
        {
            var p = origin + dir * d;
            var h = gen.GetHeight(p.x, p.z);
            if (h < level + 1.5f)
            {
                break;
            }
            stand = new Vector3(p.x, h, p.z);
        }
        stand.y = gen.GetHeight(stand.x, stand.z);
        spot = stand;
        var to = summit - stand;
        yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        var flat = new Vector2(to.x, to.z).magnitude;
        pitch = Mathf.Clamp(Mathf.Atan2(summit.y - (stand.y + 1.8f), Mathf.Max(1f, flat)) * Mathf.Rad2Deg * 0.6f, -20f, 20f);
        return true;
    }

    private static IEnumerator RunFar()
    {
        var c = new Checks(FarName);
        var player = Player.m_localPlayer;
        if (player == null || WorldGenerator.instance == null || ZoneSystem.instance == null || GameCamera.instance == null)
        {
            SelfTest.Fail(FarName, "no player or world");
            yield break;
        }
        var rig = new SpyRig(player, FarName);
        try
        {
            if (!FindVantage(rig.Origin, out var spot, out var yaw, out var pitch))
            {
                c.Note("no high ground near the spawn: far view not tested");
                c.Report(" (no vantage point)");
                yield break;
            }
            c.Note($"vantage {F(spot)} ({F(Vector3.Distance(spot, rig.Origin))} m from the spawn), looking toward yaw {F(yaw)} pitch {F(pitch)}; "
                   + $"Distant Horizons {(FeatureRegistry.Find(ScopeCamera.DistantHorizonsGuid) is FeatureView dh && dh.IsActive ? "on" : "off")}");
            yield return rig.Travel(spot + Vector3.up * 1f, Quaternion.Euler(0f, yaw, 0f), c, 40f);
            rig.TakeControls();
            rig.Noon();
            rig.ClearWeather();
            var spy = rig.Give(SpyglassContent.ItemName);
            player.EquipItem(spy, false);
            // Look pitch: positive = down in vanilla (m_lookPitch); farPitch is an elevation angle.
            rig.Look(yaw, -pitch);
            yield return new WaitForSeconds(4f);
            SelfTest.Screenshot(FarName, "before");
            yield return Frames(2);
            var fogBefore = RenderSettings.fogDensity;
            Scope.TestSetMagnification(8f);
            Scope.TestRequestRaise();
            yield return WaitFor(() => Scope.State == ScopeState.Raised, 3f);
            c.Check(Scope.State == ScopeState.Raised, "raised at the vantage point");
            yield return new WaitForEndOfFrame();
            c.Note($"fog density {RenderSettings.fogDensity:0.######} raised, {fogBefore:0.######} before "
                   + $"(clearing {(ScopeCamera.FogApplied ? "on" : "off")})");
            // Far tiles in the looked-at direction refine (Distant Horizons) while we wait.
            yield return new WaitForSeconds(5f);
            c.Check(Near(ScopeCamera.CurrentMagnification, 8f, 0.01f), $"zoom x8 ({F(ScopeCamera.CurrentMagnification)})");
            SelfTest.Screenshot(FarName, "x8");
            yield return Frames(2);
            Scope.TestSetMagnification(3f);
            yield return new WaitForSeconds(2f);
            SelfTest.Screenshot(FarName, "x3");
            yield return Frames(2);
            Scope.TestRequestLower();
            yield return WaitFor(() => Scope.State == ScopeState.Idle, 3f);
            c.Check(Scope.State == ScopeState.Idle, "lowered");
            yield return rig.GoHome(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.export ----------

    private static IEnumerator RunExport()
    {
        var c = new Checks(ExportName);
        var player = Player.m_localPlayer;
        var probe = SelfTest.ShotPath(ExportName, "probe");
        if (probe != null)
        {
            var dir = Path.GetDirectoryName(probe);
            foreach (var png in SpyglassIcon.ExportPngs())
            {
                var path = Path.Combine(dir, "spyglass-" + png.Key);
                File.WriteAllBytes(path, png.Value);
                c.Note("wrote " + path);
            }
        }
        else
        {
            c.Note("no screenshot folder: icons not written");
        }
        if (player != null)
        {
            var eye = player.m_eye;
            c.Note($"eye parent {(eye.parent != null ? eye.parent.name : "none")} local {F(eye.localPosition)} height {F(eye.position.y - player.transform.position.y)}");
            var animator = player.m_animator;
            if (animator != null)
            {
                c.Note($"animator human {animator.isHuman} update {animator.updateMode} culling {animator.cullingMode} layers {animator.layerCount}");
                if (animator.isHuman)
                {
                    var b = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    var l = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                    var h = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    c.Note($"right arm bones {(b != null ? b.name : "-")}/{(l != null ? l.name : "-")}/{(h != null ? h.name : "-")}");
                }
            }
        }
        var cam = GameCamera.instance;
        if (cam != null)
        {
            c.Note($"camera fov {F(cam.m_fov)} minDistance {F(cam.m_minDistance)} near {F(cam.m_camera.nearClipPlane)} far {F(cam.m_camera.farClipPlane)} hdr {cam.m_camera.allowHDR} path {cam.m_camera.actualRenderingPath}");
        }
        var canvases = new List<string>();
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (canvas.isRootCanvas)
            {
                canvases.Add($"{canvas.name}:{canvas.renderMode}:{canvas.sortingOrder}");
            }
        }
        c.Note("root canvases " + string.Join(", ", canvases.ToArray()));
        c.Check(true, "export");
        c.Report();
        yield break;
    }
#endif
}
