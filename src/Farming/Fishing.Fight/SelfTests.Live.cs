#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.FishingFightMod;

// Debug build only. More live self tests on the rig of SelfTests.cs (FishRig), each at a shore (trip there and back) or
// on dry land where the player stand (no trip, when water change nothing: screen, keys, toggles):
//   fishing.cast      real cast with held Attack (float lands, bait used, line length shown), no fight before a bite,
//                     empty reel give the bait back, wrong-bait nibble message, nibble + Block = "Hooked", then the
//                     fight take over
//   fishing.phases    own phase timer (calm -> fight -> calm, bar off and on, fish and float run to the side, log lines),
//                     side switches of a hard fish (rod verdict follow, arrow flip, splash objects at the fish), turn
//                     asked every tick = once a second, two-star Perch, zone size by Fishing skill
//   fishing.exits     every way out: line break, attack (= new cast), bow in hand, rod put away, float gone with no
//                     tick, world end, hooked fish picked up, and the hooked flag left behind with no float
//   fishing.swim      full bag catch (fish stay in the world), then deep water: swimming put the rod away (Swim Dive on)
//   fishing.toggle    feature off and on through the framework (Plugin.TestBlocked): calm and mid-fight
//   fishing.input     bar settings at once, Toggle block, inventory, game menu pause, hidden HUD (dry land)
//   fishing.dual      rod from a pair of axes, plain cast, Block + Jump = normal dodge (dry land, Dual Wielding and
//                     Weapon Moveset on)
//   fishing.harpoon   tame boar on a harpoon line (the game's effect, put on by the test), then a rod cast lets it go
//                     and a fight works after (dry land, Harpoon Hooks Tames on)
//   fishing.bug.struggle-reel  right-side reel in a fight down to the catch, no stall, no break (T25; failed in the
//                     in-world run: line stood still at 0.9 m), with counts of why no line came in
//   fishing.bug.stranded  fish out of the water: no fight may start, or it take no line and has no wrong side (dry
//                     land; T26; failed in the in-world run: fight start, 1.08 m of line a second, wrong side x4)
// Me read log lines of this mod with a BepInEx listener (LogTap, every level), fake held keys with a Harmony postfix
// on ZInput.GetButton under my own id (HeldKeys, only while a test hold one).
internal static partial class SelfTests
{
    private const string CastName = "fishing.cast";
    private const string PhasesName = "fishing.phases";
    private const string ExitsName = "fishing.exits";
    private const string SwimName = "fishing.swim";
    private const string ToggleName = "fishing.toggle";
    private const string InputName = "fishing.input";
    private const string DualName = "fishing.dual";
    private const string HarpoonName = "fishing.harpoon";
    // Own small tests for what the in-world run showed the mod does not do (TESTING.md T25, T26): they fail until the
    // mod is fixed, the other tests stay green.
    private const string BugReelName = "fishing.bug.struggle-reel";
    private const string StrandedName = "fishing.bug.stranded";

    private const string SwimDiveGuid = "MC.Exploration.Swimming.Dive";
    private const string DualWieldGuid = "MC.Combat.Weapons.DualWield";
    private const string MovesetGuid = "MC.Combat.Weapons.Moveset";
    private const string PickupFilterGuid = "MC.UX.AutoPickup.Filter";
    private const string HarpoonGuid = "MC.Farming.Harpoon.HooksTames";

    // Display settings every live test draw with (BarScale 1, BarOffsetX 260, BarOffsetY 0 = the defaults).
    private static readonly Vector3 DefaultDisplay = new Vector3(1f, 260f, 0f);

    private static void RegisterLive()
    {
        SelfTest.Register(CastName, RunCast);
        SelfTest.Register(PhasesName, RunPhases);
        SelfTest.Register(ExitsName, RunExits);
        SelfTest.Register(SwimName, RunSwim);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(InputName, RunInput);
        SelfTest.Register(DualName, RunDual);
        SelfTest.Register(HarpoonName, RunHarpoon);
        SelfTest.Register(BugReelName, RunBugReel);
        SelfTest.Register(StrandedName, RunStranded);
    }

    private static void UnregisterLive()
    {
        SelfTest.Unregister(CastName);
        SelfTest.Unregister(PhasesName);
        SelfTest.Unregister(ExitsName);
        SelfTest.Unregister(SwimName);
        SelfTest.Unregister(ToggleName);
        SelfTest.Unregister(InputName);
        SelfTest.Unregister(DualName);
        SelfTest.Unregister(HarpoonName);
        SelfTest.Unregister(BugReelName);
        SelfTest.Unregister(StrandedName);
    }

    // ---------- small readers ----------

    private static float Stat(PlayerStatType type)
    {
        var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        return profile != null ? profile.GetStat(type) : 0f;
    }

    // Last message in the middle of the screen.
    private static string CenterText()
    {
        var hud = MessageHud.instance;
        return hud != null && hud.m_messageCenterText != null ? hud.m_messageCenterText.text ?? "" : "";
    }

    private static string Localize(string text) => Localization.instance != null ? Localization.instance.Localize(text) : text;

    // "12m" (the float's line length message) -> 12.
    private static bool Metres(string text, out float metres)
    {
        metres = 0f;
        if (string.IsNullOrEmpty(text) || !text.EndsWith("m", StringComparison.Ordinal))
        {
            return false;
        }
        return int.TryParse(text.Substring(0, text.Length - 1), out var whole) && (metres = whole) >= 0f;
    }

    // Part of the line already in (what the meter shows).
    private static float Progress(Fight fight) =>
        fight.StartLine > 0f ? Mathf.Clamp01(1f - fight.LineLength / fight.StartLine) : 0f;

    private static bool ModActive(string guid)
    {
        var mod = FeatureRegistry.Find(guid);
        return mod != null && mod.Value.IsActive;
    }

    private static string ModStateText(string guid)
    {
        var mod = FeatureRegistry.Find(guid);
        return mod != null ? mod.Value.State : "not installed";
    }

    private static string ModDisplayName(string guid)
    {
        var mod = FeatureRegistry.Find(guid);
        return mod != null ? mod.Value.Name : null;
    }

    // One of my patches (prefix or postfix, feature Harmony id) on this game method.
    private static bool OwnPatchOn(Type type, string method)
    {
        var target = AccessTools.Method(type, method);
        var info = target != null ? Harmony.GetPatchInfo(target) : null;
        if (info == null)
        {
            return false;
        }
        foreach (var patch in info.Prefixes)
        {
            if (patch.owner == ModInfo.Guid)
            {
                return true;
            }
        }
        foreach (var patch in info.Postfixes)
        {
            if (patch.owner == ModInfo.Guid)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsFree(Fish fish)
    {
        if (fish == null || fish.m_nview == null || !fish.m_nview.IsValid())
        {
            return false;
        }
        var zdo = fish.m_nview.GetZDO();
        return !fish.IsHooked() && zdo.GetInt(ZDOVars.s_hooked) == 0 && Near(zdo.GetFloat(ZDOVars.s_escape), 0f)
               && FishingFloat.FindFloat(fish) == null;
    }

    private static void MoveTo(Player p, Vector3 pos)
    {
        p.transform.position = pos;
        if (p.m_body != null)
        {
            p.m_body.position = pos;
            p.m_body.linearVelocity = Vector3.zero;
        }
    }

    // Gone with its ZDO, for every game.
    private static void Kill(GameObject go)
    {
        if (go == null)
        {
            return;
        }
        var nview = go.GetComponent<ZNetView>();
        if (nview != null && nview.IsValid() && ZNetScene.instance != null)
        {
            if (!nview.IsOwner())
            {
                nview.ClaimOwnership();
            }
            ZNetScene.instance.Destroy(go);
        }
        else
        {
            UnityEngine.Object.Destroy(go);
        }
    }

    // ---------- log lines of this mod ----------

    // Me listen to the BepInEx log while a test run: every line of this mod (also Debug ones the file may not keep),
    // and error lines of this mod and of the mods the test name. Log events can come from any thread: lock.
    private sealed class LogTap : ILogListener
    {
        private readonly object _gate = new object();
        private readonly List<string> _own = new List<string>();
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _watch = new List<string>();
        private bool _on;

        internal LogTap(string[] alsoWatch)
        {
            _watch.Add(ModInfo.Name);
            if (alsoWatch != null)
            {
                foreach (var name in alsoWatch)
                {
                    if (!string.IsNullOrEmpty(name) && !_watch.Contains(name))
                    {
                        _watch.Add(name);
                    }
                }
            }
        }

        internal string Watched => string.Join(", ", _watch.ToArray());

        internal void Start()
        {
            lock (_gate)
            {
                _on = true;
            }
            BepInEx.Logging.Logger.Listeners.Add(this);
        }

        internal void Stop()
        {
            lock (_gate)
            {
                _on = false;
            }
            BepInEx.Logging.Logger.Listeners.Remove(this);
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null || eventArgs.Source == null)
                {
                    return;
                }
                var source = eventArgs.Source.SourceName;
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                if (text == null)
                {
                    return;
                }
                lock (_gate)
                {
                    if (!_on)
                    {
                        return;
                    }
                    if (source == ModInfo.Name)
                    {
                        _own.Add(text);
                    }
                    if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 && _watch.Contains(source)
                        && !text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                    {
                        var cut = text.IndexOf('\n');
                        _errors.Add(source + ": " + (cut > 0 ? text.Substring(0, cut).TrimEnd() : text));
                    }
                }
            }
            catch (Exception)
            {
                // Listener must never throw into the logger.
            }
        }

        public void Dispose()
        {
        }

        // Place in the list now: later calls with 'since' only see newer lines.
        internal int Mark()
        {
            lock (_gate)
            {
                return _own.Count;
            }
        }

        internal int Count(string part, int since = 0)
        {
            lock (_gate)
            {
                var n = 0;
                for (var i = Math.Max(0, since); i < _own.Count; i++)
                {
                    if (_own[i].IndexOf(part, StringComparison.Ordinal) >= 0)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        internal bool Has(string part, int since = 0) => Count(part, since) > 0;

        internal string Find(string part, int since = 0)
        {
            lock (_gate)
            {
                for (var i = Math.Max(0, since); i < _own.Count; i++)
                {
                    if (_own[i].IndexOf(part, StringComparison.Ordinal) >= 0)
                    {
                        return _own[i];
                    }
                }
                return null;
            }
        }

        internal List<string> Errors()
        {
            lock (_gate)
            {
                return new List<string>(_errors);
            }
        }
    }

    // ---------- fake held keys ----------

    // Me make ZInput.GetButton say "held" for the buttons a test hold, so the real path from key to Player.SetControls
    // run (PlayerController: inventory open, Toggle block). Own Harmony id; patch on only while a test hold a button
    // (FishRig.Restore take it off).
    private static class HeldKeys
    {
        private static readonly HashSet<string> Held = new HashSet<string>();
        private static Harmony _harmony;

        internal static void Hold(string button)
        {
            if (_harmony == null)
            {
                var harmony = new Harmony(ModInfo.Guid + ".selftest");
                harmony.Patch(AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButton), new[] { typeof(string) }),
                    postfix: new HarmonyMethod(typeof(HeldKeys), nameof(GetButton_Postfix)));
                _harmony = harmony;
            }
            Held.Add(button);
        }

        internal static void Release(string button) => Held.Remove(button);

        internal static void Remove()
        {
            Held.Clear();
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }

        private static void GetButton_Postfix(string name, ref bool __result)
        {
            if (!__result && Held.Count > 0 && name != null && Held.Contains(name))
            {
                __result = true;
            }
        }
    }

    // ---------- the place and the gear ----------

    // Me = where a live test fish: at a shore (float spot 10 m out on the water) or on dry land (float spot 7 m ahead
    // on the ground), and what it fish with.
    private sealed class Stage
    {
        internal bool Found;
        internal bool Ready;
        internal bool Water;
        internal Vector3 Stand;
        internal Vector3 WaterDir;
        internal float Level;
        internal Vector3 Spot;
        internal Vector3 FloatPos;
        internal Vector3 FishPos;
        internal ItemDrop.ItemData Rod;
        internal Transform RodTop;
        internal GameObject FloatPrefab;
        internal GameObject FishPrefab;
        internal GameObject BaitPrefab;
        internal ItemDrop.ItemData Bait;
        internal string FishName;
        internal string BaitName;
    }

    private delegate IEnumerator LiveBody(Checks c, FishRig rig, Stage st, LogTap tap);

    // One live test: the place (shore trip, or dry land with no trip), the gear, the body, clean up, trip back, and
    // "no error logged". No usable place = the test fails (nothing was tested).
    private static IEnumerator RunLive(string name, bool water, LiveBody body, params string[] alsoWatchGuids)
    {
        var c = new Checks(name);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null || WorldGenerator.instance == null || ZNetScene.instance == null
            || ObjectDB.instance == null || EnvMan.instance == null)
        {
            SelfTest.Fail(name, "no local player or world");
            yield break;
        }
        List<KeyValuePair<Vector3, Vector3>> shores = null;
        if (water)
        {
            shores = FindShores(player.transform.position, MaxShores);
            if (shores.Count == 0)
            {
                SelfTest.Fail(name, "no shore with deep water within 4 km of the player: nothing tested");
                yield break;
            }
        }
        var watch = new List<string>();
        if (alsoWatchGuids != null)
        {
            foreach (var guid in alsoWatchGuids)
            {
                watch.Add(ModDisplayName(guid));
            }
        }
        var rig = new FishRig(player, name);
        var tap = new LogTap(watch.ToArray());
        var st = new Stage();
        try
        {
            tap.Start();
            if (water)
            {
                yield return GoToShore(c, rig, shores, st);
            }
            else
            {
                yield return TakeLand(c, rig, st);
            }
            if (st.Found)
            {
                yield return Gear(c, rig, st);
            }
            if (st.Ready)
            {
                yield return body(c, rig, st, tap);
            }
            ClearOverrides();
            Fight.Shutdown();
            rig.DestroySpawned();
            var errors = tap.Errors();
            c.Check(errors.Count == 0,
                $"no error logged by {tap.Watched} during the test ({errors.Count}{(errors.Count > 0 ? ", first: " + errors[0] : "")})");
            if (rig.Travelled)
            {
                var back = new Box();
                yield return rig.Travel(rig.Origin, rig.OriginRotation, back, TravelTimeout);
                rig.Back = back.Ok;
                c.Check(back.Ok, "travel back to the start: " + back.Detail);
            }
        }
        finally
        {
            // Probe time-out or throw: SafeRunner dispose every level, this run (no yield here).
            tap.Stop();
            rig.Restore();
        }
        c.Report();
    }

