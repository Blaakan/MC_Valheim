using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Crafting.StationsBatchFeedMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod Stations.BatchFeed) and by tools/Test-Multiplayer.ps1 (client joined to a dedicated server).
// This file: names, registration, test bench (Rig) and helpers. Tests: SelfTestsSingle.cs, SelfTestsMultiplayer.cs.
// A press = Player.Interact(object, hold, alt), the single door of the Use key (alt = Alternative placement held).
// Single player (the game is the server, owner RPCs run in the same frame):
//   batchfeed.smelter          T01-T05 ore and coal slot counts, clamps, messages, one add sound; T18 hotbar; T19
//   batchfeed.family           T06 blast furnace, kiln, windmill, spinning wheel, eitr refinery, frigid kiln, hot tub
//   batchfeed.fires            T07 fires, T08 fire that can turn off (instance flag), T19 out of fuel
//   batchfeed.cooking          T09 cooking stations over fire, cooked piece, no fire
//   batchfeed.oven-foundry     T10 stone oven, frost foundry (cast slot stays vanilla)
//   batchfeed.shield-turret    T11 shield generator, ballista missile kinds
//   batchfeed.hint             T12 hint line on every covered spot, none on the others, ShowHint
//   batchfeed.settings         T14 mod-only key, T15 Amount, T20 controller labels, T26, T27 (all forced in memory)
//   batchfeed.rebind           T13 hint follows a rebind (bindings changed in memory, put back)
//   batchfeed.rebind-keys      T13 rebound keys really held (keyboard state through the Input System, window in front)
//   batchfeed.realkey          T14 T26 Right Shift / Left Shift really held the same way: the mod's own key read
//   batchfeed.hold             T16 hold after the batch = vanilla repeat pace
//   batchfeed.vanilla          T17 other things under Shift+E stay vanilla
//   batchfeed.skill            T25 first cooking level under the summary, later ones top-left
//   batchfeed.coverage         T24 coverage dump lines of a loaded world
//   batchfeed.nonowner-smelter M02 M04 M11: station of another game (made-up owner that never answers)
//   batchfeed.nonowner-others  M03 M06 M07 M08 M09: fire, cooking, ballista, shield, oven of another game
//   batchfeed.ownergone        M10 owner not connected = single vanilla press
//   batchfeed.toggle           T21 patches off = vanilla, back on = batch (framework toggle played by hand)
//   batchfeed.othermod         C03 other batch mod inside the press (stand-in patch)
//   batchfeed.compat-repair    C01, batchfeed.compat-tames C02, batchfeed.compat-lights C04
//   batchfeed.hint-follows     C05 press follow a coverage flag another mod flip; hint right after a look elsewhere
//   batchfeed.bug.hint-stale   C05 hint of the spot me keep looking at stay old (known bug: fail until fixed)
//   batchfeed.compat-claim     C06 other mod take the station over before vanilla's add (stand-in patch)
//   batchfeed.log              T23 no error line of this mod since it started
// Multiplayer (scenario modded; this mod is client-only, so the dedicated server never has it. One client and a
// dedicated server that simulate nothing = no second game can own a station: SelfTestsMultiplayer.cs say why. So
// the friend's side of M02-M09 and M11 is not played here, only this game's side):
//   batchfeed.mp.owner M01 (and the station data of M05), .nonowner-smelter this game's side of M02 M04 M11 with
//   the server's uid as owner, .ownergone M10, .toggle T21 (real Enabled), .settings T12
//   T14 T15 T27 (real settings changed live: multiplayer runs have throwaway config files), .log T23
// Me never set a ConfigEntry in single player: settings and input forced with Plugin.Test* (cleared in finally).
internal static partial class SelfTests
{
    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SessionLog.Start();
        foreach (var test in SingleTests())
        {
            SelfTest.Register(test.Key, test.Value);
        }
        foreach (var test in MultiplayerTests())
        {
            SelfTest.RegisterMultiplayer(test.Key, SelfTest.Modded, test.Value);
        }
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        foreach (var test in SingleTests())
        {
            SelfTest.Unregister(test.Key);
        }
        foreach (var test in MultiplayerTests())
        {
            SelfTest.UnregisterMultiplayer(test.Key);
        }
        ClearOverrides();
#endif
    }

