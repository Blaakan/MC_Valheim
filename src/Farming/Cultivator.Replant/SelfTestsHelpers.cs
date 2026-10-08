#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Tools of the newer self tests (SelfTestsKeys / Plant / Forge / Off / Far / Mp): stand the player
// next to a plant, press a game button like a finger would (ZInput button state, read by the real Player.Update),
// place a piece through the vanilla ghost, move a sapling's plant time back instead of the world clock, build a Forge
// of any level, read what the HUD said. Everything a helper change is put back through Rig.Undo.
internal static partial class SelfTests
{
    // ---------- player place ----------

    private static float Ground(Vector3 p) => ZoneSystem.instance.GetGroundHeight(p);

    private static void Put(Player p, Vector3 pos)
    {
        p.transform.position = pos;
        if (p.m_body != null)
        {
            p.m_body.position = pos;
            p.m_body.linearVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
    }

    // Player to pos (same loaded area, no teleport). Rig put it back to Origin at the end.
    private static void MoveTo(Rig rig, Vector3 pos)
    {
        if (!rig.Moved)
        {
            rig.Moved = true;
            var home = rig.Origin;
            var p = rig.Player;
            // Far away (a far-biome test cut short): no jump over unloaded ground, that test starts the trip home.
            rig.Undo(() =>
            {
                if ((p.transform.position - home).sqrMagnitude < 300f * 300f)
                {
                    Put(p, home);
                }
            });
        }
        Put(rig.Player, pos);
    }

    // Where the player stand to work on target: distance m from it, on the side of the test origin.
    private static Vector3 StandPoint(Vector3 origin, Vector3 target, float distance)
    {
        var dir = origin - target;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back;
        var at = target + dir * distance;
        at.y = Ground(at);
        return at;
    }

    private const float StandDistance = 2.6f;

    // Stand point of target usable: about level with it, nothing in the way of a standing player.
    private static bool StandOk(Vector3 origin, Vector3 target)
    {
        var s = StandPoint(origin, target, StandDistance);
        if (s.y < ZoneSystem.instance.m_waterLevel + 0.3f || Mathf.Abs(s.y - target.y) > 0.8f)
        {
            return false;
        }
        return !Physics.CheckCapsule(s + Vector3.up * 0.5f, s + Vector3.up * 1.6f, 0.4f, SpaceMask, QueryTriggerInteraction.Ignore);
    }

    private static readonly float[] SpotRings = { 7f, 9f, 11f, 13f, 15f, 17f, 19f, 22f, 25f, 28f, 32f, 36f, 40f, 44f, 48f };

    // ---------- no-build zones ----------

    // No no-build location at p nor within margin of it. Plain "p not inside" is not enough: first free spot found
    // outside a zone sit right at its edge, and the ghost stand up to a metre from the aimed point (toward the player,
    // who come from inside the zone): vanilla then say NoBuildZone.
    private static bool Buildable(Vector3 p, float margin = 4f)
    {
        if (Location.IsInsideNoBuildLocation(p))
        {
            return false;
        }
        for (var a = 0; a < 8; a++)
        {
            if (Location.IsInsideNoBuildLocation(p + Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward * margin))
            {
                return false;
            }
        }
        return true;
    }

    // p and all around it within margin inside a no-build location (ghost near p is in the zone for sure).
    private static bool DeepInNoBuild(Vector3 p, float margin = 3f)
    {
        if (!Location.IsInsideNoBuildLocation(p))
        {
            return false;
        }
        for (var a = 0; a < 8; a++)
        {
            if (!Location.IsInsideNoBuildLocation(p + Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward * margin))
            {
                return false;
            }
        }
        return true;
    }

    // ---------- PlantEasily ----------

    private const string PlantEasilyId = "advize.PlantEasily";

    private static readonly string[] PlantEasilyTargets =
    {
        nameof(Player.SetupPlacementGhost), nameof(Player.UpdatePlacementGhost), nameof(Player.TryPlacePiece), nameof(Player.PlacePiece),
    };

    private static bool PlantEasilyLoaded => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(PlantEasilyId);

    // PlantEasily (other mod, on the tester's PC) take over the placement ghost of every cultivator plant: its
    // UpdatePlacementGhost postfix move the ghost onto the terrain and set the status Valid when its own checks pass
    // (it know nothing of the Ancient Root rule, no-build zones or wards), its TryPlacePiece prefix refuse with its
    // own texts, its PlacePiece postfix plant a whole grid per press. Tests of this mod and the vanilla game: those
    // four patches off for the test, back at the end (Rig undo). Its config never touched. Returns patches lifted.
    private static int LiftPlantEasily(Rig rig)
    {
        if (!PlantEasilyLoaded)
        {
            return 0;
        }
        var lifted = 0;
        // Own guard: other mod's patches. A problem = note, test goes on with PlantEasily as it is.
        try
        {
            var harmony = new HarmonyLib.Harmony(PlantEasilyId);
            foreach (var method in HarmonyLib.AccessTools.GetDeclaredMethods(typeof(Player)))
            {
                if (Array.IndexOf(PlantEasilyTargets, method.Name) < 0)
                {
                    continue;
                }
                var info = HarmonyLib.Harmony.GetPatchInfo(method);
                if (info == null)
                {
                    continue;
                }
                lifted += LiftPatches(rig, harmony, method, info.Prefixes, 0);
                lifted += LiftPatches(rig, harmony, method, info.Postfixes, 1);
                lifted += LiftPatches(rig, harmony, method, info.Transpilers, 2);
                lifted += LiftPatches(rig, harmony, method, info.Finalizers, 3);
            }
        }
        catch (Exception e)
        {
            SelfTest.Note(rig.Name, $"PlantEasily's placement patches could not all be lifted ({lifted} were): {e.GetType().Name}: {e.Message}");
        }
        return lifted;
    }

    private static int LiftPatches(Rig rig, HarmonyLib.Harmony harmony, System.Reflection.MethodBase method, IEnumerable<HarmonyLib.Patch> patches, int kind)
    {
        var count = 0;
        foreach (var patch in patches.Where(p => p.owner == PlantEasilyId && p.PatchMethod != null).ToArray())
        {
            var again = new HarmonyLib.HarmonyMethod(patch.PatchMethod) { priority = patch.priority, before = patch.before, after = patch.after };
            harmony.Unpatch(method, patch.PatchMethod);
            rig.Undo(() => harmony.Patch(method, prefix: kind == 0 ? again : null, postfix: kind == 1 ? again : null, transpiler: kind == 2 ? again : null,
                finalizer: kind == 3 ? again : null, ilmanipulator: null));
            count++;
        }
        return count;
    }

    // ---------- world clock and the game's slow update loop ----------

    // New world start its clock at 2040 s. A plant time 20000 s back is then before time zero: vanilla
    // Plant.TimeSincePlanted throw (DateTime of negative ticks) inside the game's SlowUpdater coroutine, the loop die
    // and for the rest of the session no plant grow and no loose rock fall. Picked time before zero = no regrow. So
    // the clock is made old enough first (same call as the "skiptime" command, whole days: hour of day stays), and no
    // helper ever write a time before tick 1.
    private const double ClockFloor = 40000.0;

    private static bool ClockOldEnough => ZNet.instance != null && ZNet.instance.GetTimeSeconds() >= ClockFloor;

    // Server or single player: world clock at least ClockFloor. Client: nothing (server step do it). True = old enough.
    private static bool MakeClockOld()
    {
        var net = ZNet.instance;
        if (net == null)
        {
            return false;
        }
        var now = net.GetTimeSeconds();
        if (now >= ClockFloor)
        {
            return true;
        }
        if (!net.IsServer())
        {
            return false;
        }
        var day = EnvMan.instance != null && EnvMan.instance.m_dayLengthSec > 0 ? (double)EnvMan.instance.m_dayLengthSec : 1800.0;
        net.SetNetTime(now + Math.Ceiling((ClockFloor - now) / day) * day);
        return true;
    }

    private static bool ClockOk(Checks c)
    {
        return c.Check(ClockOldEnough,
            $"world clock old enough for the time jumps of this test ({(ZNet.instance != null ? F((float)ZNet.instance.GetTimeSeconds()) : "?")} s, want {F((float)ClockFloor)})");
    }

    // World time this many seconds ago, as ticks. Never before tick 2 (1 and 0 mean "never" to vanilla).
    private static long TicksBack(double seconds)
    {
        return Math.Max(2L, ZNet.instance.GetTime().Ticks - TimeSpan.FromSeconds(seconds).Ticks);
    }

    // Game's SlowUpdater run Plant.SUpdate (growth). Its coroutine die on the first exception inside any SUpdate (a
    // test of any mod, another mod): pulse object see it, me start the loop again and say so. Plants that stood there
    // while it was dead stay stuck (vanilla skip a plant not updated for 10 s), new ones are fine.
    private static IEnumerator SlowLoopRuns(Rig rig, Checks c)
    {
        var go = new GameObject("MC_SelfTest_Pulse");
        var pulse = go.AddComponent<SelfTestPulse>();
        rig.Undo(() =>
        {
            if (go != null)
            {
                Object.Destroy(go);
            }
        });
        yield return WaitFor(() => pulse.Last >= 0f, 4f);
        if (pulse.Last < 0f)
        {
            var updater = Object.FindFirstObjectByType<SlowUpdater>();
            if (updater != null)
            {
                updater.StopCoroutine("UpdateLoop");
                updater.StartCoroutine("UpdateLoop");
                SelfTest.Note(rig.Name, "the game's slow update loop was dead (an error inside it earlier this session): started again for this test");
                yield return WaitFor(() => pulse.Last >= 0f, 4f);
            }
        }
        c.Check(pulse.Last >= 0f, "the game's slow update loop runs (it grows plants)");
    }

    // Start of every test that waits for vanilla growth or regrowth.
    private static IEnumerator GrowReady(Rig rig, Checks c)
    {
        ClockOk(c);
        yield return SlowLoopRuns(rig, c);
    }

    // Free spot for a plant that the player can stand next to. accept = extra test.
    private static bool FindPlantSpot(Rig rig, List<Vector3> taken, float clear, out Vector3 spot, Func<Vector3, bool> accept = null)
    {
        var origin = rig.Origin;
        return FindSpot(origin, Vector3.forward, SpotRings, clear, taken, out spot,
            p => StandOk(origin, p) && (accept == null || accept(p)));
    }

    // Player next to target, looking at it; camera had time to follow.
    private static IEnumerator Stand(Rig rig, Vector3 target)
    {
        MoveTo(rig, StandPoint(rig.Origin, target, StandDistance) + Vector3.up * 0.05f);
        rig.LookDir(target + Vector3.up * 0.5f - rig.Player.m_eye.position);
        yield return new WaitForSeconds(0.4f);
    }

    // ---------- common start of the tests that press buttons ----------

    // Default rules, day, no auto pickup, player still (stray mouse or pad never move it), stamina use on (probe world
    // has StaminaRate 0), camera close behind the player (nothing between camera and plant), buttons let go at the end.
    // Also: world clock old enough (single player; a client asks its server in ServerDefaults), and PlantEasily's
    // placement patches off for the test (plantEasilyOff false = left as the tester has it).
    private static void PrepareInput(Rig rig, bool defaultRules = true, bool plantEasilyOff = true)
    {
        // Own guard: a problem here must show as a note and failed checks later, never stop every test at its start.
        try
        {
            MakeClockOld();
            if (plantEasilyOff)
            {
                LiftPlantEasily(rig);
            }
        }
        catch (Exception e)
        {
            SelfTest.Note(rig.Name, $"test start: world clock or PlantEasily step failed: {e.GetType().Name}: {e.Message}");
        }
        if (defaultRules)
        {
            UseDefaultRules();
        }
        rig.Noon();
        rig.NoAutoPickup();
        rig.TakeControls();
        // Hugin lands in front of the camera when a tutorial waits (first Forge, first open bag: seen in the shots of
        // 2026-10-08), right where the tests aim: no new visit while a test runs. In-memory flag, put back at the end.
        var tutorials = Raven.m_tutorialsEnabled;
        Raven.m_tutorialsEnabled = false;
        rig.Undo(() => Raven.m_tutorialsEnabled = tutorials);
        var rate = Game.m_staminaRate;
        Game.m_staminaRate = 1f;
        rig.Undo(() => Game.m_staminaRate = rate);
        rig.Undo(ReleaseKeys);
        // Player controller off = nobody count the input delay down (Hud set 0.2 s when a menu close): me clear it.
        rig.Undo(() => PlayerController.takeInputDelay = 0f);
        var cam = GameCamera.instance;
        if (cam != null)
        {
            var distance = cam.m_distance;
            cam.m_distance = Mathf.Max(cam.m_minDistance, 2f);
            rig.Undo(() => cam.m_distance = distance);
        }
        var p = rig.Player;
        var regen = p.m_staminaRegenTimer;
        rig.Undo(() => p.m_staminaRegenTimer = Mathf.Min(regen, 1f));
        p.m_stamina = p.GetMaxStamina();
    }

    // Tool in the right hand (build mode for a cultivator).
    private static IEnumerator Hold(Rig rig, ItemDrop.ItemData tool)
    {
        var p = rig.Player;
        var right = p.GetRightItem();
        if (!ReferenceEquals(right, tool))
        {
            if (right != null)
            {
                p.UnequipItem(right, false);
            }
            p.EquipItem(tool, false);
        }
        yield return Frames(3);
    }

    private static void KnowRecipe(Rig rig, Recipe recipe)
    {
        if (recipe == null || recipe.m_item == null)
        {
            return;
        }
        var key = recipe.m_item.m_itemData.m_shared.m_name;
        var p = rig.Player;
        if (p.m_knownRecipes.Add(key))
        {
            rig.Undo(() => p.m_knownRecipes.Remove(key));
        }
    }

    // ---------- buttons ----------

    private static readonly HashSet<string> PressedKeys = new HashSet<string>();

    private static bool HasButton(string name) => ZInput.instance != null && ZInput.instance.GetButtonDef(name) != null;

    private static void PressKey(string name)
    {
        var def = ZInput.instance != null ? ZInput.instance.GetButtonDef(name) : null;
        if (def != null)
        {
            def.Press();
            PressedKeys.Add(name);
        }
    }

    private static void LetGo(string name)
    {
        var def = ZInput.instance != null ? ZInput.instance.GetButtonDef(name) : null;
        if (def != null)
        {
            def.Release();
        }
    }

    // Every button a test pressed: state cleared (finally, no yield).
    private static void ReleaseKeys()
    {
        if (ZInput.instance != null)
        {
            foreach (var name in PressedKeys)
            {
                ZInput.ResetButtonStatus(name);
            }
        }
        PressedKeys.Clear();
    }

    // One press of these buttons together, like one finger on one key bound to all of them. Held three frames: the
    // game read "pressed" in exactly one Player.Update, whatever the order of ZInput and Player in the frame.
    // eachFrame = look at the game while the press is seen.
    private static IEnumerator Tap(string[] names, Action eachFrame = null)
    {
        foreach (var n in names)
        {
            PressKey(n);
        }
        for (var i = 0; i < 3; i++)
        {
            yield return null;
            eachFrame?.Invoke();
        }
        foreach (var n in names)
        {
            LetGo(n);
        }
        for (var i = 0; i < 2; i++)
        {
            yield return null;
            eachFrame?.Invoke();
        }
    }

    private static IEnumerator Tap(string name) => Tap(new[] { name });

    // Wait until a press can act: tool swing over, vanilla tool delay passed, no input delay left.
    private static IEnumerator ReadyForKey(Player p)
    {
        var until = Time.time + 4f;
        while (Time.time < until && (p.InAttack() || p.InDodge() || Time.time - p.m_lastToolUseTime <= 0.7f))
        {
            yield return null;
        }
        PlayerController.takeInputDelay = 0f;
        yield return null;
    }

    // ---------- what the HUD said ----------

    private static string CenterText()
    {
        var hud = MessageHud.instance;
        return hud != null && hud.m_messageCenterText != null ? hud.m_messageCenterText.text ?? "" : "";
    }

    private static void ClearCenter()
    {
        var hud = MessageHud.instance;
        if (hud != null && hud.m_messageCenterText != null)
        {
            hud.m_messageCenterText.text = "";
        }
    }

    private static string HintText() => Hud.instance != null && Hud.instance.m_hoverName != null ? Hud.instance.m_hoverName.text ?? "" : "";

    private static bool CrosshairYellow() => Hud.instance != null && Hud.instance.m_crosshair != null && Hud.instance.m_crosshair.color == Color.yellow;

    private static string OneLine(string text) => (text ?? "").Replace("\n", " / ");

    // Top-left message watch: waiting ones dropped now, so a new one with this text (and icon) is seen for sure.
    private sealed class TopLeftWatch
    {
        internal string Text;
        internal Sprite Icon;
        internal MessageHud.MsgData Current;
    }

    private static TopLeftWatch WatchTopLeft(string text, Sprite icon)
    {
        var hud = MessageHud.instance;
        var watch = new TopLeftWatch { Text = text, Icon = icon };
        if (hud != null)
        {
            hud.m_msgQeue.Clear();
            watch.Current = hud.currentMsg;
        }
        return watch;
    }

    private static bool Arrived(TopLeftWatch watch)
    {
        var hud = MessageHud.instance;
        if (hud == null)
        {
            return false;
        }
        foreach (var m in hud.m_msgQeue)
        {
            if (Matches(watch, m))
            {
                return true;
            }
        }
        return !ReferenceEquals(hud.currentMsg, watch.Current) && Matches(watch, hud.currentMsg);
    }

    private static bool Matches(TopLeftWatch watch, MessageHud.MsgData m)
    {
        return m != null && m.m_text == watch.Text && (watch.Icon == null || ReferenceEquals(m.m_icon, watch.Icon));
    }

    // ---------- plants and drops ----------

    private static int StackSum(List<ItemDrop> drops)
    {
        var sum = 0;
        foreach (var d in drops)
        {
            sum += d.m_itemData.m_stack;
        }
        return sum;
    }

    // What a hand pick of this plant drop (vanilla Pickable.RPC_Pick) with no skill bonus: the random bonus roll is
    // off on every plant the Rig spawned or tracked (NoSkillBonus).
    private static int PickAmount(Pickable p)
    {
        return p.m_dontScale ? p.m_amount : Mathf.Max(p.m_minAmountScaled, Game.instance.ScaleDrops(p.m_itemPrefab, p.m_amount));
    }

    // Loaded object of one of these prefabs near pos (flat distance), not in except. Null = none.
    private static ZNetView FindNear(IEnumerable<int> prefabHashes, Vector3 pos, float radius, HashSet<ZNetView> except = null)
    {
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            return null;
        }
        var hashes = new HashSet<int>(prefabHashes);
        ZNetView best = null;
        var bestSqr = radius * radius;
        foreach (var pair in scene.m_instances)
        {
            var zdo = pair.Key;
            var view = pair.Value;
            if (zdo == null || view == null || !hashes.Contains(zdo.GetPrefab()) || (except != null && except.Contains(view)))
            {
                continue;
            }
            var d = view.transform.position - pos;
            d.y = 0f;
            if (d.sqrMagnitude <= bestSqr)
            {
                best = view;
                bestSqr = d.sqrMagnitude;
            }
        }
        return best;
    }

