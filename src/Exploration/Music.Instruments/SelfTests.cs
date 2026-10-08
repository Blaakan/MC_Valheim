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
// More tests, one per item of TESTING.md where the game state can show it (SelfTests.Solo.cs, tools in
// SelfTests.Kit.cs), and the tests of a multiplayer run with their server halves (SelfTests.Multi.cs).
internal static partial class SelfTests
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
    private const string ShareName = "music.share";
    private const string FreePlayName = "music.freeplay";
    private const string PoseName = "music.pose";
    private const string ExportName = "music.export";

    private static readonly string[] Names =
    {
        NetworkName, ItemName, SynthName, SongsName, PerformName, AutoplayName, ListenName, WindowName, ShareName,
        FreePlayName, PoseName, ExportName,
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
        SelfTest.Register(ShareName, RunShare);
        SelfTest.Register(FreePlayName, RunFreePlay);
        SelfTest.Register(PoseName, RunPose);
        SelfTest.Register(ExportName, RunExport);
        LogTap.Install();
        RegisterSolo();
        RegisterMulti();
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
        UnregisterSolo();
        UnregisterMulti();
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
        Plugin.TestVolume = null;
        Plugin.TestGameMusicVolume = null;
        Plugin.TestNoteSpeed = null;
        PadInput.Test = null;
        SongWindow.TestHeldKey = KeyCode.None;
        SongWindow.TestDownKey = KeyCode.None;
        Performance.TestRightClick = false;
        FreePlayKeys.ClearTest();
        for (var i = 0; i < LaneKeys.TestPress.Length; i++)
        {
            LaneKeys.TestPress[i] = false;
        }
        // Lane keys a test gave: the player's own again.
        LaneKeys.Cache();
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
            SuccessAccuracy = 0.5f,
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
        c.Check(Mathf.Approximately(read.Notes[1].Length, NoteBatch.MaxLengthMs / 1000f) && read.Notes[1].Velocity == 0,
            "long length brought into range, velocity 0 kept (note off, layout 2)");
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

    // Same song with one more track holding only a long text event: bigger file, same notes.
    private static byte[] PadMidi(byte[] midi, int textBytes)
    {
        var ms = new MemoryStream();
        ms.Write(midi, 0, midi.Length);
        var text = new List<byte>();
        text.AddRange(Ev(0, 0xFF, 0x01));
        text.AddRange(Ev(textBytes));
        for (var i = 0; i < textBytes; i++)
        {
            text.Add((byte)'a');
        }
        WriteTrack(ms, new List<byte[]> { text.ToArray(), Ev(0, 0xFF, 0x2F, 0) });
        var bytes = ms.ToArray();
        var tracks = (bytes[10] << 8) | bytes[11];
        tracks++;
        bytes[10] = (byte)(tracks >> 8);
        bytes[11] = (byte)tracks;
        return bytes;
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

    // ---------- music.freeplay ----------

    // Free play: piano key map, window entry, held flute note ends on key up (legato, Space = octave up), HUD keys lit,
    // live batches, no comfort, tambourine keys, stop; listener: a note off on the wire ends a long note early.
    private static IEnumerator RunFreePlay()
    {
        var c = new Checks(FreePlayName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(FreePlayName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            FreePlayKeys.ClearTest();
            player.GetSEMan().RemoveStatusEffect(InstrumentContent.EffectHash, true);

            // Key map (pure): piano keys, Z = C4 on the flute, Q = C5, P = E6; Space an octave up.
            c.Check(FreePlayMap.Pitch(InstrumentKind.Flute, 0, false) == 60 && FreePlayMap.Pitch(InstrumentKind.Flute, 12, false) == 72
                    && FreePlayMap.Pitch(InstrumentKind.Flute, 28, false) == 88 && FreePlayMap.Pitch(InstrumentKind.Flute, 12, true) == 84,
                "flute piano: C4 to E6, Space an octave up");
            c.Check(FreePlayMap.Pitch(InstrumentKind.Lyre, 0, false) == 48 && FreePlayMap.Pitch(InstrumentKind.Lyre, 1, false) == 49,
                "lyre piano from C3, black keys are the sharps");
            c.Check(FreePlayMap.IsBlack(1) && !FreePlayMap.IsBlack(4) && FreePlayMap.WhiteIndex(1) == 0 && FreePlayMap.WhiteIndex(2) == 1
                    && FreePlayMap.WhiteIndex(28) == FreePlayMap.WhiteCount - 1 && FreePlayKeys.Key(1) == KeyCode.S
                    && FreePlayKeys.Key(13) == KeyCode.Alpha2,
                "piano layout: S is C#, 2 is the upper C#, 17 white keys");
            c.Check(FreePlayMap.Pitch(InstrumentKind.Tambourine, 2, false) == (int)TambourineHit.Hit
                    && FreePlayMap.Pitch(InstrumentKind.Tambourine, 5, false) == (int)TambourineHit.Shake
                    && FreePlayMap.Pitch(InstrumentKind.Tambourine, 1, false) < 0 && FreePlayMap.Pitch(InstrumentKind.Tambourine, 7, false) < 0,
                "tambourine: Z X C V only");

            // From the window's first entry.
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            Performance.TestRequestOpen();
            yield return Frames(3);
            c.Check(SongWindow.IsOpen && SongWindow.TestSelect(SongLibrary.FreePlayId), "Free play in the song window");
            yield return Frames(2);
            SongWindow.TestPress("Play");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.FreePlay && KeyCapture.Active, "free play runs, game keys held back");
            c.Check(MiniGameHud.FreeShown, "free play keys shown: " + MiniGameHud.FreeTitle);
            var sentBefore = Performance.BatchesSent;
            var emitter = Performance.LocalEmitter;
            var onsBefore = Performance.NoteOnsSent;
            var offsBefore = Performance.NoteOffsSent;

            // Q (upper C): C5, held.
            FreePlayKeys.TestDown[12] = true;
            yield return Frames(3);
            c.Check(Performance.FreeFlutePitch == 72, "held flute key: C5 (" + Performance.FreeFlutePitch + ")");
            c.Check(MiniGameHud.FreeLitCount == 1, "its key box lit");
            var last = Performance.SentBack(1);
            c.Check(Performance.NoteOnsSent == onsBefore + 1 && last.Key == 72 && last.Value > 0, $"note on 72 sent ({last.Key}, {last.Value})");
            yield return new WaitForSeconds(0.3f);
            SelfTest.Screenshot(FreePlayName, "flute");
            if (emitter != null)
            {
                emitter.ResetMeasure();
            }
            yield return new WaitForSeconds(0.4f);
            c.Check(emitter != null && emitter.Rms > 0.001f, $"held flute note sounds (rms {F(emitter != null ? emitter.Rms : 0f)})");
            c.Check(Performance.BatchesSent > sentBefore && (Performance.LastFlags & BatchFlags.Live) != 0, "streamed live");
            FreePlayKeys.TestUp[12] = true;
            yield return Frames(2);
            last = Performance.SentBack(1);
            c.Check(Performance.NoteOffsSent == offsBefore + 1 && last.Key == 72 && last.Value == 0, $"key up sends the note off for 72 ({last.Key}, {last.Value})");
            yield return new WaitForSeconds(0.5f);
            if (emitter != null)
            {
                emitter.ResetMeasure();
            }
            yield return new WaitForSeconds(0.4f);
            c.Check(emitter != null && emitter.Rms < 0.0002f, $"key up ends the flute note (rms {F(emitter != null ? emitter.Rms : 0f)})");
            c.Check(Performance.FreeFlutePitch < 0 && MiniGameHud.FreeLitCount == 0, "nothing held");

            // One flute note at a time: the new key takes over; letting go of the old one keeps the new note.
            FreePlayKeys.TestDown[12] = true;
            yield return Frames(2);
            FreePlayKeys.TestDown[16] = true;
            yield return Frames(2);
            c.Check(Performance.FreeFlutePitch == 76, "second flute key (E) takes over: E5 (" + Performance.FreeFlutePitch + ")");
            var before = Performance.SentBack(2);
            last = Performance.SentBack(1);
            c.Check(before.Key == 72 && before.Value == 0 && last.Key == 76 && last.Value > 0, "takeover: note off 72 then note on 76");
            var offsTakeover = Performance.NoteOffsSent;
            FreePlayKeys.TestUp[12] = true;
            yield return Frames(2);
            c.Check(Performance.FreeFlutePitch == 76 && Performance.NoteOffsSent == offsTakeover,
                "letting go of the first key keeps the second note (no note off)");
            FreePlayKeys.TestUp[16] = true;
            yield return Frames(2);

            // Down and up in the same frame (a quick tap during a hitch): a short note, nothing left held.
            var tapOns = Performance.NoteOnsSent;
            var tapOffs = Performance.NoteOffsSent;
            FreePlayKeys.TestDown[14] = true;
            FreePlayKeys.TestUp[14] = true;
            yield return Frames(2);
            last = Performance.SentBack(1);
            c.Check(Performance.FreeFlutePitch < 0 && Performance.NoteOnsSent == tapOns + 1 && Performance.NoteOffsSent == tapOffs
                    && last.Key == 74, $"same-frame tap: short D5, nothing held ({last.Key}, held {Performance.FreeFlutePitch})");
            yield return new WaitForSeconds(0.4f);
            if (emitter != null)
            {
                emitter.ResetMeasure();
            }
            yield return new WaitForSeconds(0.3f);
            c.Check(emitter != null && emitter.Rms < 0.0002f, $"the tap does not keep sounding (rms {F(emitter != null ? emitter.Rms : 0f)})");

            // Space held = octave up.
            FreePlayKeys.TestOctave = true;
            FreePlayKeys.TestDown[12] = true;
            yield return Frames(2);
            c.Check(Performance.FreeFlutePitch == 84, "Space: octave up, C6 (" + Performance.FreeFlutePitch + ")");
            c.Check(MiniGameHud.FreeTitle != null && MiniGameHud.FreeTitle.Contains("octave up"), "piano shows the octave up");
            yield return Frames(2);
            SelfTest.Screenshot(FreePlayName, "octave");
            FreePlayKeys.TestUp[12] = true;
            FreePlayKeys.TestOctave = false;
            yield return Frames(2);
            c.Check(!player.GetSEMan().HaveStatusEffect(InstrumentContent.EffectHash), "free play gives no Music effect");
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.None && !MiniGameHud.FreeShown, "stopped, keys panel gone");
            yield return Frames(2);
            c.Check(!KeyCapture.Active, "keys given back");

            // Tambourine: its four keys only.
            rig.Hold(InstrumentKind.Tambourine);
            yield return new WaitForSeconds(0.4f);
            c.Check(Performance.StartFreePlay(out var error), "free play on the tambourine: " + error);
            yield return Frames(2);
            var drum = Performance.LocalEmitter; // a new source: the instrument changed
            if (drum != null)
            {
                drum.ResetMeasure();
            }
            var drumOns = Performance.NoteOnsSent;
            FreePlayKeys.TestDown[2] = true;
            yield return Frames(2);
            c.Check(MiniGameHud.FreeLitCount == 1, "tambourine Hit key (X) lit");
            last = Performance.SentBack(1);
            c.Check(Performance.NoteOnsSent == drumOns + 1 && last.Key == (byte)TambourineHit.Hit, $"X sends a Hit ({last.Key})");
            yield return new WaitForSeconds(0.1f);
            c.Check(drum != null && drum.Rms > 0.001f, $"the hit sounds (rms {F(drum != null ? drum.Rms : 0f)})");
            yield return Frames(2);
            SelfTest.Screenshot(FreePlayName, "tambourine");
            FreePlayKeys.TestUp[2] = true;
            FreePlayKeys.TestDown[1] = true;
            yield return Frames(2);
            c.Check(MiniGameHud.FreeLitCount == 0 && Performance.NoteOnsSent == drumOns + 1, "a key outside its four plays nothing");
            FreePlayKeys.TestUp[1] = true;
            yield return Frames(2);
            Performance.TestRequestStop();
            yield return Frames(3);

            // Listener: a held note (8 s) heard, then a note off on the wire ends it.
            var other = new ZDOID(434343L, 9u);
            var place = player.transform.position + player.transform.right * 5f;
            var on = new NoteBatch { Performance = 77, Performer = other, Position = place, Instrument = InstrumentKind.Flute, SentAt = 0f, Flags = BatchFlags.Live };
            on.Notes.Add(new Note(0.05f, 8f, 72, 100));
            Listeners.Receive(on);
            yield return new WaitForSeconds(0.8f);
            var remote = Listeners.FirstEmitter;
            if (remote != null)
            {
                remote.ResetMeasure();
            }
            yield return new WaitForSeconds(0.3f);
            c.Check(remote != null && remote.Rms > 0.0005f, $"listener hears the held note (rms {F(remote != null ? remote.Rms : 0f)})");
            var off = new NoteBatch { Performance = 77, Performer = other, Position = place, Instrument = InstrumentKind.Flute, SentAt = 1.1f, Flags = BatchFlags.Live };
            off.Notes.Add(new Note(1.1f, 0f, 72, 0));
            var offPkg = off.ToPackage();
            offPkg.SetPos(0);
            var offBack = new NoteBatch();
            c.Check(NoteBatch.TryRead(offPkg, offBack) && offBack.Notes.Count == 1 && offBack.Notes[0].Velocity == 0,
                "note off kept on the wire (velocity 0)");
            Listeners.Receive(off);
            yield return new WaitForSeconds(0.7f);
            if (remote != null)
            {
                remote.ResetMeasure();
            }
            yield return new WaitForSeconds(0.4f);
            c.Check(remote != null && remote.Rms < 0.0002f, $"note off ends the listener's note (rms {F(remote != null ? remote.Rms : 0f)})");
            var end = new NoteBatch { Performance = 77, Performer = other, Position = place, Instrument = InstrumentKind.Flute, SentAt = 2.3f, Flags = BatchFlags.End };
            Listeners.Receive(end);
            yield return WaitFor(() => Listeners.RemoteCount == 0, 6f);
            c.Check(Listeners.RemoteCount == 0, "listener sound source freed soon after End (note off ended the 8 s note)");
            c.Report();
        }
        finally
        {
            FreePlayKeys.ClearTest();
            rig.Restore();
        }
    }

    // ---------- music.share ----------

    // Server songs: folder listing, list and pieces through the real packages (no network: server packages fed to the
    // client side), bad pieces, host window, the server's AllowPlayerSongs rule, sharing off.
    private static IEnumerator RunShare()
    {
        var c = new Checks(ShareName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(ShareName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var folder = Path.Combine(Path.GetTempPath(), "MC_MusicShareTest");
        var ownFolder = Path.Combine(Path.GetTempPath(), "MC_MusicShareOwn");
        var savedOwn = SongLibrary.ConfiguredFolder;
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            foreach (var f in new[] { folder, ownFolder })
            {
                if (Directory.Exists(f))
                {
                    Directory.Delete(f, true);
                }
                Directory.CreateDirectory(f);
            }
            var tune = MidiOf(Preset("greensleeves"));
            var big = PadMidi(MidiOf(Preset("ravens-jig")), 3 * SongShare.ChunkBytes + 123);
            File.WriteAllBytes(Path.Combine(folder, "Shared tune.mid"), tune);
            File.WriteAllBytes(Path.Combine(folder, "Copy of shared tune.mid"), tune);
            File.WriteAllBytes(Path.Combine(folder, "Long jig.mid"), big);
            File.WriteAllBytes(Path.Combine(folder, "notes.txt"), new byte[] { 1, 2, 3 });
            SongShare.TestFolder = folder;
            SongShare.TestShare = true;
            SongShare.TestScan();
            c.Check(SongShare.SharedCount == 2, $"server lists 2 songs (same bytes once, .txt skipped): {SongShare.SharedCount}");

            // List through its package to the client view.
            var listPkg = SongShare.ListPackage();
            listPkg.SetPos(0);
            c.Check(SongShare.ReceiveList(listPkg), "list read back");
            SongEntry small = null;
            SongEntry large = null;
            foreach (var e in SongShare.ClientList)
            {
                if (e.ServerSize == tune.Length)
                {
                    small = e;
                }
                else if (e.ServerSize == big.Length)
                {
                    large = e;
                }
            }
            c.Check(SongShare.ClientList.Count == 2 && small != null && large != null, "client sees both server songs");
            c.Check(small != null && small.ServerHash == MusicMath.Fnv1a(tune) && small.Title == "Copy of shared tune",
                "song id = hash of its bytes, first name kept: " + (small != null ? small.Title : "-"));

            // A song bigger than one piece: several pieces, progress, then read like a file.
            if (large != null)
            {
                SongShare.TestForget(large.ServerHash);
                SongShare.TestBegin(large);
                var request = SongShare.TestRequest;
                // A piece of an older pick still on its way: ignored, the download goes on.
                var stale = SongShare.TestPieces(large.ServerHash, request - 1);
                stale[stale.Count - 1].SetPos(0);
                c.Check(!SongShare.ReceiveChunk(stale[stale.Count - 1]) && large.Pending != null
                        && large.Pending.StartsWith("Downloading", StringComparison.Ordinal), "older pick's piece ignored: " + large.Pending);
                var pieces = SongShare.TestPieces(large.ServerHash, request);
                c.Check(pieces.Count >= 4 && pieces.Count == (big.Length + SongShare.ChunkBytes - 1) / SongShare.ChunkBytes,
                    $"sent in {pieces.Count} pieces of {SongShare.ChunkBytes} bytes ({big.Length} bytes)");
                var taken = 0;
                for (var i = 0; i < pieces.Count; i++)
                {
                    pieces[i].SetPos(0);
                    if (SongShare.ReceiveChunk(pieces[i]))
                    {
                        taken++;
                    }
                    if (i == 0)
                    {
                        c.Check(large.Pending != null && large.Pending.Contains("%"), "progress shown: " + large.Pending);
                    }
                }
                c.Check(taken == pieces.Count && large.Score != null && large.Error == null && large.Pending == null
                        && large.Parts.Count >= 1, $"downloaded song read: {large.Info} {large.Error} {large.Pending}");
                c.Check(SongShare.CachedCount >= 1, "kept for the session");
                c.Check(SongLibrary.Arrange(large, InstrumentKind.Lyre, -1, out var notes, out _, out var arrangeError)
                        && notes.Length > 8, "downloaded song arranged: " + arrangeError);
            }

            // Bad pieces never load a song.
            if (small != null)
            {
                SongShare.TestForget(small.ServerHash);
                SongShare.TestBegin(small);
                var skipped = SongShare.ChunkPackage(small.ServerHash, SongShare.TestRequest, tune, 100);
                skipped.SetPos(0);
                c.Check(!SongShare.ReceiveChunk(skipped) && small.Score == null && small.Pending != null
                        && small.Pending.Contains("broke off"), "piece out of order refused: " + small.Pending);
                SongShare.TestBegin(small);
                var damaged = (byte[])tune.Clone();
                damaged[damaged.Length - 5] ^= 0x55;
                var damagedPkg = SongShare.ChunkPackage(small.ServerHash, SongShare.TestRequest, damaged, 0);
                damagedPkg.SetPos(0);
                SongShare.ReceiveChunk(damagedPkg);
                c.Check(small.Score == null && small.Pending != null && small.Pending.Contains("damaged"),
                    "damaged song refused: " + small.Pending);
                SongShare.TestBegin(small);
                var gone = SongShare.TestStatus(small.ServerHash, SongShare.TestRequest, SongShare.StatusGone);
                gone.SetPos(0);
                SongShare.ReceiveChunk(gone);
                c.Check(small.Score == null && small.Pending != null && small.Pending.Contains("no longer"),
                    "song no longer shared: " + small.Pending);
                SongShare.TestBegin(small);
                foreach (var p in SongShare.TestPieces(small.ServerHash, SongShare.TestRequest))
                {
                    p.SetPos(0);
                    SongShare.ReceiveChunk(p);
                }
                c.Check(small.Score != null && small.Pending == null, "picked again: downloaded");
            }
            c.Check(SongShare.CanSend(0) && SongShare.CanSend(SongShare.QueueLimit) && !SongShare.CanSend(SongShare.QueueLimit + 1)
                    && SongShare.QueueLimit + SongShare.ChunkBytes + 64 <= 8192,
                "pieces wait for a short send queue (world data keep room)");
            c.Check(SongShare.CleanName("A <b>bold</b> name") == "A bbold/b name" && SongShare.CleanName("  ") == "Server song",
                "song names: no rich text, never empty");

            // Host (this game is the server): the folder's songs in the window, playable.
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            var hostId = SongShare.IdOf(MusicMath.Fnv1a(tune));
            Performance.TestRequestOpen();
            yield return Frames(3);
            c.Check(SongWindow.IsOpen && SongWindow.TestSelect(hostId), "server song in the window: " + hostId);
            yield return Frames(3);
            SelfTest.Screenshot(ShareName, "window");
            yield return Frames(2);
            SongWindow.TestPress("Play");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto, "server song plays");
            Performance.TestRequestStop();
            yield return Frames(3);

            // Server forbids own songs: hidden in the window, refused by Performance.
            SongLibrary.ConfiguredFolder = ownFolder;
            File.WriteAllBytes(Path.Combine(ownFolder, "own tune.mid"), tune);
            var strict = TestRules();
            strict.AllowPlayerSongs = false;
            ServerRules.TestRules = strict;
            var own = SongLibrary.ScanMidiFolder(out _);
            c.Check(own.Count == 1, "own song in the folder");
            if (own.Count == 1)
            {
                c.Check(!Performance.StartAuto(own[0], -1, out var refused) && refused == Performance.PlayerSongsOff,
                    "own song refused: " + refused);
            }
            Performance.TestRequestOpen();
            yield return Frames(3);
            c.Check(SongWindow.IsOpen && !SongWindow.TestSelect("midi:own tune.mid"), "own songs hidden in the window");
            c.Check(SongWindow.TestSelect(hostId), "server songs still listed");
            Performance.CloseWindow();
            yield return Frames(2);
            ServerRules.TestRules = TestRules();

            // Sharing off: no server songs.
            SongShare.TestShare = false;
            SongShare.ServerSettingsChanged();
            Performance.TestRequestOpen();
            yield return Frames(3);
            c.Check(SongWindow.IsOpen && !SongWindow.TestSelect(hostId), "sharing off: no server songs");
            Performance.CloseWindow();
            yield return Frames(2);
            c.Report();
        }
        finally
        {
            SongShare.TestShare = null;
            SongShare.TestFolder = null;
            SongShare.ServerSettingsChanged();
            SongShare.Reset();
            SongLibrary.ConfiguredFolder = savedOwn;
            if (Performance.WindowOpen)
            {
                Performance.CloseWindow();
            }
            rig.Restore();
            foreach (var f in new[] { folder, ownFolder })
            {
                try
                {
                    Directory.Delete(f, true);
                }
                catch (Exception)
                {
                    // Temp folder: fine to leave.
                }
            }
        }
    }

    // ---------- music.perform ----------

    // Press each chart note's lane just before its time (the press is read on the next frame).
    // skipEvery n: every n-th note not pressed; hitEvery n: only every n-th note pressed (0 = off). late: press this
    // many seconds after the note (0.08 = a Good, not a Perfect; else just before it).
    private static void PressDue(MiniGame game, HashSet<long> pressed, int skipEvery, int hitEvery = 0, float late = 0f)
    {
        foreach (var v in game.Visible)
        {
            var t = game.NoteTime(v.Key, v.Value);
            var key = (long)v.Key * 100000 + v.Value;
            if (t <= game.Clock + (late > 0f ? -late : 0.025f) && !pressed.Contains(key))
            {
                pressed.Add(key);
                if (skipEvery > 0 && pressed.Count % skipEvery == 0)
                {
                    continue;
                }
                if (hitEvery > 0 && pressed.Count % hitEvery != 0)
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
            // Half the notes is enough (user rule: 50 % for SuccessSeconds); one note in four never fills it.
            c.Check(Performance.StartMiniGame(song, -1, out error), "second run starts: " + error);
            yield return Frames(2);
            game = Performance.Game;
            pressed.Clear();
            start = Time.time;
            while (game != null && game.Encores == 0 && Time.time - start < 20f && Performance.Mode == PerformanceMode.MiniGame)
            {
                PressDue(game, pressed, 2);
                yield return null;
            }
            c.Check(game != null && game.Encores >= 1,
                $"half the notes: Encore (accuracy {F(game != null ? game.Accuracy : -1f)}, {F(Time.time - start)} s, "
                + $"hits {(game != null ? game.Judge.Hits : 0)}, perfect {(game != null ? game.Judge.Perfects : 0)}, "
                + $"misses {(game != null ? game.Judge.Misses : 0)})");
            Performance.TestRequestStop();
            yield return Frames(3);
            // Same with less exact hits (Good, not Perfect): a hit is a hit.
            c.Check(Performance.StartMiniGame(song, -1, out error), "Good run starts: " + error);
            yield return Frames(2);
            game = Performance.Game;
            pressed.Clear();
            start = Time.time;
            while (game != null && game.Encores == 0 && Time.time - start < 20f && Performance.Mode == PerformanceMode.MiniGame)
            {
                PressDue(game, pressed, 2, late: 0.08f);
                yield return null;
            }
            c.Check(game != null && game.Encores >= 1 && game.Judge.Hits > game.Judge.Perfects,
                $"half the notes, Good timing: Encore (accuracy {F(game != null ? game.Accuracy : -1f)}, {F(Time.time - start)} s, "
                + $"hits {(game != null ? game.Judge.Hits : 0)}, perfect {(game != null ? game.Judge.Perfects : 0)})");
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Check(Performance.StartMiniGame(song, -1, out error), "third run starts: " + error);
            yield return Frames(2);
            game = Performance.Game;
            pressed.Clear();
            start = Time.time;
            var filledMost = 0f;
            while (Time.time - start < 18f && game != null && Performance.Mode == PerformanceMode.MiniGame)
            {
                PressDue(game, pressed, 0, hitEvery: 4);
                filledMost = Mathf.Max(filledMost, game.Meter.Filled);
                yield return null;
            }
            c.Check(game != null && game.Encores == 0 && filledMost < 0.01f,
                $"one note in four: meter never fills (accuracy {F(game != null ? game.Accuracy : -1f)}, filled {F(filledMost)} s)");
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