#if DEBUG
    private const string Anchor = "[<color=yellow><b>";
    private const string AnchorEnd = "</b></color>] ";

    // Owner uid no game has: station with it = station of a game that never answer.
    private const long MadeUpOwner = 987654321012345L;

    // Other MC mods the cross-mod tests talk about.
    private const string RepairGuid = "MC.Crafting.Repair.OneClickAll";
    private const string StatsGuid = "MC.Exploration.Stats.PerCreature";
    private const string LightsGuid = "MC.Building.Lights.Switchable";

    private static KeyValuePair<string, Func<IEnumerator>> Test(string name, Func<IEnumerator> run) =>
        new KeyValuePair<string, Func<IEnumerator>>(name, run);

    private static void ClearOverrides()
    {
        Plugin.TestAmount = null;
        Plugin.TestModifierKey = null;
        Plugin.TestShowHint = null;
        Plugin.TestGamepad = null;
        Plugin.TestKeyHeld = null;
        BatchFeeder.TestConnectedOwner = null;
        BatchFeeder.ResetKeyCheck();
        Hooks.Off();
        HoverHint.Invalidate();
    }

    // Default settings whatever the player's cfg say, keyboard labels whatever device is in use.
    private static void ForceDefaults()
    {
        Plugin.TestAmount = 5;
        Plugin.TestModifierKey = KeyCode.None;
        Plugin.TestShowHint = true;
        Plugin.TestGamepad = false;
        Plugin.TestKeyHeld = false;
        BatchFeeder.TestConnectedOwner = null;
        HoverHint.Invalidate();
    }

    // ---------- small helpers ----------

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

    private static string Loc(string text) => Localization.instance.Localize(text);

    private static string Words(string text, string word) => Localization.instance.Localize(text, word);

    private static string UseLabel() => Localization.instance.GetBoundKeyString("Use");

    private static string AltLabel() => Localization.instance.GetBoundKeyString("AltPlace");

    private static bool Near(float a, float b, float tolerance = 0.001f) => Mathf.Abs(a - b) <= tolerance;

    private static string Token(string itemPrefab)
    {
        var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemPrefab) : null;
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        return drop != null ? drop.m_itemData.m_shared.m_name : null;
    }

    private static bool ModActive(string guid)
    {
        var view = FeatureRegistry.Find(guid);
        return view != null && view.Value.IsActive;
    }

    private static bool HudReady(Checks c) =>
        c.Check(MessageHud.instance != null && !Hud.IsUserHidden(),
            "the HUD is hidden (Ctrl+F3): the game shows no message then, nothing can be checked");

    private static float Fuel(Fireplace fire) => fire.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);

    // Only while this game own the fire (a write by another game would overwrite the owner's values).
    private static void SetFuel(Fireplace fire, float fuel) => fire.m_nview.GetZDO().Set(ZDOVars.s_fuel, fuel);

    private static int State(Fireplace fire) => fire.m_nview.GetZDO().GetInt(ZDOVars.s_state, 1);

    private static string Slot(CookingStation station, int slot) => station.m_nview.GetZDO().GetString("slot" + slot);

    private static int UsedSlots(CookingStation station)
    {
        var used = 0;
        for (var i = 0; i < station.m_slots.Length; i++)
        {
            if (Slot(station, i) != "")
            {
                used++;
            }
        }
        return used;
    }

    // Same words as vanilla SetSlot. Owner only.
    private static void SetSlot(CookingStation station, int slot, string item, float cookedTime, int status)
    {
        var zdo = station.m_nview.GetZDO();
        zdo.Set("slot" + slot, item);
        zdo.Set("slot" + slot, cookedTime);
        zdo.Set("slotstatus" + slot, status);
    }

    private static int ConversionIndex(Smelter smelter, string itemPrefab)
    {
        for (var i = 0; i < smelter.m_conversion.Count; i++)
        {
            var conversion = smelter.m_conversion[i];
            if (conversion != null && conversion.m_from != null && conversion.m_from.name == itemPrefab)
            {
                return i;
            }
        }
        return -1;
    }

    private static CookingStation.ItemConversion CookConversion(CookingStation station, string itemPrefab)
    {
        foreach (var conversion in station.m_conversion)
        {
            if (conversion != null && conversion.m_from != null && conversion.m_to != null && conversion.m_from.name == itemPrefab)
            {
                return conversion;
            }
        }
        return null;
    }

    // Vanilla fuel rule of smelter, oven and shield ("full" = value above max - 1), step by step.
    private static int FuelRoom(float value, int max, int limit)
    {
        var room = 0;
        while (room < limit && !(value > max - 1))
        {
            value += 1f;
            room++;
        }
        return room;
    }

    // Wait for a condition, at most <seconds> of real time. Caller check the condition again after.
    private static IEnumerator Until(Func<bool> done, float seconds)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (!done() && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
    }

    // Shield generator wake up in Start, one frame after it is spawned (net view, fuel switch callbacks, dome
    // effect): before that vanilla GetFuel and OnDestroy throw. Me wait for it before any use or destroy.
    private static IEnumerator ShieldReady(ShieldGenerator shield) => Until(() => shield == null || shield.m_nview != null, 2f);

    // Names of what a smelter-like station take as input, for messages.
    private static string Inputs(Smelter smelter)
    {
        var names = new List<string>();
        foreach (var conversion in smelter.m_conversion)
        {
            if (conversion != null && conversion.m_from != null)
            {
                names.Add(conversion.m_from.name);
            }
        }
        return names.Count == 0 ? "nothing" : string.Join(", ", names.ToArray());
    }

    // ---------- press ----------

    private struct Pressed
    {
        internal int Removed;   // items that left the inventory
        internal int Messages;  // center messages this press showed
        internal string Center; // center text after the press
    }

    // One Use press on a hovered object (hold = repeat call while the key stay down), measured in the same frame.
    // Each center message that reach the screen queue two fades: count them = count messages.
    private static Pressed Press(GameObject go, bool alt, bool hold = false)
    {
        var player = Player.m_localPlayer;
        var inventory = player.GetInventory();
        var hud = MessageHud.instance;
        var before = inventory.CountItems(null, -1, matchWorldLevel: false);
        var fades = hud._crossFadeTextBuffer.Count;
        player.Interact(go, hold, alt);
        return new Pressed
        {
            Removed = before - inventory.CountItems(null, -1, matchWorldLevel: false),
            Messages = (hud._crossFadeTextBuffer.Count - fades) / 2,
            Center = hud.m_messageCenterText.text,
        };
    }

    private static string Show(Pressed p) => $"removed {p.Removed}, {p.Messages} message(s), text '{p.Center}'";

    // Use key held for <seconds>: what Player.Update do every frame while the key stay down. Me note the time of
    // every item that leave the inventory. before = run right before each call (same frame).
    private static IEnumerator Hold(GameObject go, bool alt, float seconds, List<float> addTimes, Action before = null,
        Action<Pressed> after = null)
    {
        var end = Time.time + seconds;
        while (Time.time < end)
        {
            yield return null;
            if (go == null)
            {
                yield break;
            }
            before?.Invoke();
            var p = Press(go, alt, hold: true);
            for (var i = 0; i < p.Removed; i++)
            {
                addTimes.Add(Time.time);
            }
            after?.Invoke(p);
        }
    }

    // ---------- hint line ----------

    // Hint line of this mod in a hover text: a key line with "<key> + <key>" that end with " x<number>". Vanilla
    // rename line of a tame ("[L-Shift + E] Rename") has no such end.
    private static string HintLine(string hover)
    {
        if (string.IsNullOrEmpty(hover))
        {
            return null;
        }
        foreach (var line in hover.Split('\n'))
        {
            if (!line.StartsWith(Anchor, StringComparison.Ordinal) || line.IndexOf(" + ", StringComparison.Ordinal) < 0)
            {
                continue;
            }
            var x = line.LastIndexOf(" x", StringComparison.Ordinal);
            if (x < 0 || x + 2 >= line.Length)
            {
                continue;
            }
            var digits = true;
            for (var i = x + 2; i < line.Length; i++)
            {
                digits &= char.IsDigit(line[i]);
            }
            if (digits)
            {
                return line;
            }
        }
        return null;
    }

    // Key part of a key line: text between the yellow tags.
    private static string KeyPart(string line)
    {
        if (line == null || !line.StartsWith(Anchor, StringComparison.Ordinal))
        {
            return null;
        }
        var end = line.IndexOf(AnchorEnd, StringComparison.Ordinal);
        return end < 0 ? null : line.Substring(Anchor.Length, end - Anchor.Length);
    }

    // Expected hint, built from the hover text itself: vanilla Use line (first key line) with its key swapped for
    // "<modifier> + <use>" and " x<amount>" added, and it must be the very next line.
    private static bool HintOk(string hover, string modifier, int amount, out string detail)
    {
        var lines = (hover ?? "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(Anchor, StringComparison.Ordinal))
            {
                continue;
            }
            var end = lines[i].IndexOf(AnchorEnd, StringComparison.Ordinal);
            if (end < 0)
            {
                detail = $"Use line '{lines[i]}' has no key end";
                return false;
            }
            var action = lines[i].Substring(end + AnchorEnd.Length);
            var expected = Anchor + modifier + " + " + UseLabel() + AnchorEnd + action + " x" + amount;
            var actual = i + 1 < lines.Length ? lines[i + 1] : "(nothing below the Use line)";
            detail = $"expected '{expected}', got '{actual}'";
            return actual == expected;
        }
        detail = $"no Use line in '{hover}'";
        return false;
    }

    private static void ExpectHint(Checks c, string what, string hover, int amount = 5, string modifier = null)
    {
        var ok = HintOk(hover, modifier ?? AltLabel(), amount, out var detail);
        c.Check(ok, $"{what}: hint line right below the Use line ({detail})");
    }

    private static void ExpectNoHint(Checks c, string what, string hover) =>
        c.Check(HintLine(hover) == null, $"{what}: no batch hint expected, hover is '{hover}'");

    // ---------- log ----------

    // Me listen to the BepInEx log while a test run. Listeners get every level, so the Debug lines of each batch
    // press ("Batch feed: ... stop=...") are here even when the log file skip Debug.
    private sealed class LogTap : ILogListener
    {
        private readonly object _gate = new object();
        private readonly List<KeyValuePair<LogLevel, string>> _lines = new List<KeyValuePair<LogLevel, string>>();

        internal LogTap() => BepInEx.Logging.Logger.Listeners.Add(this);

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null || eventArgs.Source == null || eventArgs.Source.SourceName != ModInfo.Name)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                if (text == null)
                {
                    return;
                }
                lock (_gate)
                {
                    _lines.Add(new KeyValuePair<LogLevel, string>(eventArgs.Level, text));
                }
            }
            catch
            {
                // Me never throw inside logger.
            }
        }

        public void Dispose() => BepInEx.Logging.Logger.Listeners.Remove(this);

        internal int Mark()
        {
            lock (_gate)
            {
                return _lines.Count;
            }
        }

        internal List<string> Since(int mark, string start, LogLevel levels = LogLevel.All)
        {
            var result = new List<string>();
            lock (_gate)
            {
                for (var i = Math.Max(0, mark); i < _lines.Count; i++)
                {
                    if ((_lines[i].Key & levels) != 0 && _lines[i].Value.StartsWith(start, StringComparison.Ordinal))
                    {
                        result.Add(_lines[i].Value);
                    }
                }
            }
            return result;
        }

        // Last line a batch press wrote since the mark ("Batch feed: <piece> <kind> owner=..."), or "".
        internal string Batch(int mark)
        {
            var lines = Since(mark, "Batch feed: ");
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (lines[i].IndexOf(" owner=", StringComparison.Ordinal) >= 0)
                {
                    return lines[i];
                }
            }
            return "";
        }
    }

    private static void ExpectLog(Checks c, string what, string line, params string[] parts)
    {
        var ok = !string.IsNullOrEmpty(line);
        foreach (var part in parts)
        {
            ok &= line != null && line.IndexOf(part, StringComparison.Ordinal) >= 0;
        }
        c.Check(ok, $"{what}: batch log line with '{string.Join("', '", parts)}', got '{line}'");
    }

    // Me count error lines of this mod for the whole session (from first activation): own log source at Error or
    // Fatal, and error lines of anybody that name this mod's code. Self test lines are not errors of the feature.
    private sealed class SessionLog : ILogListener
    {
        private const string CodeMark = "StationsBatchFeedMod";

        private static SessionLog _instance;

        private readonly object _gate = new object();
        private int _errors;
        private string _first;
        private int _dumps;

        // Coverage dumps (their count line) seen since the mod started: a world load write one by itself. -1 = off.
        internal static int Dumps
        {
            get
            {
                if (_instance == null)
                {
                    return -1;
                }
                lock (_instance._gate)
                {
                    return _instance._dumps;
                }
            }
        }

        internal static void Start()
        {
            if (_instance == null)
            {
                _instance = new SessionLog();
                BepInEx.Logging.Logger.Listeners.Add(_instance);
            }
        }

        internal static bool Read(out int errors, out string first)
        {
            errors = 0;
            first = null;
            if (_instance == null)
            {
                return false;
            }
            lock (_instance._gate)
            {
                errors = _instance._errors;
                first = _instance._first;
            }
            return true;
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null)
                {
                    return;
                }
                var mine = eventArgs.Source != null && eventArgs.Source.SourceName == ModInfo.Name;
                if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) == 0)
                {
                    if (mine && eventArgs.Level == LogLevel.Debug && eventArgs.Data is string line
                        && line.StartsWith(DumpPrefix, StringComparison.Ordinal)
                        && line.IndexOf(" station part(s) listed from ", StringComparison.Ordinal) >= 0)
                    {
                        lock (_gate)
                        {
                            _dumps++;
                        }
                    }
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString() ?? "";
                if (text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    return;
                }
                if (!mine && text.IndexOf(CodeMark, StringComparison.Ordinal) < 0
                    && text.IndexOf(ModInfo.Guid, StringComparison.Ordinal) < 0)
                {
                    return;
                }
                lock (_gate)
                {
                    _errors++;
                    if (_first == null)
                    {
                        var cut = text.IndexOf('\n');
                        _first = cut > 0 ? text.Substring(0, cut).TrimEnd() : text;
                    }
                }
            }
            catch
            {
                // Me never throw inside logger.
            }
        }

        public void Dispose()
        {
        }
    }

    // ---------- test-only patches ----------

    // Own Harmony id, on only while a test ask, off in ClearOverrides. Never [HarmonyPatch] classes: the framework
    // apply every such class with the feature.
    private static class Hooks
    {
        private static Harmony _harmony;
        private static EffectList _effects;
        private static ZNetView _view;

        internal static int EffectCount;
        internal static readonly List<string> Rpcs = new List<string>();
        internal static bool FillAll;

        private static readonly HashSet<ZDO> Kept = new HashSet<ZDO>();
        private static bool _keeping;
        private static bool _claiming;

        private static Harmony Patcher => _harmony ?? (_harmony = new Harmony(ModInfo.Guid + ".selftest"));

        // Count how many times this effect list play (one add sound = one Create).
        internal static void WatchEffects(EffectList effects)
        {
            EffectCount = 0;
            if (_effects == null)
            {
                Patcher.Patch(AccessTools.Method(typeof(EffectList), nameof(EffectList.Create)),
                    prefix: new HarmonyMethod(typeof(Hooks), nameof(EffectPrefix)));
            }
            _effects = effects;
        }

        // Note every rpc this net view send to its owner.
        internal static void WatchRpcs(ZNetView view)
        {
            Rpcs.Clear();
            if (_view == null)
            {
                Patcher.Patch(AccessTools.Method(typeof(ZNetView), nameof(ZNetView.InvokeRPC), new[] { typeof(string), typeof(object[]) }),
                    prefix: new HarmonyMethod(typeof(Hooks), nameof(RpcPrefix)));
            }
            _view = view;
        }

        // Stand-in for a "fill all" mod: inside vanilla's add it take two more ore and send them too.
        internal static void PlayFillAllMod()
        {
            if (!FillAll)
            {
                Patcher.Patch(AccessTools.Method(typeof(Smelter), nameof(Smelter.OnAddOre)),
                    postfix: new HarmonyMethod(typeof(Hooks), nameof(FillAllPostfix)));
            }
            FillAll = true;
        }

        // Station of "another game" that never let go: made-up owner on its ZDO, and every later owner change of
        // that ZDO is refused while the test ask. Without this the station come back to this game: the world hand
        // it over every 2 s, and some mods claim a station right before vanilla's add (ValheimCommunityPatch
        // "Fix Fuel And Ore Loss" call ZNetView.ClaimOwnership in a prefix of Smelter.OnAddOre / OnAddFuel and
        // Fireplace.Interact / UseItem), so the adds would run here and the non-owner road would never be walked.
        internal static void KeepOwner(ZNetView view)
        {
            var zdo = view.GetZDO();
            if (Kept.Contains(zdo))
            {
                return;
            }
            if (!_keeping)
            {
                Patcher.Patch(AccessTools.Method(typeof(ZDO), nameof(ZDO.SetOwner)),
                    prefix: new HarmonyMethod(typeof(Hooks), nameof(OwnerPrefix)) { priority = Priority.First });
                _keeping = true;
            }
            zdo.SetOwner(MadeUpOwner);
            Kept.Add(zdo);
        }

        // Before me destroy a kept station: destroy need the owner.
        internal static void Release(ZNetView view)
        {
            if (Kept.Count > 0 && view != null && view.GetZDO() != null)
            {
                Kept.Remove(view.GetZDO());
            }
        }

        // Stand-in for a mod that take the station over right before vanilla's add: same call as
        // ValheimCommunityPatch "Fix Fuel And Ore Loss" (prefix of Smelter.OnAddOre: ClaimOwnership when not owner).
        internal static void PlayClaimMod()
        {
            if (!_claiming)
            {
                Patcher.Patch(AccessTools.Method(typeof(Smelter), nameof(Smelter.OnAddOre)),
                    prefix: new HarmonyMethod(typeof(Hooks), nameof(ClaimPrefix)));
                _claiming = true;
            }
        }

        internal static void Off()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
            _effects = null;
            _view = null;
            FillAll = false;
            Rpcs.Clear();
            Kept.Clear();
            _keeping = false;
            _claiming = false;
        }

        private static bool OwnerPrefix(ZDO __instance) => !Kept.Contains(__instance);

        private static void ClaimPrefix(Smelter __instance)
        {
            var view = __instance.m_nview;
            if (view != null && view.IsValid() && !view.IsOwner())
            {
                view.ClaimOwnership();
            }
        }

        private static void EffectPrefix(EffectList __instance)
        {
            if (ReferenceEquals(__instance, _effects))
            {
                EffectCount++;
            }
        }

        private static void RpcPrefix(ZNetView __instance, string method)
        {
            if (ReferenceEquals(__instance, _view))
            {
                Rpcs.Add(method);
            }
        }

        private static void FillAllPostfix(Smelter __instance, Humanoid user, bool __result)
        {
            if (!FillAll || !__result || user == null)
            {
                return;
            }
            var inventory = user.GetInventory();
            for (var i = 0; i < 2; i++)
            {
                var item = __instance.FindCookableItem(inventory);
                if (item == null)
                {
                    return;
                }
                var name = item.m_dropPrefab.name;
                inventory.RemoveItem(item, 1);
                __instance.m_nview.InvokeRPC("RPC_AddOre", name, false);
            }
        }
    }

    // ---------- test bench ----------

    // Me = test bench. Player's own items put aside (empty inventory = exact counts), default settings forced in
    // memory, auto pickup off, cooking skill parked, every piece me spawn remembered. End() put all back and destroy
    // pieces and the item drops they made: call it in finally.
    private sealed class Rig
    {
        private const float DropRadius = 25f;

        internal readonly Player P;
        internal readonly Inventory Inv;
        internal readonly Vector3 Origin;

        private readonly Vector3 _forward;
        private readonly Vector3 _right;
        private readonly Quaternion _rotation;
        private readonly List<ItemDrop.ItemData> _stash;
        private readonly bool _autoPickup;
        private readonly HashSet<ItemDrop> _dropsBefore = new HashSet<ItemDrop>();
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private bool _skillParked;
        private bool _skillExisted;
        private Skills.SkillType _skillType;
        private float _skillLevel;
        private float _skillAccumulator;

        // anchor = where the pieces go (default: around the player).
        internal Rig(Vector3? anchor = null)
        {
            P = Player.m_localPlayer;
            if (P == null || ZNetScene.instance == null || ObjectDB.instance == null || ZoneSystem.instance == null)
            {
                throw new InvalidOperationException("no local player or world: the test bench cannot start");
            }
            Inv = P.GetInventory();
            Origin = anchor ?? P.transform.position;
            _forward = P.transform.forward;
            _forward.y = 0f;
            _forward = _forward.sqrMagnitude > 0.001f ? _forward.normalized : Vector3.forward;
            _right = new Vector3(_forward.z, 0f, -_forward.x);
            _rotation = P.transform.rotation;

            _stash = new List<ItemDrop.ItemData>(Inv.m_inventory);
            Inv.m_inventory.Clear();
            Inv.Changed();
            _autoPickup = Player.m_enableAutoPickup;
            Player.m_enableAutoPickup = false;
            foreach (var drop in ItemDrop.s_instances)
            {
                _dropsBefore.Add(drop);
            }
            PendingAdds.Clear();
            ForceDefaults();
        }

        // Spot on the ground: metres to the right and ahead of the anchor (player's facing when the bench started).
        internal Vector3 At(float right, float forward)
        {
            var pos = Origin + _forward * forward + _right * right;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            return pos;
        }

        internal GameObject Spawn(string prefabName, float right, float forward) => SpawnAt(prefabName, At(right, forward));

        internal GameObject SpawnAt(string prefabName, Vector3 position)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                return null;
            }
            var go = Object.Instantiate(prefab, position, Quaternion.identity);
            _spawned.Add(go);
            return go;
        }

        internal GameObject SpawnPrefab(GameObject prefab, float right, float forward, float up = 0f)
        {
            if (prefab == null)
            {
                return null;
            }
            var go = Object.Instantiate(prefab, At(right, forward) + Vector3.up * up, Quaternion.identity);
            _spawned.Add(go);
            return go;
        }

        // Thing that destroy itself (piece removed like a player remove it): me stop tracking it.
        internal void Forget(GameObject go) => _spawned.Remove(go);

        internal void Destroy(GameObject go)
        {
            _spawned.Remove(go);
            Kill(go);
        }

        // Destroy need the owner (else only the local object go and the piece come back): take it first.
        private static void Kill(GameObject go)
        {
            if (go == null || ZNetScene.instance == null)
            {
                return;
            }
            var view = go.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                Hooks.Release(view);
                view.ClaimOwnership();
            }
            ZNetScene.instance.Destroy(go);
        }

        // False = no such item or no room. Never drop on the ground.
        internal bool Give(string itemPrefab, int count) => GiveAt(itemPrefab, count, -1, -1);

        // Same, into a chosen inventory slot (x from the left, y from the top).
        internal bool GiveAt(string itemPrefab, int count, int x, int y)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(itemPrefab);
            if (prefab == null || !Inv.CanAddItem(prefab, count))
            {
                return false;
            }
            return Inv.AddItem(itemPrefab, count, 1, 0, 0L, "", new Vector2i(x, y), false, false, false) != null;
        }

        internal void Empty()
        {
            Inv.m_inventory.Clear();
            Inv.Changed();
        }

        internal int Count(string token) => token == null ? 0 : Inv.CountItems(token, -1, matchWorldLevel: false);

        // Item drops that appeared near the bench since it started (cooked food taken out, bars, missiles).
        internal List<ItemDrop> NewDrops(string itemPrefab = null)
        {
            var result = new List<ItemDrop>();
            foreach (var drop in ItemDrop.s_instances)
            {
                if (drop == null || _dropsBefore.Contains(drop) || drop.m_nview == null || !drop.m_nview.IsValid())
                {
                    continue;
                }
                if (Utils.DistanceXZ(drop.transform.position, Origin) > DropRadius)
                {
                    continue;
                }
                if (itemPrefab == null || Utils.GetPrefabName(drop.gameObject) == itemPrefab)
                {
                    result.Add(drop);
                }
            }
            return result;
        }

        internal int NewDropCount(string itemPrefab)
        {
            var count = 0;
            foreach (var drop in NewDrops(itemPrefab))
            {
                count += drop.m_itemData.m_stack;
            }
            return count;
        }

        // Skill at a level where a few adds never level it up (level-up message would join the summary), or at 0.
        internal Skills.Skill ParkSkill(Skills.SkillType type, float level, float accumulator = 0f)
        {
            var skills = P.m_skills;
            if (!_skillParked)
            {
                _skillParked = true;
                _skillType = type;
                _skillExisted = skills.m_skillData.TryGetValue(type, out var old);
                if (_skillExisted)
                {
                    _skillLevel = old.m_level;
                    _skillAccumulator = old.m_accumulator;
                }
            }
            var skill = skills.GetSkill(type);
            skill.m_level = level;
            skill.m_accumulator = accumulator;
            return skill;
        }

        internal void End()
        {
            Step(ClearOverrides);
            Step(() =>
            {
                if (TextInput.instance != null && TextInput.instance.m_panel != null && TextInput.instance.m_panel.activeSelf)
                {
                    TextInput.instance.Hide();
                }
            });
            foreach (var go in new List<GameObject>(_spawned))
            {
                Step(() => Kill(go));
            }
            _spawned.Clear();
            Step(() =>
            {
                foreach (var drop in NewDrops())
                {
                    Kill(drop.gameObject);
                }
            });
            Step(() =>
            {
                Inv.m_inventory.Clear();
                Inv.m_inventory.AddRange(_stash);
                Inv.Changed();
            });
            Step(() => Player.m_enableAutoPickup = _autoPickup);
            Step(() =>
            {
                if (!_skillParked)
                {
                    return;
                }
                var skills = P.m_skills;
                if (_skillExisted)
                {
                    var skill = skills.GetSkill(_skillType);
                    skill.m_level = _skillLevel;
                    skill.m_accumulator = _skillAccumulator;
                }
                else
                {
                    skills.m_skillData.Remove(_skillType);
                }
            });
            Step(() =>
            {
                if (P != null)
                {
                    P.transform.rotation = _rotation;
                }
            });
        }

        // One clean-up step must never stop the next ones.
        private static void Step(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up step failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    // ---------- fire under a cooking station ----------

    // Burning box of this fire as cooking stations see it (vanilla keep one box per burning effect area).
    private static bool BurningBox(Fireplace fire, out Bounds box)
    {
        foreach (var pair in EffectArea.s_BurningAreas)
        {
            if (pair.Value != null && pair.Value.transform.IsChildOf(fire.transform))
            {
                box = pair.Key;
                return true;
            }
        }
        box = default;
        return false;
    }

    private static Fireplace LitFire(Rig rig, Vector3 position)
    {
        var go = rig.SpawnAt("fire_pit", position);
        var fire = go != null ? go.GetComponent<Fireplace>() : null;
        if (fire == null)
        {
            return null;
        }
        if (Fuel(fire) < 3f)
        {
            SetFuel(fire, Mathf.Min(3f, fire.m_maxFuel));
        }
        // Owner tick now: flames on, so the burning area exist this frame (also for stations that ask physics).
        fire.UpdateFireplace();
        Physics.SyncTransforms();
        return fire;
    }

    // Cooking station over a lit fire, like players build it: campfire on the ground at the station's place. Any fire
    // check point still outside a burning box get one more campfire put so its box sit right on that point.
    private static bool PutFireUnder(Rig rig, CookingStation station, out string detail)
    {
        var first = LitFire(rig, station.transform.position);
        if (first == null)
        {
            detail = "could not spawn fire_pit";
            return false;
        }
        if (station.IsFireLit())
        {
            detail = "campfire at the station's place";
            return true;
        }
        if (!BurningBox(first, out var box))
        {
            detail = "the campfire has no burning area";
            return false;
        }
        var offset = box.center - first.transform.position;
        var points = new List<Vector3>();
        if (station.m_fireCheckPoints != null && station.m_fireCheckPoints.Length > 0)
        {
            foreach (var point in station.m_fireCheckPoints)
            {
                points.Add(point.position);
            }
        }
        else
        {
            points.Add(station.transform.position);
        }
        var extra = 0;
        foreach (var point in points)
        {
            if (!EffectArea.IsPointPlus025InsideBurningArea(point))
            {
                LitFire(rig, point - offset);
                extra++;
            }
        }
        detail = $"{extra} extra campfire(s) placed on the fire check points ({points.Count} point(s))";
        return station.IsFireLit();
    }
#endif
}