    private static HashSet<ZNetView> LoadedOf(IEnumerable<int> prefabHashes)
    {
        var set = new HashSet<ZNetView>();
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            return set;
        }
        var hashes = new HashSet<int>(prefabHashes);
        foreach (var pair in scene.m_instances)
        {
            if (pair.Key != null && pair.Value != null && hashes.Contains(pair.Key.GetPrefab()))
            {
                set.Add(pair.Value);
            }
        }
        return set;
    }

    private static IEnumerable<int> GrownHashes(PlantKind kind) => kind.GrownPrefabs.Select(n => n.GetStableHashCode());

    // A plant spawned where the crosshair of the standing player really find it (Replant.Target), cultivator in hand.
    private sealed class Aimed
    {
        internal GameObject Go;
        internal ZNetView View;
        internal Vector3 Spot;
        internal Vector3 AimAt;
        internal bool Seen;
        internal string Why = "";
    }

    // Spawn prefab at a free spot, stand next to it, aim: up to four spots until Replant see it under the crosshair.
    private static IEnumerator SpawnInReach(Rig rig, List<Vector3> taken, string prefab, float clear, Aimed result, Func<Vector3, bool> accept = null)
    {
        result.Seen = false;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (!FindPlantSpot(rig, taken, clear, out var spot, accept))
            {
                result.Why += " (no free spot with room to stand)";
                yield break;
            }
            var go = rig.Spawn(prefab, spot, Quaternion.identity);
            if (go == null)
            {
                result.Why += $" (prefab {prefab} missing)";
                yield break;
            }
            yield return Settle();
            result.Go = go;
            result.View = go.GetComponent<ZNetView>();
            result.Spot = spot;
            yield return Stand(rig, spot);
            result.AimAt = AimPoint(go);
            yield return rig.AimAt(result.AimAt);
            yield return Frames(3);
            var t = Replant.Target;
            if (t.IsFresh && ReferenceEquals(t.View, result.View))
            {
                result.Seen = true;
                yield break;
            }
            result.Why += $" (try {attempt + 1} at {F(spot)}: {RayNote(rig.Player)})";
            rig.Destroy(go);
            yield return Settle();
        }
    }

    // What the camera ray of the interact mask hit first (why a plant was not under the crosshair).
    private static string RayNote(Player p)
    {
        var cam = GameCamera.instance;
        if (cam == null)
        {
            return "no camera";
        }
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 50f, p.m_interactMask))
        {
            return "ray hit nothing";
        }
        return $"ray hit {hit.collider.name} at {F(Vector3.Distance(p.m_eye.position, hit.point))} m from the eye";
    }

    // ---------- placing through the vanilla ghost ----------

    private sealed class Placing
    {
        internal Player.PlacementStatus Status;
        internal Vector3 GhostAt;
        internal bool Placed;
        internal GameObject Go;
        internal ZNetView View;
    }

    // Piece of the open build table picked (must be known), player next to point, crosshair on it: status of the ghost
    // as vanilla Player.UpdatePlacementGhost set it.
    private static IEnumerator AimGhost(Rig rig, Piece piece, Vector3 point, Placing result)
    {
        var p = rig.Player;
        result.Status = Player.PlacementStatus.NoRayHits;
        result.Placed = false;
        result.Go = null;
        result.View = null;
        yield return Stand(rig, point);
        p.UpdateKnownRecipesList();
        p.UpdateAvailablePiecesList();
        if (!p.SetSelectedPiece(piece))
        {
            yield break;
        }
        yield return rig.AimAt(point);
        yield return Frames(3);
        result.Status = p.m_placementStatus;
        result.GhostAt = p.m_placementGhost != null ? p.m_placementGhost.transform.position : point;
    }

    // Place button (left mouse) pressed once on the ghost AimGhost set up. Placed = a new object of saplingHash stand
    // at the ghost.
    private static IEnumerator PressPlace(Rig rig, int saplingHash, Placing result)
    {
        var p = rig.Player;
        var hashes = new[] { saplingHash };
        var before = LoadedOf(hashes);
        yield return ReadyForKey(p);
        yield return Tap("Attack");
        yield return Frames(3);
        var view = FindNear(hashes, result.GhostAt, 1.5f, before);
        result.Placed = view != null;
        result.View = view;
        result.Go = view != null ? view.gameObject : null;
        rig.Track(result.Go);
        result.Status = p.m_placementStatus;
    }

    // ---------- time ----------

    // Sapling planted this many seconds ago (what "skiptime" do to it, world clock untouched).
    // Never a time before the clock start (TicksBack): that throw in vanilla and kill the game's slow update loop.
    private static void Age(ZNetView sapling, double seconds)
    {
        if (Alive(sapling))
        {
            sapling.GetZDO().Set(ZDOVars.s_plantTime, TicksBack(seconds));
        }
    }

    private sealed class Grown
    {
        internal bool Ok;
        internal GameObject Go;
        internal float Waited;
    }

    // Why a sapling did not grow (note in the log of the test).
    private static string YoungNote(ZNetView sapling)
    {
        var plant = Alive(sapling) ? sapling.GetComponent<Plant>() : null;
        if (plant == null)
        {
            return "young plant gone, no grown plant found at its place";
        }
        return $"still a young plant: status {plant.GetStatus()}, {F((float)plant.TimeSincePlanted())} s since planted, grow time {F(plant.GetGrowTime())} s, "
               + $"loaded {F(Time.time - plant.m_spawnTime)} s ago, last slow update {F(Time.time - (plant.m_updateTime - 10f))} s ago";
    }

    // Wait until vanilla Plant.SUpdate grew this sapling (it wait 10 s after spawn first): sapling gone, one of the
    // kind's grown prefabs at its place.
    private static IEnumerator WaitGrown(Rig rig, PlantKind kind, ZNetView sapling, Vector3 at, float seconds, Grown result, HashSet<ZNetView> except = null)
    {
        result.Ok = false;
        result.Go = null;
        var start = Time.time;
        var hashes = GrownHashes(kind).ToArray();
        while (Time.time - start < seconds)
        {
            if (!Alive(sapling))
            {
                var view = FindNear(hashes, at, 0.8f, except);
                if (view != null)
                {
                    result.Ok = true;
                    result.Go = view.gameObject;
                    rig.Track(result.Go);
                    break;
                }
            }
            yield return new WaitForSeconds(0.25f);
        }
        result.Waited = Time.time - start;
        if (!result.Ok)
        {
            SelfTest.Note(rig.Name, $"{kind.DisplayName} transplant not grown after {F(result.Waited)} s: {YoungNote(sapling)}");
        }
    }

    // Picked plant ripe again (what "skiptime" past its regrow time do): picked time moved back, vanilla respawn check.
    private static IEnumerator Regrow(Pickable pick)
    {
        var view = pick.GetComponent<ZNetView>();
        if (Alive(view))
        {
            // World clock younger than the regrow time = picked "at the clock start", too short ago: no regrow (tests
            // check the clock first, ClockOk).
            var pickedAt = TicksBack((pick.m_respawnTimeMinutes + 1f) * 60.0);
            view.GetZDO().Set(ZDOVars.s_pickedTime, pickedAt);
            // Vanilla keeps its own copy and, while that is still 0 (plant never picked before), writes a new time.
            pick.m_pickedTime = pickedAt;
            pick.UpdateRespawn();
        }
        yield return Frames(3);
    }

    // ---------- Forge ----------

    private sealed class ForgeRig
    {
        internal CraftingStation Station;
        internal Vector3 Spot;
        internal Vector3 ToPlayer;
        internal int Extensions;
    }

    // Forge near the test origin, usable from where the player stand (use distance raised on this one object).
    private static bool SpawnForge(Rig rig, List<Vector3> taken, ForgeRig forge)
    {
        var origin = rig.Origin;
        if (!FindSpot(origin, Vector3.forward, new[] { 6f, 8f, 10f, 12f, 14f, 16f, 18f }, 4f, taken, out var spot))
        {
            return false;
        }
        var toPlayer = origin - spot;
        toPlayer.y = 0f;
        toPlayer = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : Vector3.back;
        var go = rig.Spawn("forge", spot, Quaternion.LookRotation(toPlayer));
        forge.Station = go != null ? go.GetComponentInChildren<CraftingStation>() : null;
        forge.Spot = spot;
        forge.ToPlayer = toPlayer;
        forge.Extensions = 0;
        if (forge.Station != null)
        {
            forge.Station.m_useDistance = 60f;
        }
        return forge.Station != null;
    }

    // Extensions forge_ext1..N stand around the Forge (each inside its own reach), then wait for the station level.
    private static IEnumerator ForgeLevel(Rig rig, ForgeRig forge, int level, Checks c)
    {
        while (forge.Extensions < level - 1)
        {
            var index = forge.Extensions + 1;
            var name = "forge_ext" + index;
            var prefab = ZNetScene.instance.GetPrefab(name);
            var ext = prefab != null ? prefab.GetComponent<StationExtension>() : null;
            if (!c.Check(ext != null, $"Forge extension {name} exists"))
            {
                yield break;
            }
            var reach = Mathf.Min(2.6f, ext.m_maxStationDistance * 0.75f);
            var dir = Quaternion.Euler(0f, 60f * index + 30f, 0f) * forge.ToPlayer;
            var p = forge.Spot + dir * reach;
            p.y = Ground(p);
            rig.Spawn(name, p, Quaternion.LookRotation(forge.ToPlayer));
            forge.Extensions = index;
        }
        var until = Time.time + 10f;
        while (forge.Station.GetLevel() < level && Time.time < until)
        {
            yield return new WaitForSeconds(0.25f);
        }
        c.Check(forge.Station.GetLevel() == level, $"Forge level {forge.Station.GetLevel()} with {forge.Extensions} extension(s) (want {level})");
    }

    // Crafting window open at this station on the Upgrade (or Craft) tab, list built.
    private static IEnumerator OpenStation(Rig rig, CraftingStation station, bool upgradeTab)
    {
        var gui = InventoryGui.instance;
        rig.Player.SetCraftingStation(station);
        gui.Show(null, 3);
        yield return new WaitForSecondsRealtime(0.8f);
        if (upgradeTab)
        {
            gui.OnTabUpgradePressed();
        }
        else
        {
            gui.OnTabCraftPressed();
        }
        yield return Frames(2);
    }

    private static IEnumerator CloseStation(Rig rig)
    {
        var gui = InventoryGui.instance;
        gui.Hide();
        rig.Player.SetCraftingStation(null);
        yield return new WaitForSecondsRealtime(0.4f);
    }

    // Row of the crafting list for this item (upgrade) or, item null, the craft row of this recipe. -1 = none.
    private static int RecipeRow(Recipe recipe, ItemDrop.ItemData item)
    {
        var rows = InventoryGui.instance.m_availableRecipes;
        for (var i = 0; i < rows.Count; i++)
        {
            if (item != null ? ReferenceEquals(rows[i].ItemData, item) : rows[i].ItemData == null && ReferenceEquals(rows[i].Recipe, recipe))
            {
                return i;
            }
        }
        return -1;
    }

    private static bool AnyCultivatorRow()
    {
        foreach (var row in InventoryGui.instance.m_availableRecipes)
        {
            if (row.ItemData != null && CultivatorTiers.IsCultivator(row.ItemData))
            {
                return true;
            }
        }
        return false;
    }

    // Requirement rows the crafting panel show now: "Black metal x5, Linen thread x10".
    private static string RequirementRows()
    {
        var parts = new List<string>();
        foreach (var root in InventoryGui.instance.m_recipeRequirementList)
        {
            if (root == null)
            {
                continue;
            }
            var name = root.transform.Find("res_name");
            var amount = root.transform.Find("res_amount");
            if (name == null || amount == null || !name.gameObject.activeSelf)
            {
                continue;
            }
            parts.Add(name.GetComponent<TMPro.TMP_Text>().text + " x" + amount.GetComponent<TMPro.TMP_Text>().text);
        }
        return string.Join(", ", parts.ToArray());
    }

    private static string WantRows(params object[] nameAmount)
    {
        var parts = new List<string>();
        for (var i = 0; i + 1 < nameAmount.Length; i += 2)
        {
            parts.Add(L(SharedName((string)nameAmount[i])) + " x" + nameAmount[i + 1]);
        }
        return string.Join(", ", parts.ToArray());
    }

    // Row picked and Craft pressed, crafting time skipped. Panel had two frames to draw the row first.
    private static IEnumerator CraftRow(int row)
    {
        var gui = InventoryGui.instance;
        gui.SetRecipe(row, false);
        yield return Frames(2);
        gui.OnCraftPressed();
        yield return null;
        if (gui.m_craftTimer >= 0f)
        {
            gui.m_craftTimer = 1000f;
        }
        yield return Frames(3);
    }

    private static int Have(Rig rig, string prefab) => CountByName(rig.Inv, SharedName(prefab));

    private static ItemDrop.ItemData CultivatorOfQuality(Rig rig, int quality, ItemDrop.ItemData not = null)
    {
        foreach (var item in rig.Inv.GetAllItems())
        {
            if (!ReferenceEquals(item, not) && item.m_quality == quality && CultivatorTiers.IsCultivator(item))
            {
                return item;
            }
        }
        return null;
    }

    // Our sapling pieces the open build table offer now.
    private static int SaplingPiecesOffered(Player p)
    {
        var table = p.m_buildPieces;
        if (table == null)
        {
            return 0;
        }
        var count = 0;
        foreach (var kind in PlantCatalog.All)
        {
            var sapling = TransplantContent.SaplingPrefab(kind);
            var piece = sapling != null ? sapling.GetComponent<Piece>() : null;
            if (piece != null && table.m_availablePieces.Contains(piece))
            {
                count++;
            }
        }
        return count;
    }
}

// Debug build only. Me = heartbeat of the game's SlowUpdater loop (the one that run Plant.SUpdate): it call me like any
// plant, self tests read when (SelfTests.SlowLoopRuns).
internal sealed class SelfTestPulse : SlowUpdate
{
    internal float Last = -1f;

    public override void SUpdate(float time, Vector2s referenceZone)
    {
        Last = Time.time;
    }
}
#endif
