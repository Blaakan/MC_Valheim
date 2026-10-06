using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Exploration.MusicInstrumentsMod;

// Debug build only (calls vanish in Release). In-world self tests, run by the world probe (tools/Test-InWorld.ps1,
// -Mod Music.Instruments). Design 8:
//   music.network   rules and note batch on the wire (round trip, clamps, bad layout / instrument / count refused)
//   music.item      three items, recipes (defaults), Music effect registered; tools taking both hands, no punch, model
//                   with its markers, dropped copy with collider; screenshots held and dropped
//   music.synth     our audio filter runs, sound comes out, louder on the right for a source on the right (Unity
//                   pans before our filter), silence after hush, mixer group found
//   music.songs     every preset on every instrument; a MIDI file made here read back through the songs folder
//   music.perform   mini-game with simulated key presses: hits, Encore, Music effect, +comfort, ZDO flag, keys held
//                   back, stop puts everything back; screenshot of the HUD
//   music.autoplay  lyre song plays by itself: notes sound, ZDO flag, pose weight, stop
//   music.listen    a made-up other player's batches: heard here (emitter at their place), Encore in range gives the
//                   effect, End lets go and frees the emitter
//   music.window    song window opens with the instrument in hand, holds the keys, starts the mini-game; screenshots
//   music.pose      each instrument played, camera parked in front and to the side; markers near their targets, also
//                   the way other players' games pose it; screenshots
//   music.export    item, effect and package icons written as PNG next to the screenshots
// Me force rules with ServerRules.TestRules, songs folder with SongLibrary.ConfiguredFolder (memory only), presses with
// LaneKeys.TestPress: never the config file. Rig put back controls, look, time, inventory, equipment, effects.
internal static class SelfTests
{
#if DEBUG
    private const string NetworkName = "music.network";
    private const string ItemName = "music.item";
    private const string SynthName = "music.synth";
    private const string SongsName = "music.songs";
    private const string PerformName = "music.perform";
    private const string AutoplayName = "music.autoplay";
    private const string ListenName = "music.listen";
    private const string WindowName = "music.window";
    private const string PoseName = "music.pose";
    private const string ExportName = "music.export";

    private static readonly string[] Names =
    {
        NetworkName, ItemName, SynthName, SongsName, PerformName, AutoplayName, ListenName, WindowName, PoseName, ExportName,
    };
#endif

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(NetworkName, RunNetwork);
        SelfTest.Register(ItemName, RunItem);
        SelfTest.Register(SynthName, RunSynth);
        SelfTest.Register(SongsName, RunSongs);
        SelfTest.Register(PerformName, RunPerform);
        SelfTest.Register(AutoplayName, RunAutoplay);
        SelfTest.Register(ListenName, RunListen);
        SelfTest.Register(WindowName, RunWindow);
        SelfTest.Register(PoseName, RunPose);
        SelfTest.Register(ExportName, RunExport);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        foreach (var name in Names)
        {
            SelfTest.Unregister(name);
        }
        ClearOverrides();
#endif
    }