    // WorldGenerator heights miss rivers, rocks and locations: try the spots in turn until one is real (dry ground
    // under the player, deep water where the float goes).
    private static IEnumerator GoToShore(Checks c, FishRig rig, List<KeyValuePair<Vector3, Vector3>> shores, Stage st)
    {
        var p = rig.P;
        var level = ZoneSystem.instance.m_waterLevel;
        var stand = Vector3.zero;
        var waterDir = Vector3.forward;
        var found = false;
        for (var s = 0; s < shores.Count && !found; s++)
        {
            stand = shores[s].Key;
            waterDir = shores[s].Value;
            var trip = new Box();
            rig.Travelled = true;
            yield return rig.Travel(stand, Quaternion.LookRotation(waterDir), trip, TravelTimeout);
            if (!trip.Ok)
            {
                c.Note($"shore {s + 1} at {F(stand)}: travel failed ({trip.Detail})");
                continue;
            }
            // Let the area settle (water volumes, terrain).
            for (var i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            var spotCheck = p.transform.position + waterDir * 10f;
            var floor = ZoneSystem.instance.GetGroundHeight(spotCheck);
            if (p.IsSwimming() || p.transform.position.y < level + 0.2f || floor > level - 2f)
            {
                c.Note($"shore {s + 1} at {F(stand)} not usable (swimming {p.IsSwimming()}, player y "
                       + $"{F(p.transform.position.y)}, floor 10 m out {F(floor)}, water {F(level)})");
                continue;
            }
            stand = p.transform.position;
            found = true;
            c.Note($"shore {s + 1} at {F(stand)}, water toward {F(waterDir)}, floor 10 m out {F(floor)}, travel "
                   + trip.Detail);
        }
        if (!c.Check(found, $"a usable shore among {shores.Count} candidates"))
        {
            yield break;
        }
        rig.Noon();
        rig.TakeStaminaRate();
        rig.TakeControls();
        rig.Face(waterDir);
        var asked = Time.realtimeSinceStartup;
        while (Game.m_staminaRate < 0.99f && Time.realtimeSinceStartup - asked < 6f)
        {
            yield return null;
        }
        if (!c.Check(Game.m_staminaRate >= 0.99f, $"stamina use is on for the test (world stamina rate {F(Game.m_staminaRate)})"))
        {
            yield break;
        }
        st.Water = true;
        st.Stand = stand;
        st.WaterDir = waterDir;
        st.Level = level;
        st.Spot = stand + waterDir * 10f;
        st.FloatPos = new Vector3(st.Spot.x, level + 0.2f, st.Spot.z);
        st.FishPos = new Vector3(st.Spot.x, level - 1.5f, st.Spot.z);
        st.Found = true;
    }

    // Dry land where the player stand: float spot 7 m ahead on the ground, the Perch next to it (out of the water).
    // For what the water change nothing in (what is on screen, keys, toggles, numbers of the rules).
    private static IEnumerator TakeLand(Checks c, FishRig rig, Stage st)
    {
        var p = rig.P;
        var dir = Flat(p.transform.forward);
        if (dir.sqrMagnitude < 1e-4f)
        {
            dir = Vector3.forward;
        }
        dir.Normalize();
        var stand = p.transform.position;
        var spot = stand + dir * 7f;
        var ground = ZoneSystem.instance.GetGroundHeight(spot);
        var side = Vector3.Cross(Vector3.up, dir);
        rig.TakeStaminaRate();
        rig.TakeControls();
        rig.NoAutoPickup();
        rig.Face(dir);
        // Multiplayer: the server takes the world modifier off and tells us a moment later.
        var asked = Time.realtimeSinceStartup;
        while (Game.m_staminaRate < 0.99f && Time.realtimeSinceStartup - asked < 6f)
        {
            yield return null;
        }
        c.Note($"dry land rig at {F(stand)}: float spot {F(spot)}, ground there {F(ground)}, swimming {p.IsSwimming()}");
        if (!c.Check(Game.m_staminaRate >= 0.99f && !p.IsSwimming(),
                $"stamina use is on for the test and the player stands on land (world stamina rate {F(Game.m_staminaRate)})"))
        {
            yield break;
        }
        st.Water = false;
        st.Stand = stand;
        st.WaterDir = dir;
        st.Level = ground;
        st.Spot = spot;
        st.FloatPos = new Vector3(spot.x, ground + 0.6f, spot.z);
        st.FishPos = new Vector3(spot.x + side.x * 0.6f, ground + 0.4f, spot.z + side.z * 0.6f);
        st.Found = true;
    }

    // Rod in hand (its top shows), float, bait and Fish1 prefabs, Fishing skill 0 for the test.
    private static IEnumerator Gear(Checks c, FishRig rig, Stage st)
    {
        var p = rig.P;
        var rod = rig.Give("FishingRod");
        if (!c.Check(rod != null, "FishingRod given"))
        {
            yield break;
        }
        p.EquipItem(rod, false);
        st.Rod = rod;
        var top = new Box();
        yield return RodInHand(rig, st, top);
        if (!c.Check(top.Ok, "rod top (_RodTop) shows after equipping the rod"))
        {
            yield break;
        }
        var floatPrefab = ZNetScene.instance.GetPrefab("FishingRodFloat");
        if (floatPrefab == null)
        {
            var attack = rod.m_shared.m_attack;
            var projectile = attack != null && attack.m_attackProjectile != null
                ? attack.m_attackProjectile.GetComponent<Projectile>()
                : null;
            floatPrefab = projectile != null ? projectile.m_spawnOnHit : null;
            c.Note("no prefab named FishingRodFloat; float from the rod's projectile: "
                   + (floatPrefab != null ? floatPrefab.name : "none"));
        }
        else
        {
            c.Note("float prefab FishingRodFloat found");
        }
        var baitPrefab = ObjectDB.instance.GetItemPrefab("FishingBait");
        var fishPrefab = ZNetScene.instance.GetPrefab("Fish1");
        if (!c.Check(floatPrefab != null && baitPrefab != null && fishPrefab != null, "float, bait and Fish1 prefabs exist"))
        {
            yield break;
        }
        var fishItem = fishPrefab.GetComponent<ItemDrop>();
        st.FishName = fishItem != null ? fishItem.m_itemData.m_shared.m_name : "$animal_fish1";
        var baitItem = baitPrefab.GetComponent<ItemDrop>();
        st.BaitName = baitItem.m_itemData.m_shared.m_name;
        rig.Remember(st.FishName);
        rig.Remember(st.BaitName);
        var bait = baitItem.m_itemData.Clone();
        bait.m_dropPrefab = baitPrefab;
        st.Bait = bait;
        st.FloatPrefab = floatPrefab;
        st.FishPrefab = fishPrefab;
        st.BaitPrefab = baitPrefab;
        rig.SetFishing(0f);
        st.Ready = true;
    }

    // Wait for the rod's top after an equip (up to 100 frames).
    private static IEnumerator RodInHand(FishRig rig, Stage st, Box result)
    {
        Transform rodTop = null;
        for (var i = 0; i < 100 && rodTop == null; i++)
        {
            yield return null;
            rodTop = Utils.FindChild(rig.P.transform, "_RodTop");
        }
        st.RodTop = rodTop;
        result.Ok = rodTop != null;
    }

    private static FishingFloat SpawnFloat(FishRig rig, Stage st) => SpawnFloat(rig, st, st.FloatPos);

    // Float of this player with no real cast (Instantiate + the game's own Setup: owner, bait name, line length).
    private static FishingFloat SpawnFloat(FishRig rig, Stage st, Vector3 at)
    {
        var go = UnityEngine.Object.Instantiate(st.FloatPrefab, at, Quaternion.identity);
        rig.Track(go);
        var ff = go.GetComponent<FishingFloat>();
        ff.Setup(rig.P, Vector3.zero, 0f, null, st.Rod, st.Bait);
        return ff;
    }

    private static Fish SpawnFish(FishRig rig, Stage st, int quality) => SpawnFish(rig, st, quality, st.FishPos);

    // Perch; quality 3 = two stars, set like the console's "spawn Fish1 1 3".
    private static Fish SpawnFish(FishRig rig, Stage st, int quality, Vector3 at)
    {
        var go = UnityEngine.Object.Instantiate(st.FishPrefab, at, Quaternion.identity);
        rig.Track(go);
        if (quality > 1)
        {
            var drop = go.GetComponent<ItemDrop>();
            if (drop != null)
            {
                drop.SetQuality(quality);
            }
        }
        return go.GetComponent<Fish>();
    }

    private sealed class Hooked
    {
        internal FishingFloat Float;
        internal Fish Fish;
        internal Fight Fight;
    }

    // New float and Perch at the stage spot, hooked with the float's own SetCatch (no nibble). Fight = the fight on
    // them, null when none started (pending rules, feature off).
    private static IEnumerator HookNew(FishRig rig, Stage st, int quality, Hooked hook)
    {
        hook.Fight = null;
        hook.Float = SpawnFloat(rig, st);
        hook.Fish = SpawnFish(rig, st, quality);
        for (var i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        if (hook.Float == null || hook.Fish == null)
        {
            yield break;
        }
        hook.Float.SetCatch(hook.Fish);
        for (var i = 0; i < 5 && (Fight.Current == null || !ReferenceEquals(Fight.Current.Fish, hook.Fish)); i++)
        {
            yield return new WaitForFixedUpdate();
        }
        var fight = Fight.Current;
        if (fight != null && ReferenceEquals(fight.Fish, hook.Fish) && ReferenceEquals(fight.Float, hook.Float))
        {
            hook.Fight = fight;
        }
    }

    // Float and fish of a finished step out of the world (a fight still on them ends by itself: float gone).
    private static void Remove(Hooked hook)
    {
        Kill(hook.Float != null ? hook.Float.gameObject : null);
        Kill(hook.Fish != null ? hook.Fish.gameObject : null);
        hook.Fight = null;
    }

    // ---------- fishing.cast ----------

    private static IEnumerator RunCast() => RunLive(CastName, true, CastBody);

    private sealed class CastResult
    {
        internal FishingFloat Float;
        internal bool Drew;
        internal float Line;
        internal string Shown = "";
        internal string Detail = "";
    }

    // Float of this player in the world (not 'not').
    private static FishingFloat OwnFloat(Player p, FishingFloat not)
    {
        foreach (var ff in FishingFloat.GetAllInstances())
        {
            if (ff != null && !ReferenceEquals(ff, not) && ff.m_nview != null && ff.m_nview.IsValid()
                && ReferenceEquals(ff.GetOwner(), p))
            {
                return ff;
            }
        }
        return null;
    }

    // Real cast like a player: hold Attack (the rod is a draw weapon: longer hold = farther), let go, wait for the new
    // float of this player. Rod not a draw weapon: one Attack press.
    private static IEnumerator Cast(FishRig rig, Stage st, float hold, FishingFloat old, CastResult result)
    {
        var p = rig.P;
        var attack = st.Rod.m_shared.m_attack;
        var draw = attack != null && attack.m_bowDraw;
        // Two ticks with nothing held first: a draw only start from a released Attack.
        rig.Reel(false);
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        var pressed = Time.time;
        do
        {
            p.SetControls(Vector3.zero, true, true, false, false, false, false, false, false, false, false);
            yield return new WaitForFixedUpdate();
            result.Drew |= p.IsDrawingBow();
        }
        while (draw && Time.time - pressed < hold);
        rig.Reel(false);
        var let = Time.time;
        while (Time.time - let < 8f)
        {
            yield return new WaitForFixedUpdate();
            var ff = OwnFloat(p, old);
            if (ff != null)
            {
                result.Float = ff;
                result.Line = ff.m_lineLength;
                result.Shown = CenterText();
                rig.Track(ff.gameObject);
                result.Detail = $"float {F(Time.time - let)} s after letting go, line {F(ff.m_lineLength)} m";
                yield break;
            }
        }
        result.Detail = $"no float within 8 s (draw weapon {draw}, drew {result.Drew})";
    }

    private static IEnumerator CastBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        ServerRules.TestRules = new FightRules();
        FightHud.TestDisplay = DefaultDisplay;
        var given = rig.Give("FishingBait", 5);
        if (!c.Check(given != null, "FishingBait given"))
        {
            yield break;
        }
        var rodAttack = st.Rod.m_shared.m_attack;
        c.Note($"rod: draw weapon {rodAttack != null && rodAttack.m_bowDraw}, ammo '{st.Rod.m_shared.m_ammoType}'; "
               + $"Harpoon Hooks Tames (it patches thrown things) is {ModStateText(HarpoonGuid)}");

        // Cast 1: a float of this player in the water, one bait used, line length shown, nothing of the fight runs.
        var bait0 = rig.Inv.CountItems(st.BaitName);
        var hold = 1f;
        rig.Fill(p.GetMaxStamina());
        var cast = new CastResult();
        yield return Cast(rig, st, hold, null, cast);
        var ff = cast.Float;
        for (var i = 0; i < 10 && ff != null; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        if (ff == null)
        {
            // Too far (the line breaks past its longest) or no float: once more, shorter.
            c.Note("first cast left no float (" + cast.Detail + "); casting again with a shorter hold");
            hold = 0.45f;
            bait0 = rig.Inv.CountItems(st.BaitName);
            rig.Fill(p.GetMaxStamina());
            cast = new CastResult();
            yield return Cast(rig, st, hold, null, cast);
            ff = cast.Float;
        }
        if (!c.Check(ff != null, "cast: a float of this player lands (" + cast.Detail + ")"))
        {
            yield break;
        }
        c.Check(cast.Shown == cast.Line.ToString("0m"),
            $"cast: the line length shows mid-screen (\"{cast.Shown}\", line {F(cast.Line)} m)");
        c.Check(rig.Inv.CountItems(st.BaitName) == bait0 - 1,
            $"cast: one bait used ({bait0} -> {rig.Inv.CountItems(st.BaitName)})");
        var wet = Time.time;
        while (ff != null && !ff.IsInWater() && Time.time - wet < 3f)
        {
            yield return new WaitForFixedUpdate();
        }
        c.Check(ff != null && ff.IsInWater(), "cast: the float sits in the water");
        var took = false;
        var bar = false;
        var idleLine = ff != null ? ff.m_lineLength : 0f;
        var idle = Time.time;
        while (ff != null && Time.time - idle < 0.6f)
        {
            yield return new WaitForFixedUpdate();
            took |= Fight.Current != null;
            bar |= FightHud.Shown;
        }
        c.Check(ff != null && !took && !bar && ff.GetCatch() == null && Near(ff.m_lineLength, idleLine),
            "no bite yet: nothing of the fight runs (no catch bar, line untouched)");

        // Empty reel: the normal game's reel brings the float in and the bait comes back.
        if (ff != null)
        {
            var rodSpeed = FightLogic.ReelSpeed(ff.m_pullLineSpeed, ff.m_pullLineSpeedMaxSkill,
                p.GetSkillFactor(Skills.SkillType.Fishing), 1f);
            var reelFrom = ff.m_lineLength;
            var lastLine = reelFrom;
            var reelStart = Time.time;
            var lastSeen = reelStart;
            rig.Reel(true);
            while (ff != null && Time.time - reelStart < 22f)
            {
                rig.Fill(p.GetMaxStamina());
                yield return new WaitForFixedUpdate();
                took |= Fight.Current != null;
                bar |= FightHud.Shown;
                if (ff != null)
                {
                    lastLine = ff.m_lineLength;
                    lastSeen = Time.time;
                }
            }
            rig.Reel(false);
            yield return null;
            var reelRate = (reelFrom - lastLine) / Mathf.Max(0.01f, lastSeen - reelStart);
            c.Check(ff == null, $"empty reel: the float comes in and is gone ({F(reelFrom)} m in {F(lastSeen - reelStart)} s)");
            c.Check(!took && !bar, "empty reel: the normal game's reel (no fight, no catch bar)");
            c.Check(reelRate > 0.3f && reelRate <= rodSpeed * 1.1f,
                $"empty reel: at the rod's own speed ({F(reelRate)} m/s, rod {F(rodSpeed)} m/s)");
            c.Check(rig.Inv.CountItems(st.BaitName) == bait0,
                $"empty reel: the bait comes back ({rig.Inv.CountItems(st.BaitName)} of {bait0})");
        }

        // Cast 2: wrong-bait nibble, then a nibble and Block.
        rig.Fill(p.GetMaxStamina());
        cast = new CastResult();
        yield return Cast(rig, st, hold, null, cast);
        ff = cast.Float;
        if (!c.Check(ff != null, "second cast: a float lands (" + cast.Detail + ")"))
        {
            yield break;
        }
        wet = Time.time;
        while (ff != null && !ff.IsInWater() && Time.time - wet < 3f)
        {
            yield return new WaitForFixedUpdate();
        }
        if (!c.Check(ff != null && ff.IsInWater(), "second cast: the float sits in the water"))
        {
            yield break;
        }
        var wrongText = Localize("$msg_fishing_wrongbait");
        ff.RPC_Nibble(0L, ZDOID.None, false);
        yield return null;
        c.Check(CenterText() == wrongText && ff != null && ff.GetCatch() == null,
            $"wrong bait nibble: the game says so and nothing is hooked (\"{CenterText()}\", expected \"{wrongText}\")");
        if (ff == null)
        {
            yield break;
        }

        // Three Perch around the float (like "spawn Fish1 3"). A few seconds for one to come by itself, else the test
        // gives the nibble through the float's own nibble call.
        var around = ff.transform.position;
        var fishes = new List<Fish>
        {
            SpawnFish(rig, st, 1, new Vector3(around.x + 1.2f, st.Level - 1.2f, around.z)),
            SpawnFish(rig, st, 1, new Vector3(around.x - 1.2f, st.Level - 1.2f, around.z + 0.8f)),
            SpawnFish(rig, st, 1, new Vector3(around.x, st.Level - 1.2f, around.z - 1.5f)),
        };
        var hookedText = Localize("$msg_fishing_hooked");
        var hookedBefore = Stat(PlayerStatType.FishHooked);
        var nibbleBase = ff.m_nibbleTime;
        var natural = false;
        var watch = Time.time;
        while (ff != null && Time.time - watch < 8f && !natural)
        {
            yield return new WaitForFixedUpdate();
            natural = ff != null && ff.m_nibbler != null && ff.m_nibbleTime > nibbleBase;
        }
        c.Note(natural
            ? $"a Perch swam to the float and nibbled by itself after {F(Time.time - watch)} s"
            : "no Perch nibbled by itself within 8 s: the test gives the nibble");
        Fish caught = null;
        var textAtHook = "";
        var dipped = natural;
        for (var attempt = 0; attempt < 2 && caught == null && ff != null; attempt++)
        {
            if (!natural || attempt > 0)
            {
                if (attempt > 0)
                {
                    // The float takes a new nibble 1 s after the last.
                    yield return new WaitForSeconds(1.2f);
                }
                if (ff == null || fishes[0] == null)
                {
                    break;
                }
                ff.RPC_Nibble(0L, fishes[0].GetZDOID(), true);
            }
            rig.Fill(p.GetMaxStamina());
            rig.Reel(true);
            var press = Time.time;
            while (ff != null && caught == null && Time.time - press < 0.45f)
            {
                yield return new WaitForFixedUpdate();
                if (ff != null)
                {
                    // The nibble pushes the float down on the next physics step.
                    dipped |= ff.m_body.linearVelocity.y < -1f;
                    caught = ff.GetCatch();
                    if (caught != null)
                    {
                        textAtHook = CenterText();
                    }
                }
            }
            rig.Reel(false);
        }
        c.Check(dipped, "nibble: the float dips");
        c.Check(caught != null && caught.IsHooked() && ff != null && ReferenceEquals(ff.GetCatch(), caught),
            "Block right after the nibble: the fish is on the hook");
        c.Check(textAtHook == hookedText, $"the game says \"{hookedText}\" (got \"{textAtHook}\")");
        c.Check(Near(Stat(PlayerStatType.FishHooked), hookedBefore + 1f), "counted as a hooked fish");
        if (caught == null || ff == null)
        {
            yield break;
        }
        for (var i = 0; i < 5 && Fight.Current == null; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        var fight = Fight.Current;
        c.Check(fight != null && ReferenceEquals(fight.Fish, caught) && ReferenceEquals(fight.Float, ff),
            "the fight takes over only now, on the hooked fish");
        yield return null;
        yield return null;
        c.Check(FightHud.BarShown && tap.Has("Fight started: " + Utils.GetPrefabName(caught.gameObject)),
            "after a real cast and hook the catch bar is on screen");
    }

    // ---------- fishing.phases ----------

    private static IEnumerator RunPhases() => RunLive(PhasesName, true, PhasesBody);

    // What the game spawns for a splash of this fish (its jump effects), as scene object names.
    private static List<string> SplashNames(Fish fish)
    {
        var names = new List<string>();
        var effects = fish.m_jumpEffects != null ? fish.m_jumpEffects.m_effectPrefabs : null;
        if (effects != null)
        {
            foreach (var effect in effects)
            {
                if (effect != null && effect.m_enabled && effect.m_prefab != null)
                {
                    names.Add(effect.m_prefab.name + "(Clone)");
                }
            }
        }
        return names;
    }

    private static readonly List<GameObject> Roots = new List<GameObject>();

    // New splash objects at the fish since the last call: its children, or scene roots within 4 m of it.
    private static int NewSplashes(Fish fish, List<string> names, HashSet<int> seen)
    {
        if (names.Count == 0 || fish == null)
        {
            return 0;
        }
        var found = 0;
        var t = fish.transform;
        for (var i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (names.Contains(child.name) && seen.Add(child.gameObject.GetInstanceID()))
            {
                found++;
            }
        }
        var scene = fish.gameObject.scene;
        if (scene.IsValid())
        {
            Roots.Clear();
            scene.GetRootGameObjects(Roots);
            var at = t.position;
            foreach (var go in Roots)
            {
                if (go != null && (go.transform.position - at).sqrMagnitude < 16f && names.Contains(go.name)
                    && seen.Add(go.GetInstanceID()))
                {
                    found++;
                }
            }
            Roots.Clear();
        }
        return found;
    }

    private static IEnumerator PhasesBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        var turn = FightRules.Default.RodAngle + 15f;
        var h = FightHud.TestTrackHeight;
        FightHud.TestDisplay = DefaultDisplay;
        var hook = new Hooked();

        // --- Own phase timer (T06): short calm and fight times, nothing forced. Fish off the bar for free: the line
        // holds while calm.
        var quick = new FightRules { CalmSeconds = FightRules.CalmSecondsMin, StruggleSeconds = 1.5f, OffBarStamina = 0f };
        ServerRules.TestRules = quick;
        Fight.TestPin = FishPin.Away;
        yield return HookNew(rig, st, 1, hook);
        var fight = hook.Fight;
        if (!c.Check(fight != null, "fight starts on the hooked Perch"))
        {
            yield break;
        }
        var ff = hook.Float;
        var fish = hook.Fish;
        var d01 = fight.Profile.D01;
        var calmLow = quick.CalmSeconds * 0.6f * Mathf.Lerp(1.2f, 0.8f, d01);
        var calmHigh = quick.CalmSeconds * 1.4f * Mathf.Lerp(1.2f, 0.8f, d01);
        c.Check(fight.Phase == FightPhase.Calm && fight.PhaseLeft <= calmHigh + 0.01f && fight.PhaseLeft >= calmLow - 0.3f,
            $"the fight starts calm, for the rule's time ({F(fight.PhaseLeft)} s left, rule {F(calmLow)} to {F(calmHigh)} s)");
        var mark = tap.Mark();
        var calmStart = Time.time;
        while (ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Calm && Time.time - calmStart < calmHigh + 1.5f)
        {
            yield return new WaitForFixedUpdate();
        }
        var calmTime = Time.time - calmStart;
        if (!c.Check(ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle && calmTime >= calmLow - 0.4f,
                $"the fish starts to fight by itself after the calm time ({F(calmTime)} s, rule {F(calmLow)} to {F(calmHigh)} s)"))
        {
            yield break;
        }
        var side = fight.Side;
        var fightLow = quick.StruggleSeconds * 0.67f * Mathf.Lerp(0.8f, 1.2f, d01);
        var fightHigh = quick.StruggleSeconds * 1.33f * Mathf.Lerp(0.8f, 1.2f, d01);
        c.Check((side == FightLogic.Right || side == FightLogic.Left) && fight.PhaseLeft <= fightHigh + 0.01f
                && fight.PhaseLeft >= fightLow - 0.1f,
            $"the fight has a side and the rule's length (side {side}, {F(fight.PhaseLeft)} s left, rule {F(fightLow)} to {F(fightHigh)} s)");
        var fightLine = tap.Find("Fight: the fish fights for ", mark);
        c.Check(fightLine != null && fightLine.IndexOf(side > 0 ? "running right" : "running left", StringComparison.Ordinal) >= 0,
            $"Debug line \"Fight: the fish fights for ... s, running {(side > 0 ? "right" : "left")}.\" (got \"{fightLine}\")");
        var zdo = fish.m_nview.GetZDO();
        c.Check(zdo.GetInt(ZDOVars.s_hooked) == 1 && zdo.GetFloat(ZDOVars.s_escape) > 0f && !fish.IsOutOfWater(),
            "fight: hooked and escape keys on with the fish in the water (what makes every game splash at it)");
        var origin = p.transform.position;
        var fishDir0 = Flat(fish.transform.position - origin);
        var floatDir0 = Flat(ff.transform.position - origin);
        var runLine = ff.m_lineLength;
        var struggleStart = Time.time;
        yield return null;
        yield return null;
        var barGone = !FightHud.BarShown && !FightHud.TestBarOnScreen;
        var fishYaw = 0f;
        var floatYaw = 0f;
        var longest = runLine;
        while (ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle
               && Time.time - struggleStart < fightHigh + 1.5f)
        {
            yield return new WaitForFixedUpdate();
            if (fish != null && ff != null)
            {
                // Farthest toward the side (a late turn back must not hide the run).
                fishYaw = Mathf.Max(fishYaw, FightLogic.SignedYaw(fishDir0, Flat(fish.transform.position - origin)) * side);
                floatYaw = Mathf.Max(floatYaw, FightLogic.SignedYaw(floatDir0, Flat(ff.transform.position - origin)) * side);
                longest = Mathf.Max(longest, ff.m_lineLength);
            }
        }
        var struggleTime = Time.time - struggleStart;
        c.Check(barGone, "fight: the catch bar disappears");
        c.Check(fishYaw > 3f, $"fight: the fish runs to its side ({F(fishYaw)} deg around the fisher toward the {(side > 0 ? "right" : "left")})");
        c.Check(floatYaw > 1f, $"fight: the float (and the line to it) moves that way ({F(floatYaw)} deg)");
        c.Note($"fight without reeling: line {F(runLine)} -> {F(longest)} m");
        if (!c.Check(ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Calm
                     && struggleTime <= fightHigh + 0.3f && struggleTime >= fightLow - 0.3f,
                $"the fight ends by itself after its time ({F(struggleTime)} s, rule {F(fightLow)} to {F(fightHigh)} s)"))
        {
            yield break;
        }
        yield return null;
        yield return null;
        c.Check(FightHud.BarShown && FightHud.TestBarOnScreen, "calm again: the catch bar is back");
        c.Check(Near(zdo.GetFloat(ZDOVars.s_escape), 0f), "calm again: escape key off (no more splashes)");
        c.Check(tap.Has("Fight: calm for ", mark), "Debug line \"Fight: calm for ... s.\"");

        // --- Side changes of a hard fish (T10): FishDifficulty 2, long fight pinned, side left to the fish, a high
        // switch rate so several changes fit in the test. Rod kept on the left of the line every tick, no reel.
        Remove(hook);
        yield return null;
        ServerRules.TestRules = new FightRules { FishDifficulty = FightRules.DifficultyMax, StruggleSeconds = FightRules.StruggleSecondsMax };
        Fight.TestPin = null;
        Fight.TestPhase = FightPhase.Struggle;
        Fight.TestSide = 0;
        Fight.TestSwitchRate = 3f;
        FightHud.TestArrow = true;
        yield return HookNew(rig, st, 1, hook);
        fight = hook.Fight;
        ff = hook.Float;
        fish = hook.Fish;
        if (!c.Check(fight != null && Near(fight.Profile.Difficulty, 30f),
                $"FishDifficulty 2: a Perch of difficulty 30 ({(fight != null ? F(fight.Profile.Difficulty) : "no fight")})"))
        {
            yield break;
        }
        var names = SplashNames(fish);
        var seen = new HashSet<int>();
        NewSplashes(fish, names, seen);
        var splashes = 0;
        rig.Reel(false);
        var lastSide = fight.Side;
        var changes = 0;
        var lastChange = -1f;
        var minGap = float.MaxValue;
        var verdictOff = 0;
        var ticks = 0;
        var swingOk = 0;
        var swingBad = 0;
        var watchStart = Time.time;
        while (ReferenceEquals(Fight.Current, fight) && ff != null && fish != null && Time.time - watchStart < 7f)
        {
            var lineDir = Flat(ff.transform.position - p.transform.position);
            rig.Face(Quaternion.Euler(0f, -turn, 0f) * lineDir);
            yield return new WaitForFixedUpdate();
            if (!ReferenceEquals(Fight.Current, fight) || ff == null || fish == null)
            {
                break;
            }
            ticks++;
            if (fight.Side != lastSide)
            {
                if (lastChange >= 0f)
                {
                    minGap = Mathf.Min(minGap, Time.time - lastChange);
                }
                lastChange = Time.time;
                lastSide = fight.Side;
                changes++;
            }
            if (fight.Verdict != (fight.Side == FightLogic.Right ? RodVerdict.Good : RodVerdict.Wrong))
            {
                verdictOff++;
            }
            var since = lastChange >= 0f ? Time.time - lastChange : -1f;
            if (since > 0.7f && since < 0.95f)
            {
                var across = Vector3.Dot(fish.m_body.linearVelocity, FightLogic.RightOf(lineDir)) * fight.Side;
                if (across > 0.5f)
                {
                    swingOk++;
                }
                else
                {
                    swingBad++;
                }
            }
            if (ticks % 5 == 0)
            {
                splashes += NewSplashes(fish, names, seen);
            }
        }
        c.Check(ReferenceEquals(Fight.Current, fight) && ff != null, "the fight is still on after 7 s without reeling");
        c.Check(changes >= 2, $"the fighting fish turns to the other side by itself ({changes} times in 7 s)");
        c.Check(changes < 2 || minGap >= FightLogic.MinSecondsPerSide - 0.03f,
            $"never twice within a second (shortest time on a side {(changes < 2 ? "n/a" : F(minGap) + " s")})");
        // A turn asked by the fish's own swim code (shallows) comes after the float's tick: one stale tick each.
        c.Check(ticks > 100 && verdictOff <= changes,
            $"rod on the left of the line: right while the fish runs right, wrong once it runs left, at every change ({verdictOff} of {ticks} ticks off)");
        c.Check(swingOk >= 5 && swingOk > swingBad * 3,
            $"after a change the fish swims across to its new side ({swingOk} of {swingOk + swingBad} samples)");
        // No more switches while the arrow is read (a switch between the frame and the read would look wrong).
        Fight.TestSwitchRate = 0f;
        yield return new WaitForFixedUpdate();
        yield return null;
        yield return null;
        if (ReferenceEquals(Fight.Current, fight))
        {
            c.Check(FightHud.ArrowShown && FightHud.TestArrowPosition.x * fight.Side < 0f && FightHud.TestArrowScale.x * fight.Side < 0f,
                $"the arrow follows the side (fish side {fight.Side}, arrow at x {F(FightHud.TestArrowPosition.x)})");
        }
        c.Check(names.Count > 0 && splashes > 0,
            $"splashes appear at the fighting fish ({splashes} splash objects in 7 s; the game's splash effects: {string.Join(", ", names.ToArray())})");

        // --- Shallow-water turn asked every tick (T24): the fish turns at most once a second. No own switches, no
        // line taken.
        if (ReferenceEquals(Fight.Current, fight) && ff != null)
        {
            ServerRules.TestRules = new FightRules { FishDifficulty = FightRules.DifficultyMax, LineRunSpeed = 0f };
            Fight.TestSwitchRate = 0f;
            lastSide = fight.Side;
            changes = 0;
            lastChange = -1f;
            minGap = float.MaxValue;
            var askStart = Time.time;
            while (ReferenceEquals(Fight.Current, fight) && ff != null && Time.time - askStart < 3.7f)
            {
                fight.RequestFlip();
                yield return new WaitForFixedUpdate();
                if (fight.Side != lastSide)
                {
                    if (lastChange >= 0f)
                    {
                        minGap = Mathf.Min(minGap, Time.time - lastChange);
                    }
                    lastChange = Time.time;
                    lastSide = fight.Side;
                    changes++;
                }
            }
            c.Check(changes >= 2 && changes <= 4 && minGap >= 0.97f,
                $"a turn asked every tick for 3.7 s (shallows on both sides): the fish turns once a second at most ({changes} turns, shortest gap {(changes < 2 ? "n/a" : F(minGap) + " s")})");
        }

        // --- Two-star Perch and the zone size by skill (T14).
        Remove(hook);
        yield return null;
        Fight.TestSwitchRate = null;
        Fight.TestSide = FightLogic.Right;
        FightHud.TestArrow = null;
        ServerRules.TestRules = new FightRules { OffBarStamina = 0f };
        Fight.TestPhase = FightPhase.Calm;
        Fight.TestPin = FishPin.Away;
        mark = tap.Mark();
        yield return HookNew(rig, st, 3, hook);
        fight = hook.Fight;
        if (c.Check(fight != null, "fight starts on a two-star Perch"))
        {
            c.Check(fight.Quality == 3 && Near(fight.Profile.Difficulty, 31f)
                    && tap.Has("Fight started: Fish1 quality 3, difficulty 31 (Mixed)", mark),
                $"two-star Perch: quality 3, difficulty 31, and the Debug line says so (quality {fight.Quality}, difficulty {F(fight.Profile.Difficulty)})");
            rig.SetFishing(0f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;
            var zone0 = fight.Bar.ZoneSize;
            var zoneDrawn0 = FightHud.TestZoneHeight;
            rig.SetFishing(100f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;
            var zone100 = fight.Bar.ZoneSize;
            var zoneDrawn100 = FightHud.TestZoneHeight;
            rig.SetFishing(0f);
            c.Check(Near(zone0, 0.24f, 0.001f) && Near(zone100, 0.38f, 0.001f) && Near(zoneDrawn0, 0.24f * h, 1f)
                    && Near(zoneDrawn100, 0.38f * h, 1f),
                $"the zone is bigger at Fishing 100 than at 0, also on screen (24 % -> 38 %: {F(zone0)} -> {F(zone100)}, drawn {F(zoneDrawn0)} -> {F(zoneDrawn100)} of {F(h)})");
            // The marker of this live fight moves by its difficulty (fishing.logic: a 31 marker travels more than a 15).
            c.Check(Near(fight.Bar.Difficulty, 31f, 0.01f),
                $"two-star Perch: its catch bar marker moves with difficulty 31 (bar difficulty {F(fight.Bar.Difficulty)})");
        }
    }

    // ---------- fishing.bug.struggle-reel ----------

    private static IEnumerator RunBugReel() => RunLive(BugReelName, true, BugReelBody);

    // Right-side reel in a fight down to the catch (T25): first a quick calm reel to about 6 m, then the fight with
    // the default reel speed, rod kept on the right side. Own test: in the in-world run of 2026-10-07 the line came
    // in at a third of the reel speed and stood still at 0.9 m for the last 10 s (no catch). Me count why no line
    // came in on each tick (rod judged on the wrong side, or float dragged past the line: the two things that stop
    // the reel in Fight.Step), so the next run say which. Then the rod held still 2 s (player who stop turning).
    private static IEnumerator BugReelBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        var turn = FightRules.Default.RodAngle + 15f;
        FightHud.TestDisplay = DefaultDisplay;
        var hook = new Hooked();
        ServerRules.TestRules = new FightRules { ReelSpeed = FightRules.ReelSpeedMax };
        Fight.TestPhase = FightPhase.Calm;
        Fight.TestPin = FishPin.InZone;
        yield return HookNew(rig, st, 1, hook);
        var fight = hook.Fight;
        var ff = hook.Float;
        var fish = hook.Fish;
        if (!c.Check(fight != null, "fight starts for the reel in a fight"))
        {
            yield break;
        }
        var shorten = Time.time;
        while (ff != null && ff.m_lineLength > 6f && Time.time - shorten < 5f)
        {
            yield return new WaitForFixedUpdate();
        }
        if (!c.Check(ff != null && ff.m_lineLength <= 6.1f, "calm reel to about 6 m of line"))
        {
            yield break;
        }
        ServerRules.TestRules = new FightRules();
        Fight.TestPin = null;
        Fight.TestSide = FightLogic.Right;
        Fight.TestPhase = FightPhase.Struggle;
        var fishBefore = rig.Inv.CountItems(st.FishName);
        var mark = tap.Mark();
        var from = ff.m_lineLength;
        var best = from;
        var bestAt = Time.time;
        var stall = 0f;
        var stallFar = 0f;
        var reelStart = Time.time;
        var first = true;
        var last = from;
        var ticks = 0;
        var inTicks = 0;
        var wrongTicks = 0;
        var draggedTicks = 0;
        var otherTicks = 0;
        var worstPast = 0f;
        var turned = 0f;
        var at4 = -1f;
        var at2 = -1f;
        var at1 = -1f;
        rig.Reel(true);
        while (ff != null && Time.time - reelStart < 25f)
        {
            rig.Fill(p.GetMaxStamina());
            // Like a player: turn again only when the rod is no longer well on the right side (not every tick).
            var toFloat = Flat(ff.transform.position - p.transform.position);
            var rodYaw = FightLogic.SignedYaw(toFloat, p.transform.forward);
            if (first || rodYaw > -(FightRules.Default.RodAngle + 5f) || rodYaw < -100f)
            {
                var want = Quaternion.Euler(0f, -turn, 0f) * toFloat;
                if (!first)
                {
                    turned += Mathf.Abs(FightLogic.SignedYaw(p.transform.forward, want));
                }
                rig.Face(want);
                first = false;
            }
            yield return new WaitForFixedUpdate();
            if (ff == null)
            {
                break;
            }
            // Why this tick brought no line (read after the tick: the float moved one step since, so "about").
            ticks++;
            var line = ff.m_lineLength;
            var past = PastLine(st, ff);
            worstPast = Mathf.Max(worstPast, past);
            if (line < last - 0.0001f)
            {
                inTicks++;
            }
            else if (fight.Verdict == RodVerdict.Wrong)
            {
                wrongTicks++;
            }
            else if (past >= FightLogic.StruggleDrag)
            {
                draggedTicks++;
            }
            else
            {
                otherTicks++;
            }
            last = line;
            var since = Time.time - reelStart;
            if (at4 < 0f && line <= 4f)
            {
                at4 = since;
            }
            if (at2 < 0f && line <= 2f)
            {
                at2 = since;
            }
            if (at1 < 0f && line <= 1f)
            {
                at1 = since;
            }
            if (line < best - 0.02f)
            {
                best = line;
                bestAt = Time.time;
            }
            stall = Mathf.Max(stall, Time.time - bestAt);
            if (line > 2f)
            {
                stallFar = Mathf.Max(stallFar, Time.time - bestAt);
            }
            if (Time.time - bestAt > 6f)
            {
                // Stands still for good: nothing more to learn by waiting.
                break;
            }
        }
        var reelTime = Time.time - reelStart;
        c.Note($"reel in a fight: line {F(from)} -> {F(best)} m in {F(reelTime)} s (4 m after {Seconds(at4)}, 2 m after "
               + $"{Seconds(at2)}, 1 m after {Seconds(at1)}); of {ticks} ticks the line came in on {inTicks}; no line on "
               + $"{wrongTicks} ticks with the rod judged on the wrong side, on {draggedTicks} with the float about "
               + $"{F(FightLogic.StruggleDrag)} m or more past the line (farthest {F(worstPast)} m), on {otherTicks} for "
               + $"neither; the fisher turned {F(turned)} deg in all");
        if (ff != null)
        {
            var top = st.RodTop;
            c.Note($"where the line stands: float {F(Flat(ff.transform.position - p.transform.position).magnitude)} m from "
                   + $"the fisher (flat), {F(ff.transform.position.y - st.Level)} m above the water, "
                   + (top != null ? $"{F(Vector3.Distance(top.position, ff.transform.position))} m from the rod top, " : "")
                   + $"line {F(ff.m_lineLength)} m; fish out of the water: "
                   + (fish != null ? fish.IsOutOfWater().ToString() : "gone"));
        }

        // Still on the hook: the rod held where it is for 2 s (a player who stops turning), Block still held.
        if (ff != null && ReferenceEquals(Fight.Current, fight))
        {
            var holdLine = ff.m_lineLength;
            var holdTicks = 0;
            var holdWrong = 0;
            var holdDragged = 0;
            var holdStart = Time.time;
            while (ff != null && Time.time - holdStart < 2f)
            {
                rig.Fill(p.GetMaxStamina());
                yield return new WaitForFixedUpdate();
                if (ff == null)
                {
                    break;
                }
                holdTicks++;
                if (fight.Verdict == RodVerdict.Wrong)
                {
                    holdWrong++;
                }
                else if (PastLine(st, ff) >= FightLogic.StruggleDrag)
                {
                    holdDragged++;
                }
            }
            c.Note($"rod then held still for 2 s with Block: wrong side on {holdWrong} of {holdTicks} ticks, float dragged "
                   + $"past the line on {holdDragged}, line {F(holdLine)} -> {(ff != null ? F(ff.m_lineLength) + " m" : "caught")}");
        }
        rig.Reel(false);
        yield return null;
        var broke = tap.Has("the float was pulled too far", mark) || tap.Has("the fish took all the line", mark);
        c.Check(ff == null && tap.Has("Fight ended: caught.", mark) && rig.Inv.CountItems(st.FishName) == fishBefore + 1,
            $"right-side reel in a fight from {F(from)} m: the fish is caught ({F(reelTime)} s, last line {F(best)} m)");
        c.Check(stallFar <= 1f,
            $"with more than 2 m of line left the line keeps coming in (longest time with no line in {F(stallFar)} s)");
        c.Check(stall <= 1f,
            $"the line keeps coming in while the fish pulls the float sideways (longest time with no line in {F(stall)} s)");
        c.Check(!broke, "the line does not break");
    }

    // How far the float is past the line's end (m): rod top to float, minus the line. Fight.Step reels only while
    // this is under FightLogic.StruggleDrag in a fight.
    private static float PastLine(Stage st, FishingFloat ff)
    {
        var top = st.RodTop;
        return top != null && ff != null ? Vector3.Distance(top.position, ff.transform.position) - ff.m_lineLength : 0f;
    }

    private static string Seconds(float t) => t < 0f ? "never" : F(t) + " s";

    // ---------- fishing.exits ----------

    private static IEnumerator RunExits() => RunLive(ExitsName, true, ExitsBody);

    private static IEnumerator ExitsBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        FightHud.TestDisplay = DefaultDisplay;
        var hook = new Hooked();

        // --- Line break (T09): FishDifficulty 2, LineRunSpeed 5, long fight, no reel.
        var brokeText = Localize("$msg_fishing_linebroke");
        ServerRules.TestRules = new FightRules
        {
            FishDifficulty = FightRules.DifficultyMax, LineRunSpeed = FightRules.LineRunSpeedMax,
            StruggleSeconds = FightRules.StruggleSecondsMax,
        };
        Fight.TestPhase = FightPhase.Struggle;
        Fight.TestSide = FightLogic.Right;
        var mark = tap.Mark();
        yield return HookNew(rig, st, 1, hook);
        if (c.Check(hook.Fight != null, "line break: fight starts"))
        {
            var ff = hook.Float;
            var fish = hook.Fish;
            var maxLine = ff.m_maxDistance;
            var longest = ff.m_lineLength;
            var start = Time.time;
            rig.Reel(false);
            while (ff != null && Time.time - start < 15f)
            {
                yield return new WaitForFixedUpdate();
                if (ff != null)
                {
                    longest = Mathf.Max(longest, ff.m_lineLength);
                }
            }
            var text = CenterText();
            yield return null;
            c.Check(ff == null && longest > maxLine - 3f,
                $"not reeling: the fish takes the line to its longest ({F(maxLine)} m) and the float is gone (longest {F(longest)} m, {F(Time.time - start)} s)");
            c.Check(text == brokeText, $"line broke: the game says so (\"{text}\", expected \"{brokeText}\")");
            c.Check(tap.Has("Fight ended: the fish took all the line.", mark) || tap.Has("Fight ended: the float was pulled too far.", mark),
                "Debug line \"Fight ended: the fish took all the line.\" (or \"... pulled too far.\")");
            c.Check(Fight.Current == null && !FightHud.Shown, "line broke: fight over, nothing on screen");
            c.Check(IsFree(fish), "line broke: the fish is free (not hooked, no hooked flag, no escape time)");
            if (fish != null)
            {
                // "The fish swims off": free and moving away from where the line broke (up to 4 s to get going).
                var at = fish.transform.position;
                var swam = 0f;
                var freed = Time.time;
                while (fish != null && swam <= 0.3f && Time.time - freed < 4f)
                {
                    yield return new WaitForFixedUpdate();
                    if (fish != null)
                    {
                        swam = Vector3.Distance(at, fish.transform.position);
                    }
                }
                c.Check(fish != null && swam > 0.3f && IsFree(fish),
                    fish != null
                        ? $"line broke: the freed fish swims off ({F(swam)} m in {F(Time.time - freed)} s)"
                        : "line broke: the freed fish swims off (it is gone from the world)");
            }
        }
        Remove(hook);
        yield return null;

        // --- Attack with the rod while a fish is hooked = a new cast (T15).
        ServerRules.TestRules = new FightRules { OffBarStamina = 0f };
        Fight.TestPhase = FightPhase.Calm;
        Fight.TestPin = FishPin.Away;
        var baitGiven = rig.Give("FishingBait", 5);
        c.Check(baitGiven != null, "FishingBait given");
        yield return HookNew(rig, st, 1, hook);
        if (c.Check(hook.Fight != null, "attack: fight starts"))
        {
            var ff = hook.Float;
            var fish = hook.Fish;
            yield return null;
            yield return null;
            var barBefore = FightHud.BarShown;
            var baitBefore = rig.Inv.CountItems(st.BaitName);
            mark = tap.Mark();
            rig.Fill(p.GetMaxStamina());
            var cast = new CastResult();
            yield return Cast(rig, st, 0.8f, ff, cast);
            yield return null;
            c.Check(barBefore && ff == null && Fight.Current == null && !FightHud.Shown,
                "attack with a fish hooked: the fishing ends, the bar and the hooked float are gone");
            c.Check(tap.Has("Fight ended: the fisher attacked.", mark), "Debug line \"Fight ended: the fisher attacked.\"");
            c.Check(IsFree(fish), "attack: the fish is free");
            c.Check(cast.Float != null && cast.Float.GetCatch() == null,
                "the attack with the rod is a cast: a new, empty float (" + cast.Detail + ")");
            c.Check(rig.Inv.CountItems(st.BaitName) == baitBefore - 1,
                $"the new cast uses one bait, none comes back from the hooked float ({baitBefore} -> {rig.Inv.CountItems(st.BaitName)})");
            Kill(cast.Float != null ? cast.Float.gameObject : null);
            var swing = Time.time;
            while (p.InAttack() && Time.time - swing < 4f)
            {
                yield return new WaitForFixedUpdate();
            }
        }
        Remove(hook);
        yield return null;

        // --- Switch to a bow (T15): the rod leaves the hand.
        var bow = rig.Give("Bow");
        rig.Give("ArrowWood", 5);
        if (c.Check(bow != null, "Bow given"))
        {
            yield return HookNew(rig, st, 1, hook);
            if (c.Check(hook.Fight != null, "bow: fight starts"))
            {
                var ff = hook.Float;
                var fish = hook.Fish;
                mark = tap.Mark();
                p.EquipItem(bow, false);
                var start = Time.time;
                while (ff != null && Time.time - start < 3f)
                {
                    yield return new WaitForFixedUpdate();
                }
                yield return null;
                c.Check(ff == null && Fight.Current == null && !FightHud.Shown && !ReferenceEquals(p.GetCurrentWeapon(), st.Rod),
                    "bow in hand: the fishing ends as in the normal game, bar and float gone");
                c.Check(tap.Has("Fight ended: the rod is gone.", mark), "Debug line \"Fight ended: the rod is gone.\" (bow)");
                c.Check(IsFree(fish), "bow: the fish is free");
            }
            p.UnequipItem(bow, false);
            p.EquipItem(st.Rod, false);
            var top = new Box();
            yield return RodInHand(rig, st, top);
            c.Check(top.Ok, "rod back in hand after the bow");
        }
        Remove(hook);
        yield return null;

        // --- Rod put away (T15).
        yield return HookNew(rig, st, 1, hook);
        if (c.Check(hook.Fight != null, "rod away: fight starts"))
        {
            var ff = hook.Float;
            var fish = hook.Fish;
            mark = tap.Mark();
            p.HideHandItems();
            var start = Time.time;
            while (ff != null && Time.time - start < 3f)
            {
                yield return new WaitForFixedUpdate();
            }
            yield return null;
            c.Check(ff == null && Fight.Current == null && !FightHud.Shown,
                "rod put away: the fishing ends as in the normal game, bar and float gone");
            c.Check(tap.Has("Fight ended: the rod is gone.", mark), "Debug line \"Fight ended: the rod is gone.\" (put away)");
            c.Check(IsFree(fish), "rod put away: the fish is free");
            p.ShowHandItems();
            var top = new Box();
            yield return RodInHand(rig, st, top);
            c.Check(top.Ok, "rod back in hand");
        }
        Remove(hook);
        yield return null;

        // --- Float gone with no tick of the fight (logout, area unload; T23): calm, then mid-fight with the arrow.
        for (var round = 0; round < 2; round++)
        {
            var fighting = round == 1;
            var what = fighting ? "float gone mid-fight" : "float gone in the calm phase";
            Fight.TestPhase = fighting ? FightPhase.Struggle : FightPhase.Calm;
            Fight.TestSide = FightLogic.Right;
            FightHud.TestArrow = fighting ? true : (bool?)null;
            yield return HookNew(rig, st, 1, hook);
            if (!c.Check(hook.Fight != null, what + ": fight starts"))
            {
                continue;
            }
            yield return new WaitForSeconds(fighting ? 0.6f : 0.2f);
            yield return null;
            yield return null;
            var shownBefore = fighting ? FightHud.ArrowShown : FightHud.BarShown;
            mark = tap.Mark();
            ZNetScene.instance.Destroy(hook.Float.gameObject);
            yield return null;
            yield return null;
            yield return null;
            c.Check(shownBefore && Fight.Current == null && !FightHud.Shown && !FightHud.BarShown && !FightHud.ArrowShown,
                what + ": the fight ends by itself, no bar or arrow left on screen");
            c.Check(tap.Has("Fight ended: its float or fish is gone.", mark), what + ": Debug line \"Fight ended: its float or fish is gone.\"");
            c.Check(IsFree(hook.Fish), what + ": the fish is let go");
            Remove(hook);
            yield return null;
        }
        FightHud.TestArrow = null;

        // --- World end (logout): the hook on it ends a fight still going.
        c.Check(OwnPatchOn(typeof(ZNet), nameof(ZNet.OnDestroy)), "the world-end hook is in place (ZNet.OnDestroy)");
        Fight.TestPhase = FightPhase.Calm;
        yield return HookNew(rig, st, 1, hook);
        if (c.Check(hook.Fight != null, "world end: fight starts"))
        {
            mark = tap.Mark();
            Fight.Shutdown();
            c.Check(Fight.Current == null && tap.Has("Fight ended: the mod was turned off.", mark),
                "world end (what the hook runs): the fight ends, Debug line \"Fight ended: the mod was turned off.\"");
        }
        Remove(hook);
        yield return null;

        // --- Hooked fish picked up mid-fight, the float stays (T27). Quick calm reel to about 4 m first.
        ServerRules.TestRules = new FightRules { ReelSpeed = FightRules.ReelSpeedMax, OffBarStamina = 0f };
        Fight.TestPhase = FightPhase.Calm;
        Fight.TestPin = FishPin.InZone;
        yield return HookNew(rig, st, 1, hook);
        if (c.Check(hook.Fight != null, "pickup: fight starts"))
        {
            var ff = hook.Float;
            var fish = hook.Fish;
            var start = Time.time;
            while (ff != null && ff.m_lineLength > 4f && Time.time - start < 5f)
            {
                yield return new WaitForFixedUpdate();
            }
            Fight.TestPin = FishPin.Away;
            ServerRules.TestRules = new FightRules { OffBarStamina = 0f };
            yield return new WaitForSeconds(0.5f);
            yield return null;
            var barBefore = FightHud.BarShown;
            var fishBefore = rig.Inv.CountItems(st.FishName);
            var baitBefore = rig.Inv.CountItems(st.BaitName);
            var caughtText = Localize("$msg_fishing_catched " + fish.GetHoverName());
            var started = tap.Count("Fight started:");
            mark = tap.Mark();
            var picked = ff != null && fish != null && fish.Pickup(p);
            for (var i = 0; i < 3; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            yield return null;
            yield return null;
            c.Check(picked && rig.Inv.CountItems(st.FishName) == fishBefore + 1,
                $"hooked fish picked up: it is in the inventory ({fishBefore} -> {rig.Inv.CountItems(st.FishName)})");
            c.Check(barBefore && Fight.Current == null && !FightHud.Shown, "fish gone: the fight ends and the bar goes");
            c.Check(tap.Has("Fight ended: its float or fish is gone.", mark) || tap.Has("Fight ended: the fish is gone.", mark),
                "Debug line \"Fight ended: its float or fish is gone.\" (or \"... the fish is gone.\")");
            c.Check(!tap.Has("Fight ended: caught.", mark) && !CenterText().StartsWith(caughtText, StringComparison.Ordinal),
                $"a pickup, not a catch: no catch message (\"{CenterText()}\")");
            c.Check(ff != null && ff.GetCatch() == null, "the float stays in the water, empty");
            yield return new WaitForSeconds(1f);
            c.Check(Fight.Current == null && tap.Count("Fight started:") == started, "no new fight on the empty float");
            if (ff != null)
            {
                start = Time.time;
                rig.Reel(true);
                while (ff != null && Time.time - start < 12f)
                {
                    rig.Fill(p.GetMaxStamina());
                    yield return new WaitForFixedUpdate();
                }
                rig.Reel(false);
                yield return null;
                c.Check(ff == null, $"Block reels the empty float in the normal game's way ({F(Time.time - start)} s)");
                c.Check(rig.Inv.CountItems(st.BaitName) == baitBefore,
                    $"no bait comes back, it went with the bite ({baitBefore} -> {rig.Inv.CountItems(st.BaitName)})");
            }
        }
        Remove(hook);
        yield return null;

        // --- Hooked flag left behind with no float (its fisher left mid-fight, M04): cleared by a game with the mod.
        Fight.TestPhase = null;
        Fight.TestPin = null;
        var lone = SpawnFish(rig, st, 1);
        for (var i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        if (c.Check(lone != null && lone.m_nview != null && lone.m_nview.IsValid() && lone.m_nview.IsOwner(), "a free Perch run by this game"))
        {
            var loneZdo = lone.m_nview.GetZDO();
            loneZdo.Set(ZDOVars.s_hooked, 1);
            loneZdo.Set(ZDOVars.s_escape, 2.5f);
            for (var i = 0; i < 3; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            c.Check(loneZdo.GetInt(ZDOVars.s_hooked) == 0 && Near(loneZdo.GetFloat(ZDOVars.s_escape), 0f),
                $"a fish left with the hooked flag and no float: the flag and the escape time are cleared, so it stops splashing (hooked {loneZdo.GetInt(ZDOVars.s_hooked)}, escape {F(loneZdo.GetFloat(ZDOVars.s_escape))})");
        }
    }

    // ---------- fishing.swim ----------

    private static IEnumerator RunSwim() => RunLive(SwimName, true, SwimBody, SwimDiveGuid, PickupFilterGuid);

    private static IEnumerator SwimBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        FightHud.TestDisplay = DefaultDisplay;
        var hook = new Hooked();

        // --- Full bag (X05): the catch happens, the fish stays in the world where it was pulled in.
        c.Note($"Loot Pickup Filter is {ModStateText(PickupFilterGuid)}");
        var fillers = new List<ItemDrop.ItemData>();
        for (var i = 0; i < 64 && rig.Inv.HaveEmptySlot(); i++)
        {
            var filler = rig.Give("FishingRod");
            if (filler == null)
            {
                break;
            }
            fillers.Add(filler);
        }
        ServerRules.TestRules = new FightRules { ReelSpeed = FightRules.ReelSpeedMax };
        Fight.TestPhase = FightPhase.Calm;
        Fight.TestPin = FishPin.InZone;
        var mark = tap.Mark();
        yield return HookNew(rig, st, 1, hook);
        if (c.Check(hook.Fight != null, "full bag: fight starts"))
        {
            var ff = hook.Float;
            var fish = hook.Fish;
            var full = !rig.Inv.HaveEmptySlot() && fish.m_itemDrop != null && !rig.Inv.CanAddItem(fish.m_itemDrop.m_itemData);
            var before = rig.Inv.CountItems(st.FishName);
            var start = Time.time;
            while (ff != null && Time.time - start < 25f)
            {
                yield return new WaitForFixedUpdate();
            }
            yield return null;
            yield return null;
            c.Check(full, $"the inventory is full before the catch ({fillers.Count} slots filled)");
            c.Check(ff == null && Fight.Current == null && !FightHud.Shown && tap.Has("Fight ended: caught.", mark),
                $"full bag: the reel ends like a catch, float and bar gone ({F(Time.time - start)} s)");
            c.Check(rig.Inv.CountItems(st.FishName) == before, "full bag: nothing goes into the inventory");
            c.Check(fish != null && IsFree(fish), "full bag: the fish stays in the world, off the hook");
            if (fish != null)
            {
                var away = Vector3.Distance(Flat(fish.transform.position), Flat(p.transform.position));
                c.Check(away < 6f, $"full bag: the fish is where it was pulled in ({F(away)} m from the fisher)");
            }
        }
        Remove(hook);
        foreach (var filler in fillers)
        {
            if (rig.Inv.ContainsItem(filler))
            {
                rig.Inv.RemoveItem(filler);
            }
        }
        yield return null;

        // --- Deep water (X01): swimming puts the rod away as in the normal game, the fishing ends.
        ServerRules.TestRules = new FightRules { OffBarStamina = 0f };
        Fight.TestPhase = FightPhase.Calm;
        Fight.TestPin = FishPin.Away;
        yield return HookNew(rig, st, 1, hook);
        if (c.Check(hook.Fight != null, "deep water: fight starts"))
        {
            var ff = hook.Float;
            var fish = hook.Fish;
            yield return null;
            yield return null;
            var barBefore = FightHud.BarShown;
            mark = tap.Mark();
            var deep = st.Stand + st.WaterDir * 14f;
            deep.y = st.Level - 0.3f;
            rig.Fill(p.GetMaxStamina());
            MoveTo(p, deep);
            var swam = false;
            var start = Time.time;
            while (ff != null && Time.time - start < 6f)
            {
                yield return new WaitForFixedUpdate();
                swam |= p.IsSwimming() && !p.IsOnGround();
            }
            yield return null;
            var rodAway = Utils.FindChild(p.transform, "_RodTop") == null;
            c.Check(ModActive(SwimDiveGuid), $"Swim Dive is active in this run ({ModStateText(SwimDiveGuid)})");
            c.Check(swam, "the fisher swims in deep water");
            c.Check(barBefore && ff == null && Fight.Current == null && !FightHud.Shown,
                $"swimming: the fishing ends, bar and float gone ({F(Time.time - start)} s after entering the water)");
            c.Check(rodAway && tap.Has("Fight ended: the rod is gone.", mark),
                "swimming: the rod is put away as in the normal game (Debug line \"Fight ended: the rod is gone.\")");
            c.Check(IsFree(fish), "swimming: the fish is free");
            MoveTo(p, st.Stand + Vector3.up * 0.3f);
            start = Time.time;
            while ((p.IsSwimming() || !p.IsOnGround()) && Time.time - start < 5f)
            {
                yield return new WaitForFixedUpdate();
            }
            c.Check(!p.IsSwimming() && p.IsOnGround(), "back on the shore");
        }
    }

    // ---------- fishing.toggle ----------

    private static IEnumerator RunToggle() => RunLive(ToggleName, true, ToggleBody);

    // Calm, fish off the bar for free: a fight that just stands there.
    private static void QuietCalm()
    {
        ServerRules.TestRules = new FightRules { OffBarStamina = 0f };
        Fight.TestPhase = FightPhase.Calm;
        Fight.TestPin = FishPin.Away;
        FightHud.TestDisplay = DefaultDisplay;
    }

    private static void Block(FishRig rig, bool blocked)
    {
        Plugin.TestBlocked = blocked;
        rig.Blocked = blocked;
        FeatureRegistry.RefreshAll();
    }

    private static IEnumerator ToggleBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        var hook = new Hooked();
        var found = FeatureRegistry.Find(ModInfo.Guid);
        if (!c.Check(found != null && found.Value.IsActive, "the mod is active before the test"))
        {
            yield break;
        }
        var me = found.Value;

        // --- Turned off in the calm phase: bar gone at once, the normal game's reel takes the hooked fish.
        QuietCalm();
        yield return HookNew(rig, st, 1, hook);
        if (!c.Check(hook.Fight != null, "calm: fight starts"))
        {
            yield break;
        }
        var ff = hook.Float;
        var fish = hook.Fish;
        yield return null;
        yield return null;
        var barBefore = FightHud.BarShown;
        var mark = tap.Mark();
        Block(rig, true);
        c.Check(barBefore && !me.IsActive, $"turned off: the mod is inactive ({me.State}: {me.Status})");
        c.Check(Fight.Current == null && !FightHud.Shown && FightHud.TestRoot == null,
            "turned off in the calm phase: the bar is gone at once");
        c.Check(tap.Has("Fight ended: the mod was turned off.", mark), "Debug line \"Fight ended: the mod was turned off.\"");
        c.Check(!OwnPatchOn(typeof(FishingFloat), nameof(FishingFloat.FixedUpdate))
                && !OwnPatchOn(typeof(Fish), nameof(Fish.CustomFixedUpdate)) && !OwnPatchOn(typeof(Fish), nameof(Fish.SwimDirection))
                && !OwnPatchOn(typeof(Hud), nameof(Hud.Update)),
            "turned off: none of the mod's patches is left on the float, the fish or the HUD");
        c.Check(ff != null && ReferenceEquals(ff.GetCatch(), fish) && fish.IsHooked(), "turned off: the fish stays on the hook");
        rig.Reel(false);
        rig.Fill(p.GetMaxStamina());
        var line0 = ff != null ? ff.m_lineLength : 0f;
        var stamina0 = p.GetStamina();
        yield return new WaitForSeconds(0.6f);
        var idleLine = ff != null ? ff.m_lineLength : -1f;
        var idleUsed = stamina0 - p.GetStamina();
        c.Note($"normal game's reel, no Block: line {F(line0)} -> {F(idleLine)} m, {F(idleUsed)} stamina in 0.6 s");
        // Normal game's reel take line only on ticks where the float sit less than 0.2 m past the line
        // (FishingFloat.FixedUpdate), and the fish start its own escape 1 s after the turn-off (Fight.End) and drag
        // the float away: a fixed 1.2 s gave 0.12 m in one run. So me hold Block until 0.2 m came in (at least
        // 1.2 s, at most 12 s). Stamina full before every tick (0 = the normal game drop the fish), use summed
        // tick by tick. Holding the fish alone cost stamina too: Block must cost clearly more.
        var reelUsed = 0f;
        var reelTicks = 0;
        var lineTicks = 0;
        var escapeTicks = 0;
        var reelStart = Time.time;
        rig.Reel(true);
        while (ff != null && Time.time - reelStart < 12f
               && (Time.time - reelStart < 1.2f || idleLine - ff.m_lineLength <= 0.2f))
        {
            rig.Fill(p.GetMaxStamina());
            var tickStamina = p.GetStamina();
            var tickLine = ff.m_lineLength;
            yield return new WaitForFixedUpdate();
            reelUsed += Mathf.Max(0f, tickStamina - p.GetStamina());
            reelTicks++;
            if (ff != null && ff.m_lineLength < tickLine - 1e-5f)
            {
                lineTicks++;
            }
            if (fish != null && fish.IsEscaping())
            {
                escapeTicks++;
            }
        }
        var reelTime = Time.time - reelStart;
        var reeled = ff != null ? idleLine - ff.m_lineLength : -1f;
        rig.Reel(false);
        rig.Fill(p.GetMaxStamina());
        var idleRate = idleUsed / 0.6f;
        var reelRate = reelTime > 0.01f ? reelUsed / reelTime : 0f;
        c.Note($"normal game's reel with Block: {F(reeled)} m in {F(reelTime)} s, line in on {lineTicks} of {reelTicks} ticks, the fish in its own escape on {escapeTicks}; {F(reelRate)} stamina/s (holding the fish alone {F(idleRate)}/s)");
        c.Check(ff != null && Near(idleLine, line0, 0.01f), "normal game's reel: without Block the line holds");
        c.Check(reeled > 0.2f && reelUsed > 1f && reelRate > idleRate * 2f,
            $"normal game's reel: holding Block brings line in and drains stamina, at least twice the cost of only holding the fish ({F(reeled)} m in {F(reelTime)} s, {F(reelRate)} stamina/s with Block, {F(idleRate)}/s without)");
        yield return null;
        c.Check(Fight.Current == null && !FightHud.Shown, "still no bar while the mod is off");

        // --- Turned on again: the bar is back (the still-hooked fish, then a new one).
        Block(rig, false);
        QuietCalm();
        c.Check(me.IsActive && OwnPatchOn(typeof(FishingFloat), nameof(FishingFloat.FixedUpdate)),
            $"turned on again: the mod is active ({me.State})");
        rig.Fill(p.GetMaxStamina());
        for (var i = 0; i < 5 && Fight.Current == null; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        yield return null;
        yield return null;
        c.Check(ff != null && Fight.Current != null && ReferenceEquals(Fight.Current.Fish, fish) && FightHud.BarShown,
            "turned on again with the fish still hooked: the fight takes it back, bar on screen");
        Remove(hook);
        yield return null;
        yield return HookNew(rig, st, 1, hook);
        yield return null;
        yield return null;
        if (!c.Check(hook.Fight != null && FightHud.BarShown && FightHud.TestBarOnScreen, "turned on again, new fish hooked: the bar is back"))
        {
            yield break;
        }

        // --- Turned off during a fight: the fish stops running, lies still about a second, then the normal game's
        // own escape starts.
        ff = hook.Float;
        fish = hook.Fish;
        Fight.TestPin = null;
        Fight.TestSide = FightLogic.Right;
        Fight.TestPhase = FightPhase.Struggle;
        rig.Reel(false);
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForSeconds(0.8f);
        var zdo = fish.m_nview.GetZDO();
        var running = fish.m_body.linearVelocity.magnitude;
        var fighting = hook.Fight.Phase == FightPhase.Struggle && zdo.GetFloat(ZDOVars.s_escape) > 0f && fish.IsEscaping();
        mark = tap.Mark();
        Block(rig, true);
        var offAt = Time.time;
        c.Check(fighting && running > 1f, $"before: the fish fights and runs ({F(running)} m/s)");
        c.Check(Fight.Current == null && !FightHud.Shown && tap.Has("Fight ended: the mod was turned off.", mark),
            "turned off during a fight: the fight is over, nothing on screen");
        c.Check(fish.IsHooked() && !fish.IsEscaping() && Near(zdo.GetFloat(ZDOVars.s_escape), 0f)
                && !OwnPatchOn(typeof(Fish), nameof(Fish.SwimDirection)),
            "turned off during a fight: the fish stops fighting at once (still hooked, escape time off, not steered any more)");
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForSeconds(0.5f);
        c.Check(fish != null && !fish.IsEscaping(), "half a second later the fish is still quiet");
        if (fish != null)
        {
            c.Note($"fish speed half a second after the turn-off: {F(fish.m_body.linearVelocity.magnitude)} m/s (was {F(running)})");
        }
        var escaped = false;
        var escapeLength = 0f;
        while (fish != null && Time.time - offAt < 2.5f && !escaped)
        {
            yield return new WaitForFixedUpdate();
            if (fish != null && fish.IsEscaping())
            {
                escaped = true;
                escapeLength = fish.m_escapeTime;
            }
        }
        var after = Time.time - offAt;
        if (c.Check(fish != null, "the fish is still there"))
        {
            var vanillaMax = fish.m_escapeMax + fish.m_escapeMaxPerLevel;
            c.Check(escaped && after > 0.8f && escapeLength <= vanillaMax + 0.05f && zdo.GetFloat(ZDOVars.s_escape) > 0f
                    && zdo.GetInt(ZDOVars.s_hooked) == 1,
                $"about a second later the fish fights the normal game's way: its own escape of {F(escapeLength)} s (at most {F(vanillaMax)} s for a plain Perch), {F(after)} s after the turn-off");
        }
        Block(rig, false);
        c.Check(me.IsActive, "turned on again at the end");
    }

    // ---------- fishing.input ----------

    private static IEnumerator RunInput() => RunLive(InputName, false, InputBody);

    private static IEnumerator InputBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        // Default rules, calm pinned, fish pinned off the bar: the fight uses stamina every tick it runs (3 a second
        // for a Perch): that is how the test sees the fight go on or stand still.
        ServerRules.TestRules = new FightRules();
        Fight.TestPhase = FightPhase.Calm;
        Fight.TestPin = FishPin.Away;
        FightHud.TestDisplay = DefaultDisplay;
        var hook = new Hooked();
        yield return HookNew(rig, st, 1, hook);
        var fight = hook.Fight;
        if (!c.Check(fight != null, "fight starts (float and Perch on dry land in front of the player)"))
        {
            yield break;
        }
        var ff = hook.Float;
        rig.Fill(p.GetMaxStamina());
        yield return null;
        yield return null;

        // --- Bar settings (T19): applied on the next frame.
        var normal = FightHud.TestBarPosition;
        var normalScale = FightHud.TestBarScale;
        FightHud.TestDisplay = new Vector3(1.5f, -300f, 100f);
        yield return null;
        var moved = FightHud.TestBarPosition;
        var movedScale = FightHud.TestBarScale;
        FightHud.TestDisplay = DefaultDisplay;
        yield return null;
        c.Check(FightHud.BarShown && Near(normal.x, 260f, 0.5f) && Near(normal.y, 0f, 0.5f) && Near(normalScale.x, 1f),
            $"default settings: bar 260 right of the screen centre, normal size (at {F(normal.x)}, {F(normal.y)}, scale {F(normalScale.x)})");
        c.Check(Near(moved.x, -300f, 0.5f) && Near(moved.y, 100f, 0.5f) && Near(movedScale.x, 1.5f) && Near(movedScale.y, 1.5f),
            $"BarScale 1.5, BarOffsetX -300, BarOffsetY 100: a bigger bar left of the crosshair and higher on the next frame (at {F(moved.x)}, {F(moved.y)}, scale {F(movedScale.x)})");
        c.Check(Near(FightHud.TestBarPosition.x, 260f, 0.5f) && Near(FightHud.TestBarScale.x, 1f), "settings put back: the bar is back in place");
        // No override for one frame: the bar is drawn by the player's own settings (read here, never written).
        FightHud.TestDisplay = null;
        yield return null;
        var own = FightHud.TestBarPosition;
        var ownScale = FightHud.TestBarScale;
        FightHud.TestDisplay = DefaultDisplay;
        yield return null;
        var haveSettings = Plugin.BarScale != null && Plugin.BarOffsetX != null && Plugin.BarOffsetY != null;
        c.Check(haveSettings && Near(own.x, Plugin.BarOffsetX.Value, 0.5f) && Near(own.y, Plugin.BarOffsetY.Value, 0.5f)
                && Near(ownScale.x, Plugin.BarScale.Value, 0.001f) && Near(ownScale.y, Plugin.BarScale.Value, 0.001f),
            haveSettings
                ? $"no override: the bar is drawn by the BarScale / BarOffsetX / BarOffsetY settings of this game (at {F(own.x)}, {F(own.y)}, scale {F(ownScale.x)}; settings {F(Plugin.BarOffsetX.Value)}, {F(Plugin.BarOffsetY.Value)}, {F(Plugin.BarScale.Value)})"
                : "no override: the bar settings are bound");

        // --- Toggle block (T17): one press keeps the zone rising, the next press lets go. Keys through the game's own
        // input path (PlayerController) with a faked Block button.
        var top = 1f - fight.Bar.ZoneSize;
        rig.Keys = true;
        rig.Keyboard(true);
        p.ToggleBlock = true;
        rig.Fill(p.GetMaxStamina());
        HeldKeys.Hold("Block");
        for (var i = 0; i < 3; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        HeldKeys.Release("Block");
        var pressedOn = p.m_blocking;
        var stayed = true;
        var pressAt = Time.time;
        while (Time.time - pressAt < 2.2f)
        {
            yield return new WaitForFixedUpdate();
            stayed &= p.IsBlocking();
        }
        c.Check(pressedOn && stayed && Near(fight.Bar.ZonePos, top, 0.002f),
            $"Toggle block on: one press of Block keeps the zone rising to the top with the key let go (zone {F(fight.Bar.ZonePos)} of {F(top)}, blocking all along {stayed})");
        HeldKeys.Hold("Block");
        for (var i = 0; i < 3; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        HeldKeys.Release("Block");
        var pressedOff = !p.m_blocking;
        yield return new WaitForSeconds(2f);
        c.Check(pressedOff && !p.IsBlocking() && fight.Bar.ZonePos < top * 0.5f,
            $"Toggle block on: the next press lets go and the zone falls (zone {F(fight.Bar.ZonePos)})");
        p.ToggleBlock = false;

        // --- Inventory open (T18): Block held on the keyboard does nothing, the zone falls, the fight goes on.
        rig.Fill(p.GetMaxStamina());
        HeldKeys.Hold("Block");
        yield return new WaitForSeconds(1.8f);
        var heldUp = p.IsBlocking() && fight.Bar.ZonePos > top - 0.01f;
        rig.InventoryOpen = true;
        InventoryGui.instance.Show(null);
        var opened = Time.time;
        while (!InventoryGui.IsVisible() && Time.time - opened < 2f)
        {
            yield return null;
        }
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForSeconds(0.3f);
        var staminaOpen = p.GetStamina();
        yield return new WaitForSeconds(2.2f);
        var visible = InventoryGui.IsVisible();
        var noReel = !p.IsBlocking();
        var zoneOpen = fight.Bar.ZonePos;
        var usedOpen = staminaOpen - p.GetStamina();
        var sameFight = ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Calm;
        InventoryGui.instance.Hide();
        rig.InventoryOpen = false;
        var closed = Time.time;
        while (InventoryGui.IsVisible() && Time.time - closed < 3f)
        {
            yield return null;
        }
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForSeconds(1.2f);
        var again = p.IsBlocking() && fight.Bar.ZonePos > 0.3f;
        HeldKeys.Release("Block");
        c.Check(heldUp, $"Block held on the keyboard: the zone rises to the top (zone {F(fight.Bar.ZonePos)})");
        c.Check(visible && noReel && zoneOpen < top * 0.5f,
            $"inventory open with Block still held: no reel, the zone falls (inventory shown {visible}, blocking {!noReel}, zone {F(zoneOpen)})");
        c.Check(sameFight && usedOpen > 3f,
            $"inventory open: the fight goes on ({F(usedOpen)} stamina used off the bar in 2.2 s)");
        c.Check(again, "inventory closed with Block still held: the zone rises again");
        rig.Keyboard(false);

        // --- Game menu (T18): single player pauses, the fight stands still, then goes on.
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForSeconds(0.3f);
        var menu = Menu.instance;
        rig.MenuOpen = true;
        if (menu != null)
        {
            menu.Show();
        }
        else
        {
            Game.Pause();
        }
        var asked = Time.realtimeSinceStartup;
        while (Time.timeScale > 0f && Time.realtimeSinceStartup - asked < 2f)
        {
            yield return null;
        }
        var paused = Game.IsPaused() && Time.timeScale <= 0f;
        yield return null;
        var staminaPaused = p.GetStamina();
        var zonePaused = fight.Bar.ZonePos;
        var markerPaused = fight.Bar.FishPos;
        var linePaused = ff != null ? ff.m_lineLength : -1f;
        var clockPaused = Time.time;
        yield return new WaitForSecondsRealtime(2f);
        var still = Near(p.GetStamina(), staminaPaused) && Near(fight.Bar.ZonePos, zonePaused, 0.0001f)
                    && Near(fight.Bar.FishPos, markerPaused, 0.0001f) && ff != null && Near(ff.m_lineLength, linePaused, 0.0001f)
                    && Near(Time.time, clockPaused);
        var kept = ReferenceEquals(Fight.Current, fight) && FightHud.BarShown;
        if (menu != null)
        {
            menu.Hide();
        }
        else
        {
            Game.Unpause();
        }
        rig.MenuOpen = false;
        asked = Time.realtimeSinceStartup;
        while (Time.timeScale <= 0f && Time.realtimeSinceStartup - asked < 2f)
        {
            yield return null;
        }
        var staminaResumed = p.GetStamina();
        yield return new WaitForSeconds(1f);
        var resumed = ReferenceEquals(Fight.Current, fight) && p.GetStamina() < staminaResumed - 1f;
        c.Check(paused, "game menu open in single player: the game is paused");
        c.Check(still && kept, "paused for 2 s: the fight stands still (stamina, zone, marker, line and clock unchanged), the bar stays");
        c.Check(resumed, $"menu closed: the fight goes on ({F(staminaResumed - p.GetStamina())} stamina used off the bar in 1 s)");

        // --- Hidden HUD (Ctrl+F3, T19): the bar goes with the HUD. Last: the game shows no message while hidden.
        var hud = Hud.instance;
        rig.Fill(p.GetMaxStamina());
        yield return null;
        var barX = FightHud.TestBarWorld.x;
        var rootX = hud.m_rootObject.transform.position.x;
        var wasVisible = hud.IsVisible() && FightHud.BarShown;
        rig.HudHidden = true;
        hud.m_userHidden = true;
        yield return null;
        yield return null;
        yield return null;
        var hidden = !hud.IsVisible();
        var barShift = FightHud.TestBarWorld.x - barX;
        var rootShift = hud.m_rootObject.transform.position.x - rootX;
        var inside = FightHud.TestRoot != null && FightHud.TestRoot.IsChildOf(hud.m_rootObject.transform);
        hud.m_userHidden = false;
        rig.HudHidden = false;
        yield return null;
        yield return null;
        yield return null;
        c.Check(wasVisible && hidden && inside && Mathf.Abs(rootShift) > 0.001f
                && Mathf.Abs(barShift - rootShift) <= Mathf.Abs(rootShift) * 0.01f + 0.01f,
            $"HUD hidden (what Ctrl+F3 does): the bar goes away with the HUD (bar moved {F(barShift)}, the HUD {F(rootShift)})");
        c.Check(hud.IsVisible() && FightHud.BarShown && Mathf.Abs(FightHud.TestBarWorld.x - barX) <= Mathf.Abs(rootShift) * 0.01f + 0.01f,
            "HUD shown again: the bar is back in place");
    }

    // ---------- fishing.dual ----------

    private static IEnumerator RunDual() => RunLive(DualName, false, DualBody, DualWieldGuid, MovesetGuid);

    private static string ItemName(ItemDrop.ItemData item) =>
        item == null ? "nothing" : item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;

    private static IEnumerator DualBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        c.Check(ModActive(DualWieldGuid) && ModActive(MovesetGuid),
            $"Dual Wielding and Weapon Moveset are active in this run ({ModStateText(DualWieldGuid)}, {ModStateText(MovesetGuid)})");

        // --- Rod from a pair of axes (X04): both axes leave the hands.
        var axe1 = rig.Give("AxeIron");
        var axe2 = rig.Give("AxeIron");
        if (c.Check(axe1 != null && axe2 != null, "two AxeIron given"))
        {
            p.UnequipItem(st.Rod, false);
            yield return null;
            p.EquipItem(axe1);
            p.EquipItem(axe2);
            yield return null;
            yield return null;
            var right = p.m_rightItem;
            var left = p.m_leftItem;
            var paired = (ReferenceEquals(right, axe1) && ReferenceEquals(left, axe2))
                         || (ReferenceEquals(right, axe2) && ReferenceEquals(left, axe1));
            c.Check(paired, $"a pair of axes in the hands (right {ItemName(right)}, left {ItemName(left)})");
            p.EquipItem(st.Rod);
            var top = new Box();
            yield return RodInHand(rig, st, top);
            right = p.m_rightItem;
            left = p.m_leftItem;
            var axeInHand = ReferenceEquals(right, axe1) || ReferenceEquals(right, axe2) || ReferenceEquals(left, axe1)
                            || ReferenceEquals(left, axe2);
            c.Check(top.Ok && !axeInHand && ReferenceEquals(p.GetCurrentWeapon(), st.Rod),
                $"rod equipped from the pair: both axes leave the hands (right {ItemName(right)}, left {ItemName(left)}, on the back {ItemName(p.m_hiddenRightItem)} / {ItemName(p.m_hiddenLeftItem)})");
            if (!top.Ok)
            {
                yield break;
            }
        }

        // --- A plain cast.
        var baitGiven = rig.Give("FishingBait", 3);
        c.Check(baitGiven != null, "FishingBait given");
        var baitBefore = rig.Inv.CountItems(st.BaitName);
        rig.Fill(p.GetMaxStamina());
        var cast = new CastResult();
        yield return Cast(rig, st, 0.8f, null, cast);
        c.Check(cast.Float != null && rig.Inv.CountItems(st.BaitName) == baitBefore - 1 && ReferenceEquals(p.GetCurrentWeapon(), st.Rod),
            "a plain cast with the rod: a float, one bait used (" + cast.Detail + ")");
        Kill(cast.Float != null ? cast.Float.gameObject : null);
        var swing = Time.time;
        while (p.InAttack() && Time.time - swing < 4f)
        {
            yield return new WaitForFixedUpdate();
        }
        yield return null;

        // --- Block + Jump while reeling = a normal dodge roll (toward the float, so the line stays slack), the bar
        // goes on after it.
        ServerRules.TestRules = new FightRules();
        Fight.TestPhase = FightPhase.Calm;
        Fight.TestPin = FishPin.Away;
        FightHud.TestDisplay = DefaultDisplay;
        var hook = new Hooked();
        yield return HookNew(rig, st, 1, hook);
        var fight = hook.Fight;
        if (!c.Check(fight != null, "fight starts"))
        {
            yield break;
        }
        rig.Fill(p.GetMaxStamina());
        rig.Reel(true);
        yield return new WaitForSeconds(1.6f);
        var rising = p.IsBlocking() && fight.Bar.ZonePos > 1f - fight.Bar.ZoneSize - 0.01f;
        var mark = tap.Mark();
        rig.Fill(p.GetMaxStamina());
        p.SetControls(new Vector3(0f, 0f, 1f), false, false, false, false, false, true, true, false, false, false);
        var rolled = false;
        var fellInRoll = false;
        var blockInRoll = false;
        var attacked = false;
        var jumpAt = Time.time;
        while (Time.time - jumpAt < 4.5f)
        {
            yield return new WaitForFixedUpdate();
            // Block stays held, Jump and the direction let go.
            rig.Reel(true);
            attacked |= p.InAttack();
            if (p.InDodge())
            {
                rolled = true;
                fellInRoll |= fight.Bar.ZoneSpeed < 0f;
                blockInRoll |= p.IsBlocking();
            }
            else if (rolled || Time.time - jumpAt > 1.5f)
            {
                break;
            }
        }
        rig.Fill(p.GetMaxStamina());
        // Long enough for the zone to stop falling and climb again.
        yield return new WaitForSeconds(1.8f);
        var after = p.IsBlocking() && fight.Bar.ZonePos > 0.05f && fight.Bar.ZoneSpeed >= 0f;
        yield return null;
        c.Check(rising, "Block held: reeling (the zone rises to the top)");
        c.Check(rolled && !attacked, $"Block + Jump while reeling: a normal dodge roll, no attack (rolled {rolled}, attack {attacked})");
        c.Check(rolled && fellInRoll && !blockInRoll, "during the roll Block does not count: the zone falls");
        c.Check(ReferenceEquals(Fight.Current, fight) && FightHud.BarShown && after && !tap.Has("Fight ended:", mark),
            "after the roll the same fight goes on: bar on screen, the zone rises again with Block");
        rig.Reel(false);
    }

    // ---------- fishing.harpoon ----------

    private static IEnumerator RunHarpoon() => RunLive(HarpoonName, false, HarpoonBody, HarpoonGuid);

    // Tame boar on a harpoon line (the game's own harpoon effect, put on by the test: no throw, no aim), then the rod:
    // the cast lets the line go as in the normal game, and a fight works after it. Harpoon Hooks Tames on.
    private static IEnumerator HarpoonBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        c.Check(ModActive(HarpoonGuid), $"Harpoon Hooks Tames is active in this run ({ModStateText(HarpoonGuid)})");
        SE_Harpooned harpoon = null;
        foreach (var effect in ObjectDB.instance.m_StatusEffects)
        {
            if (effect is SE_Harpooned found)
            {
                harpoon = found;
                break;
            }
        }
        var boarPrefab = ZNetScene.instance.GetPrefab("Boar");
        if (!c.Check(harpoon != null && boarPrefab != null, "the game's harpoon effect and the Boar prefab exist"))
        {
            yield break;
        }
        var side = Vector3.Cross(Vector3.up, st.WaterDir);
        var at = st.Stand + side * 5f;
        at.y = ZoneSystem.instance.GetGroundHeight(at) + 0.3f;
        var boarGo = UnityEngine.Object.Instantiate(boarPrefab, at, Quaternion.identity);
        rig.Track(boarGo);
        var boar = boarGo.GetComponent<Character>();
        for (var i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        if (!c.Check(boar != null && boar.m_nview != null && boar.m_nview.IsValid(), "a boar stands next to the player"))
        {
            yield break;
        }
        boar.SetTamed(true);
        var hash = harpoon.NameHash();
        var line = boar.GetSEMan().AddStatusEffect(hash, true) as SE_Harpooned;
        if (!c.Check(line != null && boar.IsTamed(), "the tame boar takes the harpoon effect"))
        {
            yield break;
        }
        line.SetAttacker(p);
        // The game lets a harpoon line go on an attack or a block only after 2 s.
        var held = Time.time;
        while (Time.time - held < 2.3f)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
        }
        c.Check(boar != null && boar.GetSEMan().HaveStatusEffect(hash), "the tame boar is still on the harpoon line after 2 s");

        // The rod is in hand: cast.
        var baitGiven = rig.Give("FishingBait", 3);
        c.Check(baitGiven != null, "FishingBait given");
        rig.Fill(p.GetMaxStamina());
        var cast = new CastResult();
        yield return Cast(rig, st, 0.8f, null, cast);
        var let = Time.time;
        while (boar != null && boar.GetSEMan().HaveStatusEffect(hash) && Time.time - let < 2f)
        {
            yield return new WaitForFixedUpdate();
        }
        c.Check(cast.Float != null, "cast with the rod: a float (" + cast.Detail + ")");
        c.Check(boar != null && !boar.GetSEMan().HaveStatusEffect(hash), "the cast lets the harpoon line go, as in the normal game");
        Kill(cast.Float != null ? cast.Float.gameObject : null);
        var swing = Time.time;
        while (p.InAttack() && Time.time - swing < 4f)
        {
            yield return new WaitForFixedUpdate();
        }
        yield return null;

        // The fight works after it: calm bar, then a fight to the right, then calm again.
        QuietCalm();
        var hook = new Hooked();
        yield return HookNew(rig, st, 1, hook);
        var fight = hook.Fight;
        if (!c.Check(fight != null, "after the harpoon: a hooked Perch starts the fight"))
        {
            yield break;
        }
        yield return null;
        yield return null;
        var calmBar = FightHud.BarShown;
        Fight.TestSide = FightLogic.Right;
        Fight.TestPhase = FightPhase.Struggle;
        yield return new WaitForSeconds(0.3f);
        yield return null;
        var fought = ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle && !FightHud.BarShown;
        Fight.TestPhase = FightPhase.Calm;
        yield return new WaitForSeconds(0.2f);
        yield return null;
        c.Check(calmBar && fought && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Calm && FightHud.BarShown,
            "after the harpoon: catch bar, fight and calm again work as usual");
    }

    // ---------- fishing.bug.stranded ----------

    private static IEnumerator RunStranded() => RunLive(StrandedName, false, StrandedBody);

    // Fish lying out of the water (dry land) with its own phase timer. Right behaviour, either of: no fight starts
    // while it is out of the water (the calm bar goes on), or a fight there takes no line and has no wrong side.
    private static IEnumerator StrandedBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        var quick = new FightRules
        {
            CalmSeconds = FightRules.CalmSecondsMin, StruggleSeconds = FightRules.StruggleSecondsMax, OffBarStamina = 0f,
        };
        ServerRules.TestRules = quick;
        Fight.TestPin = FishPin.Away;
        FightHud.TestDisplay = DefaultDisplay;
        var hook = new Hooked();
        yield return HookNew(rig, st, 1, hook);
        var fight = hook.Fight;
        if (!c.Check(fight != null, "fight starts with the Perch lying on dry land"))
        {
            yield break;
        }
        var ff = hook.Float;
        var fish = hook.Fish;
        var calmHigh = quick.CalmSeconds * 1.4f * Mathf.Lerp(1.2f, 0.8f, fight.Profile.D01);
        var dry = fish.IsOutOfWater();
        var start = Time.time;
        while (ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Calm && Time.time - start < calmHigh + 2f)
        {
            yield return new WaitForFixedUpdate();
            dry &= fish != null && fish.IsOutOfWater();
        }
        var waited = Time.time - start;
        if (!c.Check(dry && fish != null && ff != null && ReferenceEquals(Fight.Current, fight),
                "the fish lies out of the water for the whole wait and stays hooked"))
        {
            yield break;
        }
        if (fight.Phase == FightPhase.Calm)
        {
            yield return null;
            yield return null;
            c.Check(FightHud.BarShown,
                $"fish out of the water: no fight starts, the catch bar goes on ({F(waited)} s waited, the calm time is at most {F(calmHigh)} s)");
            yield break;
        }
        c.Note($"a fight started {F(waited)} s after the hook although the fish lies out of the water (it cannot run there)");

        // The fight is on: it must take no line...
        rig.Reel(false);
        rig.Fill(p.GetMaxStamina());
        var line0 = ff.m_lineLength;
        var at = fish.transform.position;
        yield return new WaitForSeconds(1f);
        if (ff == null || fish == null || !ReferenceEquals(Fight.Current, fight))
        {
            c.Check(false, "the fight ended during the fight with the fish out of the water");
            yield break;
        }
        var grew = ff.m_lineLength - line0;
        var crawled = Vector3.Distance(Flat(fish.transform.position), Flat(at));
        c.Check(grew < 0.1f,
            $"a fish out of the water cannot run, so it must not take line (took {F(grew)} m in 1 s while it moved {F(crawled)} m)");

        // ... and reeling toward the fish must not cost the wrong-side price (nothing shows a side there).
        var rules = ServerRules.Current;
        var normalCost = FightLogic.ReelCost(ff.m_pullStaminaUse, fish.m_escapeStaminaUse, fight.Quality,
            ff.m_pullStaminaUseMaxSkillMultiplier, p.GetSkillFactor(Skills.SkillType.Fishing)) * rules.StruggleStamina;
        rig.Face(Flat(ff.transform.position - p.transform.position));
        rig.Fill(p.GetMaxStamina());
        rig.Reel(true);
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        var stamina0 = p.GetStamina();
        var t0 = Time.time;
        yield return new WaitForSeconds(0.3f);
        var rate = (stamina0 - p.GetStamina()) / Mathf.Max(0.01f, Time.time - t0);
        rig.Reel(false);
        c.Check(rate <= normalCost * 1.5f + 0.5f,
            $"reeling toward a fish that lies out of the water must not cost the wrong-side price ({F(rate)} stamina/s, the normal cost of a fighting fish is {F(normalCost)}/s, wrong side x{F(rules.WrongSideStamina)})");
    }
}
#endif