#if DEBUG
    private static void ClearOverrides()
    {
        ServerRules.TestPending = false;
        ServerRules.TestRules = null;
        InstrumentPose.TestAsRemote = false;
        TestCamera.Active = false;
        Plugin.TestInactive = false;
        for (var i = 0; i < LaneKeys.TestPress.Length; i++)
        {
            LaneKeys.TestPress[i] = false;
        }
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

    private static MusicRules TestRules(float successSeconds = 6f)
    {
        var d = MusicRules.Default;
        return new MusicRules
        {
            FluteResources = d.FluteResources,
            FluteStation = d.FluteStation,
            FluteStationLevel = d.FluteStationLevel,
            LyreResources = d.LyreResources,
            LyreStation = d.LyreStation,
            LyreStationLevel = d.LyreStationLevel,
            TambourineResources = d.TambourineResources,
            TambourineStation = d.TambourineStation,
            TambourineStationLevel = d.TambourineStationLevel,
            ComfortBonus = 3,
            SuccessSeconds = successSeconds,
            SuccessAccuracy = 0.7f,
            BonusMinutes = 2f,
            BonusRange = 20f,
            HearingRange = 40f,
        };
    }

    private static SongEntry Preset(string id)
    {
        foreach (var e in SongLibrary.Presets)
        {
            if (e.Id == "preset:" + id)
            {
                return e;
            }
        }
        return null;
    }

    // Me = what the live tests change on the player and world. Restore (finally, no yield) put everything back.
    private sealed class Rig
    {
        internal readonly Player Player;
        private readonly float _pitch;
        private readonly Quaternion _yaw;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly List<ItemDrop.ItemData> _added = new List<ItemDrop.ItemData>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private PlayerController _controller;
        private bool _controllerEnabled;
        private bool _envSaved;
        private bool _todOn;
        private float _tod;
        private readonly bool _hadEffect;

        internal Rig(Player player)
        {
            Player = player;
            _pitch = player.m_lookPitch;
            _yaw = player.m_lookYaw;
            _right = player.GetRightItem();
            _left = player.GetLeftItem();
            _hadEffect = player.GetSEMan().HaveStatusEffect(InstrumentContent.EffectHash);
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

        internal ItemDrop.ItemData Hold(InstrumentKind kind)
        {
            var item = Give(InstrumentContent.ItemName(kind));
            if (item != null)
            {
                Player.EquipItem(item, false);
            }
            return item;
        }

        internal void Spawned(GameObject go)
        {
            if (go != null)
            {
                _spawned.Add(go);
            }
        }

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
            Player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
        }

        internal void Attack()
        {
            Player.SetControls(Vector3.zero, true, true, false, false, false, false, false, false, false, false);
        }

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

        internal void Look(float yaw, float pitch)
        {
            Player.m_lookYaw = Quaternion.Euler(0f, yaw, 0f);
            Player.m_lookPitch = pitch;
            Player.UpdateEyeRotation();
            Player.m_lookDir = Player.m_eye.forward;
        }

        internal void Restore()
        {
            ClearOverrides();
            Try("performance", () => Performance.Abort(null));
            Try("window", Performance.CloseWindow);
            Try("listeners", Listeners.Shutdown);
            Try("controls", () =>
            {
                if (_controller != null)
                {
                    Player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
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
            Try("effect", () =>
            {
                if (!_hadEffect)
                {
                    Player.GetSEMan().RemoveStatusEffect(InstrumentContent.EffectHash, true);
                }
            });
            Try("spawned", () =>
            {
                foreach (var go in _spawned)
                {
                    if (go != null && ZNetScene.instance != null)
                    {
                        ZNetScene.instance.Destroy(go);
                    }
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
        }

        private static void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up ({what}) failed: {e.Message}");
            }
        }
    }

    // Camera parked where a test wants it (pose and item screenshots). Debug-only patch, last of all.
    internal static class TestCamera
    {
        internal static bool Active;
        internal static Vector3 Position;
        internal static Quaternion Rotation = Quaternion.identity;

        internal static void LookAt(Vector3 from, Vector3 at)
        {
            Position = from;
            Rotation = Quaternion.LookRotation(at - from);
            Active = true;
        }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera))]
    private static class TestCameraPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(GameCamera __instance)
        {
            if (!TestCamera.Active)
            {
                return;
            }
            __instance.transform.position = TestCamera.Position;
            __instance.transform.rotation = TestCamera.Rotation;
        }
    }

    // ---------- music.network ----------

    private static IEnumerator RunNetwork()
    {
        var c = new Checks(NetworkName);
        // Rules round trip and clamps.
        var r = TestRules(17f);
        r.FluteResources = "FineWood:9";
        var pkg = new ZPackage();
        r.Write(pkg);
        pkg.SetPos(0);
        c.Check(MusicRules.TryRead(pkg, out var back, out var clamped) && !clamped, "rules read back");
        c.Check(back != null && back.FluteResources == "FineWood:9" && Mathf.Approximately(back.SuccessSeconds, 17f)
                && Mathf.Approximately(back.BonusRange, r.BonusRange) && back.ComfortBonus == 3, "rules values kept");
        var wild = TestRules(500f);
        wild.ComfortBonus = 99;
        wild.HearingRange = float.NaN;
        pkg = new ZPackage();
        wild.Write(pkg);
        pkg.SetPos(0);
        c.Check(MusicRules.TryRead(pkg, out var tamed, out clamped) && clamped && tamed.ComfortBonus == MusicRules.ComfortMax
                && Mathf.Approximately(tamed.SuccessSeconds, MusicRules.SuccessSecondsMax)
                && Mathf.Approximately(tamed.HearingRange, MusicRules.Default.HearingRange), "wild rules clamped");
        pkg = new ZPackage();
        pkg.Write(MusicRules.Layout + 1);
        pkg.SetPos(0);
        c.Check(!MusicRules.TryRead(pkg, out _, out _), "unknown rules layout refused");

        // Note batch round trip.
        var batch = new NoteBatch
        {
            Performance = 1234,
            Performer = new ZDOID(77L, 5u),
            Position = new Vector3(1f, 2f, 3f),
            Instrument = InstrumentKind.Lyre,
            Seq = 9,
            SentAt = 12.5f,
            Flags = BatchFlags.Encore | BatchFlags.Live,
        };
        batch.Notes.Add(new Note(12.501f, 0.75f, 60, 90));
        batch.Notes.Add(new Note(13.2f, 70f, 127, 0));
        var wire = batch.ToPackage();
        wire.SetPos(0);
        var read = new NoteBatch();
        c.Check(NoteBatch.TryRead(wire, read), "batch read back");
        c.Check(read.Performance == 1234 && read.Performer == batch.Performer && read.Instrument == InstrumentKind.Lyre
                && read.Seq == 9 && Mathf.Approximately(read.SentAt, 12.5f) && read.IsEncore && read.Live && !read.IsEnd,
            "batch header kept");
        c.Check(read.Notes.Count == 2 && Mathf.Abs(read.Notes[0].Time - 12.501f) < 0.0011f && read.Notes[0].Pitch == 60
                && Mathf.Abs(read.Notes[0].Length - 0.75f) < 0.0011f, "notes kept to the millisecond");
        c.Check(Mathf.Approximately(read.Notes[1].Length, NoteBatch.MaxLengthMs / 1000f) && read.Notes[1].Velocity == 1,
            "long length and zero velocity brought into range");
        // Bad instrument / too many notes / cut off.
        var bad = new ZPackage();
        bad.Write(NoteBatch.Layout);
        bad.Write(1);
        bad.Write(new ZDOID(1L, 1u));
        bad.Write(Vector3.zero);
        bad.Write((byte)9);
        bad.SetPos(0);
        c.Check(!NoteBatch.TryRead(bad, new NoteBatch()), "unknown instrument refused");
        var big = new ZPackage();
        big.Write(NoteBatch.Layout);
        big.Write(1);
        big.Write(new ZDOID(1L, 1u));
        big.Write(Vector3.zero);
        big.Write((byte)1);
        big.Write(0);
        big.Write(0f);
        big.Write((byte)0);
        big.Write((byte)(NoteBatch.MaxNotes + 1));
        big.SetPos(0);
        c.Check(!NoteBatch.TryRead(big, new NoteBatch()), "too many notes refused");
        var cut = batch.ToPackage();
        var bytes = cut.GetArray();
        var shortPkg = new ZPackage(bytes.Length > 10 ? Slice(bytes, bytes.Length - 3) : bytes);
        c.Check(!NoteBatch.TryRead(shortPkg, new NoteBatch()), "cut-off batch refused");
        // Rule selection: client uses the server's rules, pending without them.
        c.Check(ServerRules.Select(true, null, MusicRules.Default).IsPending, "client without server rules: pending");
        c.Check(ServerRules.Select(false, null, MusicRules.Default) == MusicRules.Default, "single player: own rules");
        c.Check(Mathf.Approximately(NoteRelay.RelayRange(TestRules()), 40f + NoteRelay.RangeMargin), "relay range = hearing + margin");
        c.Report();
        yield break;
    }

    private static byte[] Slice(byte[] data, int length)
    {
        var copy = new byte[length];
        Array.Copy(data, copy, length);
        return copy;
    }

    // ---------- music.item ----------

    private static IEnumerator RunItem()
    {
        var c = new Checks(ItemName);
        var player = Player.m_localPlayer;
        var db = ObjectDB.instance;
        if (player == null || db == null)
        {
            SelfTest.Fail(ItemName, "no player or item database");
            yield break;
        }
        c.Note("build: " + InstrumentContent.BuildReport);
        // Default rules in memory (never the config file): the recipes follow them after the rebuild delay.
        ServerRules.TestRules = TestRules();
        yield return new WaitForSeconds(InstrumentContent.RebuildDelaySeconds + 0.3f);
        var expected = new Dictionary<InstrumentKind, string>
        {
            { InstrumentKind.Flute, "FineWood:4" },
            { InstrumentKind.Lyre, "LinenThread:8,Silver:2" },
            { InstrumentKind.Tambourine, "FineWood:3,LeatherScraps:4" },
        };
        foreach (var kind in InstrumentContent.Kinds)
        {
            var name = InstrumentContent.ItemName(kind);
            var prefab = db.GetItemPrefab(name);
            c.Check(prefab != null && prefab == InstrumentContent.Prefab(kind), name + " in the item database");
            c.Check(ZNetScene.instance != null && ZNetScene.instance.GetPrefab(name) == prefab, name + " in the network list");
            var shared = prefab != null ? prefab.GetComponent<ItemDrop>().m_itemData.m_shared : null;
            c.Check(shared != null && shared.m_itemType == ItemDrop.ItemData.ItemType.Tool && shared.m_maxStackSize == 1
                    && shared.m_value == 0 && !shared.m_useDurability && shared.m_buildPieces == null, name + " data");
            c.Check(shared != null && shared.m_icons != null && shared.m_icons.Length > 0 && shared.m_icons[0] != null, name + " icon");
            var recipe = InstrumentContent.RecipeOf(kind);
            c.Check(recipe != null && db.m_recipes.Contains(recipe) && recipe.m_enabled, name + " recipe registered and shown");
            if (recipe != null)
            {
                var parts = new List<string>();
                foreach (var req in recipe.m_resources)
                {
                    parts.Add(req.m_resItem.name + ":" + req.m_amount);
                }
                parts.Sort(StringComparer.Ordinal);
                var got = string.Join(",", parts.ToArray());
                c.Check(got == expected[kind], $"{name} materials {got}");
                c.Check(recipe.m_craftingStation != null && recipe.m_craftingStation.name == MusicRules.Workbench
                        && recipe.m_minStationLevel == ServerRules.Current.StationLevel(kind), name + " station and level");
            }
            // Model under the attach child, with its markers.
            var attach = prefab != null ? prefab.transform.Find("attach") : null;
            var model = attach != null ? attach.Find(InstrumentModels.ModelName) : null;
            c.Check(model != null, name + " model under attach");
            if (model != null)
            {
                var markers = new List<string>();
                foreach (var m in new[] { InstrumentModels.MouthMarker, InstrumentModels.LeftHandMarker, InstrumentModels.StrumMarker, InstrumentModels.BodyMarker })
                {
                    if (FindDeep(model, m) != null)
                    {
                        markers.Add(m);
                    }
                }
                c.Note($"{name} markers: {string.Join(", ", markers.ToArray())}");
                c.Check(markers.Contains(InstrumentModels.LeftHandMarker), name + " has a left hand marker");
                c.Check(model.GetComponentInChildren<Collider>(true) != null, name + " collider");
                var renderer = model.GetComponentInChildren<MeshRenderer>(true);
                c.Check(renderer != null && renderer.sharedMaterials.Length > 0, name + " mesh renderer");
            }
        }
        c.Check(db.GetStatusEffect(InstrumentContent.EffectHash) != null, "Music effect registered");

        var rig = new Rig(player);
        try
        {
            rig.TakeControls();
            rig.Noon();
            rig.Look(player.transform.eulerAngles.y, 0f);
            var torch = rig.Give("Torch");
            foreach (var kind in InstrumentContent.Kinds)
            {
                if (torch != null)
                {
                    player.EquipItem(torch, false);
                }
                var item = rig.Hold(kind);
                yield return new WaitForSeconds(0.4f);
                c.Check(player.GetRightItem() == item && player.GetLeftItem() == null, kind + " takes both hands");
                c.Check(Performance.Held == kind, kind + " known in hand");
                // A click opens the window (or would) and never punches.
                rig.Attack();
                yield return Frames(2);
                rig.TakeControls();
                c.Check(!player.InAttack(), kind + ": no punch on click");
                yield return Frames(2);
                Performance.CloseWindow();
                if (kind == InstrumentKind.Flute)
                {
                    // Feature off: the click goes to the game and the always-on guard stops the punch; recipes hidden.
                    Plugin.TestInactive = true;
                    InstrumentContent.Rebuild();
                    rig.Attack();
                    yield return Frames(3);
                    rig.TakeControls();
                    c.Check(!player.InAttack() && !Performance.WindowOpen, "feature off: click does nothing, no punch");
                    c.Check(!InstrumentContent.RecipeOf(kind).m_enabled, "feature off: recipe hidden");
                    Plugin.TestInactive = false;
                    InstrumentContent.Rebuild();
                    c.Check(InstrumentContent.RecipeOf(kind).m_enabled, "feature back on: recipe shown");
                    yield return Frames(2);
                }
                var head = player.m_eye.position;
                TestCamera.LookAt(head + player.transform.forward * 1.6f + player.transform.right * 0.6f, head - Vector3.up * 0.4f);
                yield return Frames(6);
                SelfTest.Screenshot(ItemName, kind.ToString().ToLowerInvariant() + "-held");
                yield return Frames(2);
                TestCamera.Active = false;
            }
            // Dropped copies side by side.
            var origin = player.transform.position + player.transform.forward * 2f;
            var i = 0;
            foreach (var kind in InstrumentContent.Kinds)
            {
                var go = Object.Instantiate(InstrumentContent.Prefab(kind), origin + player.transform.right * (i - 1) * 0.7f + Vector3.up * 0.4f, Quaternion.identity);
                rig.Spawned(go);
                i++;
            }
            yield return new WaitForSeconds(2f);
            TestCamera.LookAt(origin + Vector3.up * 1.4f - player.transform.forward * 1.2f, origin);
            yield return Frames(6);
            SelfTest.Screenshot(ItemName, "dropped");
            yield return Frames(2);
            TestCamera.Active = false;
            c.Report();
        }
        finally
        {
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
            var found = FindDeep(root.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    // ---------- music.synth ----------

    private static IEnumerator RunSynth()
    {
        var c = new Checks(SynthName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(SynthName, "no player");
            yield break;
        }
        c.Note(AudioKit.DescribeMixer());
        if (!c.Check(AudioKit.Available, "sound available"))
        {
            c.Report();
            yield break;
        }
        c.Check(AudioKit.SfxGroup != null, "SFX mixer group found");
        var emitters = new List<Emitter>();
        try
        {
            foreach (var kind in InstrumentContent.Kinds)
            {
                var e = Emitter.Create(player.transform, new Vector3(0f, 1.4f, 0.2f), kind, true, 40f);
                emitters.Add(e);
                if (!c.Check(e != null, kind + " emitter made"))
                {
                    continue;
                }
                var now = AudioKit.DspNow;
                var pitches = kind == InstrumentKind.Tambourine ? new byte[] { 0, 1, 2, 3 } : new byte[] { 62, 66, 69, 74 };
                for (var i = 0; i < pitches.Length; i++)
                {
                    e.Schedule(now + 0.1 + i * 0.25, now + 0.1 + i * 0.25 + 0.4, pitches[i], 100);
                }
                yield return new WaitForSeconds(0.3f);
                e.ResetMeasure();
                yield return new WaitForSeconds(0.8f);
                c.Note($"{kind}: {e.Callbacks} callbacks, {e.Channels} channels, {e.Frames} frames, rms {F(e.Rms)}, peak {F(e.PeakOut)}");
                c.Check(e.Callbacks > 10 && !e.Failed, kind + " filter runs");
                c.Check(e.Rms > 0.003f && e.PeakOut <= 1.0001f, kind + $" sounds (rms {F(e.Rms)})");
                e.Hush();
                yield return new WaitForSeconds(0.3f);
                e.ResetMeasure();
                yield return new WaitForSeconds(0.3f);
                c.Check(e.PeakOut < 0.0001f, kind + " silent after hush (no DC from the ones clip)");
            }

            // Panning: a 3D source 8 m to the camera's right is louder in the right channel.
            var cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            if (cam != null)
            {
                var side = Emitter.Create(null, player.m_eye.position + cam.right * 8f, InstrumentKind.Flute, false, 40f);
                emitters.Add(side);
                if (side != null)
                {
                    var now = AudioKit.DspNow;
                    side.Schedule(now + 0.05, now + 1.5, 69, 110);
                    yield return new WaitForSeconds(0.4f);
                    side.ResetMeasure();
                    yield return new WaitForSeconds(0.6f);
                    c.Note($"3D source on the right: left peak {F(side.PeakLeft)}, right peak {F(side.PeakRight)}, channels {side.Channels}");
                    if (Plugin.Volume != null && Plugin.Volume.Value <= 0.01f)
                    {
                        c.Note("Volume is 0 in this config: panning not checked");
                    }
                    else
                    {
                        c.Check(side.Channels < 2 || side.PeakRight > side.PeakLeft * 1.3f, "panned to the right");
                        c.Check(side.PeakRight > 0.0005f, "3D source heard");
                    }
                }
            }
            c.Report();
        }
        finally
        {
            foreach (var e in emitters)
            {
                if (e != null)
                {
                    e.Hush();
                    e.Destroy();
                }
            }
        }
    }

    // ---------- music.songs ----------

    private static IEnumerator RunSongs()
    {
        var c = new Checks(SongsName);
        foreach (var entry in SongLibrary.Presets)
        {
            c.Check(entry.Error == null, entry.Id + " parses");
            foreach (var kind in InstrumentContent.Kinds)
            {
                var ok = SongLibrary.Arrange(entry, kind, -1, out var notes, out var length, out var error);
                c.Check(ok && notes.Length > 8 && length > 10f, $"{entry.Id} on the {kind}: {error}");
            }
        }
        var saved = SongLibrary.ConfiguredFolder;
        var folder = Path.Combine(Path.GetTempPath(), "MC_MusicSelfTest");
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
            SongLibrary.ConfiguredFolder = folder;
            var empty = SongLibrary.ScanMidiFolder(out var error);
            c.Check(empty.Count == 0 && error == null && Directory.Exists(folder), "empty songs folder made");
            File.WriteAllBytes(Path.Combine(folder, "test tune.mid"), MidiOf(Preset("greensleeves")));
            var list = SongLibrary.ScanMidiFolder(out error);
            var entry = list.Count == 1 ? list[0] : null;
            c.Check(entry != null && entry.Title == "test tune", "MIDI file listed");
            if (entry != null)
            {
                c.Check(SongLibrary.Load(entry) && entry.Parts.Count == 2, $"MIDI file read: {entry.Info} {entry.Error}");
                foreach (var kind in InstrumentContent.Kinds)
                {
                    c.Check(SongLibrary.Arrange(entry, kind, -1, out var notes, out _, out var e2) && notes.Length > 8,
                        $"MIDI on the {kind}: {e2}");
                }
            }
        }
        finally
        {
            SongLibrary.ConfiguredFolder = saved;
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception)
            {
                // Temp folder: fine to leave.
            }
        }
        c.Report();
        yield break;
    }

    // Format 1 MIDI of a preset: melody (channel 1) and chords (channel 2), 480 ticks per quarter at the preset tempo.
    private static byte[] MidiOf(SongEntry preset)
    {
        var p = preset.Preset;
        var melody = new List<Note>();
        SongText.TryParse(p.Melody, p.Bpm, p.BeatsPerBar, false, melody, out _, out _);
        var chords = new List<Note>();
        SongText.TryParseChords(p.Chords, p.Bpm, chords, out _, out _);
        var ticksPerSecond = 480.0 * p.Bpm / 60.0;
        var ms = new MemoryStream();
        void U32(long v)
        {
            ms.WriteByte((byte)(v >> 24));
            ms.WriteByte((byte)(v >> 16));
            ms.WriteByte((byte)(v >> 8));
            ms.WriteByte((byte)v);
        }
        void Tag(string s)
        {
            foreach (var ch in s)
            {
                ms.WriteByte((byte)ch);
            }
        }
        Tag("MThd");
        U32(6);
        ms.WriteByte(0);
        ms.WriteByte(1);
        ms.WriteByte(0);
        ms.WriteByte(3);
        ms.WriteByte(480 >> 8);
        ms.WriteByte(480 & 0xFF);
        var us = (int)Math.Round(60000000.0 / p.Bpm);
        WriteTrack(ms, new List<byte[]> { Ev(0, 0xFF, 0x51, 3, (byte)(us >> 16), (byte)(us >> 8), (byte)us), Ev(0, 0xFF, 0x2F, 0) });
        WriteTrack(ms, TrackOf(melody, 0, ticksPerSecond));
        WriteTrack(ms, TrackOf(chords, 1, ticksPerSecond));
        return ms.ToArray();
    }

    private static List<byte[]> TrackOf(List<Note> notes, int channel, double ticksPerSecond)
    {
        var events = new List<KeyValuePair<long, byte[]>>();
        foreach (var n in notes)
        {
            var on = (long)Math.Round(n.Time * ticksPerSecond);
            var off = Math.Max(on + 1, (long)Math.Round(n.End * ticksPerSecond) - 1);
            events.Add(new KeyValuePair<long, byte[]>(on, new byte[] { (byte)(0x90 | channel), n.Pitch, n.Velocity }));
            events.Add(new KeyValuePair<long, byte[]>(off, new byte[] { (byte)(0x80 | channel), n.Pitch, 0 }));
        }
        events.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : (a.Value[0] & 0xF0).CompareTo(b.Value[0] & 0xF0));
        var track = new List<byte[]>();
        long last = 0;
        foreach (var e in events)
        {
            track.Add(Ev(e.Key - last, e.Value));
            last = e.Key;
        }
        track.Add(Ev(0, 0xFF, 0x2F, 0));
        return track;
    }

    private static void WriteTrack(MemoryStream ms, List<byte[]> events)
    {
        var body = new MemoryStream();
        foreach (var e in events)
        {
            body.Write(e, 0, e.Length);
        }
        foreach (var ch in "MTrk")
        {
            ms.WriteByte((byte)ch);
        }
        var len = body.Length;
        ms.WriteByte((byte)(len >> 24));
        ms.WriteByte((byte)(len >> 16));
        ms.WriteByte((byte)(len >> 8));
        ms.WriteByte((byte)len);
        body.WriteTo(ms);
    }

    private static byte[] Ev(long delta, params byte[] data)
    {
        var vlq = new List<byte> { (byte)(delta & 0x7F) };
        delta >>= 7;
        while (delta > 0)
        {
            vlq.Insert(0, (byte)((delta & 0x7F) | 0x80));
            delta >>= 7;
        }
        var result = new byte[vlq.Count + data.Length];
        vlq.CopyTo(result, 0);
        data.CopyTo(result, vlq.Count);
        return result;
    }

    // ---------- music.perform ----------

    // Press each chart note's lane just before its time (the press is read on the next frame).
    private static void PressDue(MiniGame game, HashSet<long> pressed, int skipEvery)
    {
        foreach (var v in game.Visible)
        {
            var t = game.NoteTime(v.Key, v.Value);
            var key = (long)v.Key * 100000 + v.Value;
            if (t <= game.Clock + 0.025f && !pressed.Contains(key))
            {
                pressed.Add(key);
                if (skipEvery > 0 && pressed.Count % skipEvery == 0)
                {
                    continue;
                }
                LaneKeys.TestPress[game.Lane(v.Value)] = true;
            }
        }
    }

    private static IEnumerator RunPerform()
    {
        var c = new Checks(PerformName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(PerformName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        try
        {
            rig.TakeControls();
            rig.Noon();
            ServerRules.TestRules = TestRules(6f);
            player.GetSEMan().RemoveStatusEffect(InstrumentContent.EffectHash, true);
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            var song = Preset("kjerringa-med-staven");
            c.Check(Performance.StartMiniGame(song, -1, out var error), "mini-game starts: " + error);
            yield return Frames(2);
            var game = Performance.Game;
            if (!c.Check(game != null && Performance.Mode == PerformanceMode.MiniGame, "mini-game running"))
            {
                c.Report();
                yield break;
            }
            c.Check(KeyCapture.Active && Chat.instance != null && Chat.instance.HasFocus(), "game keys held back");
            var view = player.m_nview;
            c.Check(view != null && view.GetZDO().GetInt(InstrumentPose.PlayingKey) == (int)InstrumentKind.Flute, "playing flag set");
            var pressed = new HashSet<long>();
            var start = Time.time;
            var shot = false;
            while (game.Encores == 0 && Time.time - start < 30f && Performance.Mode == PerformanceMode.MiniGame)
            {
                PressDue(game, pressed, 0);
                if (!shot && game.Clock > Judge.LeadIn + 3f)
                {
                    shot = true;
                    SelfTest.Screenshot(PerformName, "playing");
                }
                yield return null;
            }
            c.Note($"hits {game.Judge.Hits}, perfect {game.Judge.Perfects}, misses {game.Judge.Misses}, strays {game.Judge.Strays}, "
                   + $"accuracy {F(game.Accuracy)}, meter {F(game.Meter.Fraction)}, encores {game.Encores}, time {F(Time.time - start)} s");
            c.Check(game.Judge.Hits >= 8 && game.Judge.Misses <= 1 && game.Judge.Strays == 0, "simulated presses hit");
            c.Check(game.Encores >= 1, "Encore after the meter filled");
            var seman = player.GetSEMan();
            c.Check(seman.HaveStatusEffect(InstrumentContent.EffectHash), "Music effect on the performer");
            var comfortField = player.m_comfortLevel;
            var comfort = player.GetComfortLevel();
            c.Note($"comfort {comfort} (base {comfortField})");
            c.Check(comfortField <= 0 || comfort == comfortField + 3, "comfort +3 with the effect");
            var emitter = Performance.LocalEmitter;
            c.Check(emitter != null && emitter.Callbacks > 0, "own notes sound");
            c.Check(Performance.BatchesSent > 0, "notes streamed (batches made)");
            // Rested length follow the comfort with Music, also outdoors (test world: no shelter, comfort 1 + 3).
            c.Note($"in shelter {player.InShelter()}");
            var hadRested = seman.HaveStatusEffect(SEMan.s_statusEffectRested);
            seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
            var rested = seman.AddStatusEffect(SEMan.s_statusEffectRested, resetTime: true) as SE_Rested;
            if (rested != null)
            {
                var wanted = rested.m_baseTTL + (player.GetComfortLevel() - 1) * rested.m_TTLPerComfortLevel;
                c.Note($"Rested {F(rested.m_ttl)} s (base {F(rested.m_baseTTL)} s, {F(rested.m_TTLPerComfortLevel)} s per comfort)");
                c.Check(Mathf.Approximately(rested.m_ttl, wanted) && rested.m_ttl >= rested.m_baseTTL + 3f * rested.m_TTLPerComfortLevel - 0.5f,
                    "Rested lasts 3 comfort levels longer with Music");
            }
            else
            {
                c.Check(false, "Rested effect added");
            }
            if (!hadRested)
            {
                seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
            }
            // Stop: everything back.
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.None && Performance.Game == null, "stopped");
            c.Check(view.GetZDO().GetInt(InstrumentPose.PlayingKey) == 0, "playing flag cleared");
            yield return Frames(2);
            c.Check(!KeyCapture.Active, "keys given back");
            // A poor player (every third note skipped... and strays) never fills the meter quickly.
            c.Check(Performance.StartMiniGame(song, -1, out error), "second run starts: " + error);
            yield return Frames(2);
            game = Performance.Game;
            pressed.Clear();
            start = Time.time;
            while (Time.time - start < 9f && game != null && Performance.Mode == PerformanceMode.MiniGame)
            {
                PressDue(game, pressed, 2);
                yield return null;
            }
            c.Check(game != null && game.Encores == 0, $"half the notes: no Encore (accuracy {F(game != null ? game.Accuracy : -1f)})");
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- music.autoplay ----------

    private static IEnumerator RunAutoplay()
    {
        var c = new Checks(AutoplayName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(AutoplayName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            rig.Hold(InstrumentKind.Lyre);
            yield return new WaitForSeconds(0.4f);
            c.Check(Performance.StartAuto(Preset("hearthfire-lullaby"), -1, out var error), "autoplay starts: " + error);
            yield return new WaitForSeconds(1.5f);
            var emitter = Performance.LocalEmitter;
            if (emitter != null)
            {
                emitter.ResetMeasure();
            }
            yield return new WaitForSeconds(1.5f);
            c.Check(Performance.Mode == PerformanceMode.Auto, "playing by itself");
            c.Check(emitter != null && emitter.Rms > 0.002f, $"lyre heard (rms {F(emitter != null ? emitter.Rms : 0f)})");
            c.Check(player.m_nview.GetZDO().GetInt(InstrumentPose.PlayingKey) == (int)InstrumentKind.Lyre, "playing flag set");
            c.Check(Performance.PoseWeight > 0.9f, "pose fully in");
            c.Check(!KeyCapture.Active, "keys free while a song plays by itself");
            var musicVolume = Plugin.GameMusicVolume != null ? Plugin.GameMusicVolume.Value : 0.3f;
            if (musicVolume < 0.95f)
            {
                c.Check(Listeners.Duck < 0.999f, "game music fading");
            }
            else
            {
                c.Note("GameMusicVolume is about 1 in this config: music fade not checked");
            }
            // Unequip: stops at once.
            player.UnequipItem(player.GetRightItem(), false);
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.None, "putting the lyre away stops the song");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- music.listen ----------

    private static IEnumerator RunListen()
    {
        var c = new Checks(ListenName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(ListenName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        try
        {
            ServerRules.TestRules = TestRules();
            player.GetSEMan().RemoveStatusEffect(InstrumentContent.EffectHash, true);
            var other = new ZDOID(424242L, 7u);
            var place = player.transform.position + player.transform.right * 6f;
            var batch = new NoteBatch
            {
                Performance = 99,
                Performer = other,
                Position = place,
                Instrument = InstrumentKind.Lyre,
                SentAt = 0f,
            };
            for (var i = 0; i < 8; i++)
            {
                batch.Notes.Add(new Note(0.2f + i * 0.25f, 0.5f, (byte)(57 + i * 2), 100));
            }
            // Performer clock 0 = now (their batches carry it as SentAt).
            var t0 = AudioKit.DspNow;
            float Perf() => (float)(AudioKit.DspNow - t0);
            var scheduledBefore = Listeners.NotesScheduled;
            Listeners.Receive(batch);
            c.Check(Listeners.RemoteCount == 1, "other player's performance heard");
            c.Check(Listeners.NotesScheduled - scheduledBefore == 8, "all notes scheduled");
            yield return new WaitForSeconds(0.6f);
            var emitter = Listeners.FirstEmitter;
            if (emitter != null)
            {
                emitter.ResetMeasure();
            }
            yield return new WaitForSeconds(0.8f);
            c.Check(emitter != null && emitter.Rms > 0.0005f, $"heard at their place (rms {F(emitter != null ? emitter.Rms : 0f)})");
            c.Check(emitter != null && (emitter.transform.position - place).magnitude < 0.5f, "sound where they stand");
            // Encore from them, in range: the effect is ours.
            var encore = new NoteBatch { Performance = 99, Performer = other, Position = place, Instrument = InstrumentKind.Lyre, SentAt = Perf(), Flags = BatchFlags.Encore };
            Listeners.Receive(encore);
            c.Check(player.GetSEMan().HaveStatusEffect(InstrumentContent.EffectHash), "Encore in range gives the Music effect");
            player.GetSEMan().RemoveStatusEffect(InstrumentContent.EffectHash, true);
            // Too far: no effect.
            var far = new NoteBatch { Performance = 99, Performer = other, Position = player.transform.position + Vector3.forward * 60f, Instrument = InstrumentKind.Lyre, SentAt = Perf(), Flags = BatchFlags.Encore };
            Listeners.Receive(far);
            c.Check(!player.GetSEMan().HaveStatusEffect(InstrumentContent.EffectHash), "Encore out of range gives nothing");
            // Note far from its batch time: dropped (the far Encore let the performance go: this one starts it over).
            var droppedBefore = Listeners.NotesDropped;
            var late = new NoteBatch { Performance = 99, Performer = other, Position = place, Instrument = InstrumentKind.Lyre, SentAt = Perf() };
            late.Notes.Add(new Note(late.SentAt - 3f, 0.2f, 60, 90));
            late.Notes.Add(new Note(late.SentAt + 0.1f, 0.2f, 62, 90));
            Listeners.Receive(late);
            c.Check(Listeners.NotesDropped > droppedBefore, "note far from its batch time dropped");
            c.Check(Listeners.NewestExtraDelay < 0.01f, $"restart keeps the old timing (extra delay {F(Listeners.NewestExtraDelay)} s)");
            // Stall: after 2 s of nothing, a batch 1.5 s late. Its notes drop (never a burst), the timing stays.
            yield return new WaitForSeconds(2.2f);
            droppedBefore = Listeners.NotesDropped;
            var stalled = new NoteBatch { Performance = 99, Performer = other, Position = place, Instrument = InstrumentKind.Lyre, SentAt = Perf() - 1.5f };
            stalled.Notes.Add(new Note(stalled.SentAt + 0.05f, 0.2f, 64, 90));
            Listeners.Receive(stalled);
            c.Check(Listeners.NotesDropped > droppedBefore, "stalled batch: late note dropped");
            c.Check(Listeners.NewestExtraDelay <= Listeners.MaxDelay, $"stall: delay stays bounded (extra {F(Listeners.NewestExtraDelay)} s)");
            // End: let go, emitter freed after the tails.
            var end = new NoteBatch { Performance = 99, Performer = other, Position = place, Instrument = InstrumentKind.Lyre, SentAt = Perf(), Flags = BatchFlags.End };
            Listeners.Receive(end);
            // Nothing more under an ended id (a hostile stream would make a new sound source each time).
            scheduledBefore = Listeners.NotesScheduled;
            var afterEnd = new NoteBatch { Performance = 99, Performer = other, Position = place, Instrument = InstrumentKind.Lyre, SentAt = Perf() };
            afterEnd.Notes.Add(new Note(afterEnd.SentAt + 0.1f, 0.2f, 60, 90));
            Listeners.Receive(afterEnd);
            c.Check(Listeners.NotesScheduled == scheduledBefore, "notes after End ignored");
            yield return WaitFor(() => Listeners.RemoteCount == 0, 12f);
            c.Check(Listeners.RemoteCount == 0, "performance over: emitter freed");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- music.window ----------

    private static IEnumerator RunWindow()
    {
        var c = new Checks(WindowName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(WindowName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            rig.Hold(InstrumentKind.Tambourine);
            yield return new WaitForSeconds(0.4f);
            Performance.TestRequestOpen();
            yield return Frames(3);
            c.Check(Performance.WindowOpen && SongWindow.IsOpen, "song window open");
            c.Check(KeyCapture.Active, "game keys held back while it is open");
            yield return Frames(4);
            SelfTest.Screenshot(WindowName, "window");
            yield return Frames(2);
            c.Note(SongWindow.DescribeLayout());
            SongWindow.TestSelect("preset:row-the-longship");
            yield return Frames(2);
            SongWindow.TestPress("Perform");
            yield return Frames(3);
            c.Check(!Performance.WindowOpen && Performance.Mode == PerformanceMode.MiniGame, "Perform starts the mini-game");
            yield return new WaitForSeconds(Judge.LeadIn + 1.5f);
            SelfTest.Screenshot(WindowName, "minigame");
            yield return Frames(2);
            c.Note(MiniGameHud.DescribeLayout());
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.None, "stopped");
            // Autoplay status line.
            c.Check(Performance.StartAuto(Preset("ravens-jig"), -1, out var error), "autoplay starts: " + error);
            yield return new WaitForSeconds(1.5f);
            SelfTest.Screenshot(WindowName, "autoplay");
            yield return Frames(2);
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- music.pose ----------

    private static IEnumerator RunPose()
    {
        var c = new Checks(PoseName);
        var player = Player.m_localPlayer;
        if (player == null || GameCamera.instance == null)
        {
            SelfTest.Fail(PoseName, "no player or camera");
            yield break;
        }
        var rig = new Rig(player);
        try
        {
            rig.TakeControls();
            rig.Noon();
            ServerRules.TestRules = TestRules();
            rig.Look(player.transform.eulerAngles.y, 0f);
            var songs = new Dictionary<InstrumentKind, string>
            {
                { InstrumentKind.Flute, "vem-kan-segla" },
                { InstrumentKind.Lyre, "greensleeves" },
                { InstrumentKind.Tambourine, "mead-hall-reel" },
            };
            foreach (var kind in InstrumentContent.Kinds)
            {
                var label = kind.ToString().ToLowerInvariant();
                rig.Hold(kind);
                yield return new WaitForSeconds(0.5f);
                c.Check(Performance.StartAuto(Preset(songs[kind]), -1, out var error), label + " plays: " + error);
                yield return new WaitForSeconds(1.2f);
                yield return new WaitForEndOfFrame();
                c.Note(label + ": " + InstrumentPose.Describe(player));
                if (kind == InstrumentKind.Flute && InstrumentPose.TryGetMarker(player, InstrumentModels.MouthMarker, out var mouth))
                {
                    var gap = (mouth - InstrumentPose.MouthTarget(player)).magnitude;
                    c.Check(gap < 0.08f, $"flute at the mouth ({F(gap)} m)");
                }
                c.Check(InstrumentPose.TryGetMarker(player, InstrumentModels.LeftHandMarker, out _), label + " left hand marker found");
                var leftGap = InstrumentPose.LeftHandGap(player);
                c.Check(leftGap >= 0f && leftGap < 0.06f, $"{label}: left hand on the instrument ({F(leftGap)} m)");
                if (kind == InstrumentKind.Lyre)
                {
                    c.Check(InstrumentPose.InstanceMoved(player), "lyre placed against the chest");
                }
                var head = player.m_eye.position;
                var fwd = player.transform.forward;
                var right = player.transform.right;
                TestCamera.LookAt(head + fwd * 1.7f + right * 0.5f, head - Vector3.up * 0.35f);
                yield return Frames(6);
                SelfTest.Screenshot(PoseName, label + "-front");
                yield return Frames(2);
                TestCamera.LookAt(head + right * 1.7f + fwd * 0.3f, head - Vector3.up * 0.35f);
                yield return Frames(6);
                SelfTest.Screenshot(PoseName, label + "-side");
                yield return Frames(2);
                // The way other players' games pose it.
                InstrumentPose.TestAsRemote = true;
                yield return new WaitForSeconds(0.8f);
                TestCamera.LookAt(head + fwd * 1.7f - right * 0.5f, head - Vector3.up * 0.35f);
                yield return Frames(6);
                SelfTest.Screenshot(PoseName, label + "-remote");
                yield return Frames(2);
                c.Note(label + " remote: " + InstrumentPose.Describe(player));
                InstrumentPose.TestAsRemote = false;
                if (kind == InstrumentKind.Lyre)
                {
                    // Seated by the fire: sit emote, the song goes on, the lyre rests lower.
                    player.StartEmote("sit", false);
                    yield return new WaitForSeconds(1.5f);
                    c.Check(Performance.Mode == PerformanceMode.Auto, "lyre: still playing after sitting down");
                    c.Note("seated: sitting " + player.IsSitting() + "; " + InstrumentPose.Describe(player));
                    var low = player.m_eye.position;
                    TestCamera.LookAt(low + player.transform.forward * 1.6f + player.transform.right * 0.5f, low - Vector3.up * 0.3f);
                    yield return Frames(6);
                    SelfTest.Screenshot(PoseName, "lyre-seated");
                    yield return Frames(2);
                    player.StopEmote();
                    yield return new WaitForSeconds(1f);
                }
                TestCamera.Active = false;
                Performance.TestRequestStop();
                yield return new WaitForSeconds(0.6f);
                if (kind == InstrumentKind.Lyre)
                {
                    c.Check(!InstrumentPose.InstanceMoved(player), "lyre back in the hand after stopping");
                }
                player.UnequipItem(player.GetRightItem(), false);
                yield return Frames(3);
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- music.export ----------

    private static IEnumerator RunExport()
    {
        var c = new Checks(ExportName);
        var probe = SelfTest.ShotPath(ExportName, "probe");
        if (probe != null)
        {
            var dir = Path.GetDirectoryName(probe);
            foreach (var png in InstrumentIcons.ExportPngs())
            {
                var path = Path.Combine(dir, "music-" + png.Key);
                File.WriteAllBytes(path, png.Value);
                c.Note("wrote " + path);
            }
        }
        else
        {
            c.Note("no screenshot folder: icons not written");
        }
        var player = Player.m_localPlayer;
        var animator = player != null ? player.m_animator : null;
        if (animator != null && animator.isHuman)
        {
            var bones = new List<string>();
            foreach (var b in new[] { HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightIndexProximal })
            {
                var t = animator.GetBoneTransform(b);
                bones.Add(b + "=" + (t != null ? t.name : "-"));
            }
            c.Note("bones: " + string.Join(", ", bones.ToArray()));
        }
        c.Check(InstrumentIcons.Get(InstrumentKind.Flute) != null && InstrumentIcons.EffectIcon() != null, "icons made");
        c.Report();
        yield break;
    }
#endif
}
