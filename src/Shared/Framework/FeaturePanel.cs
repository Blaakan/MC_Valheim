using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MC.Shared;

// Me = "MC Mods" window. Only leader mod add me (see ModPlugin.Start), so one panel for all MC mods.
// Show button in main menu and pause menu (cursor free there). Click = list of all MC features by category:
// toggle, status, who needs it, multiplayer support, dependencies.
// Me also say on spawn if some feature inactive for reason user did not choose (missing dep, server lack mod...).
internal sealed class FeaturePanel : MonoBehaviour
{
    private static readonly string[] CategoryOrder = { "Core", "Combat", "Exploration", "Farming", "Cooking", "Building", "Crafting", "UX" };
    private const int WindowId = 0x4D43_0001;
    private const float ButtonWidth = 238f, ButtonHeight = 30f, Margin = 12f;

    private static string _lastNotice;

    private Harmony _harmony;
    private bool _open;
    private bool _canShow;
    private float _scale = 1f;
    private Rect _window;
    private Vector2 _scroll;
    private bool _stylesReady;
    private GUIStyle _header, _small, _wrap, _box, _toggle, _warnButton;
    private GUIStyle _statusOk, _statusOff, _statusWarn, _statusError;

    // IMGUI clicks also reach game uGUI buttons under them (Logout!). Me put invisible top-most raycast target
    // over our button / window, so uGUI hit it instead. (Disabling EventSystem made vanilla code NRE each frame.)
    private GameObject _blockerRoot;
    private GameObject _fullBlocker;
    private RectTransform _buttonBlocker;
    private UnityEngine.EventSystems.EventSystem _navEventSystem;
    private bool _navWasOn;

    private void Awake()
    {
        try
        {
            _harmony = new Harmony("MC.Shared.FeaturePanel");
            _harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.OnSpawned)),
                postfix: new HarmonyMethod(typeof(FeaturePanel), nameof(OnPlayerSpawned)));
        }
        catch (Exception e)
        {
            Log.Warning($"Feature notices unavailable: {e.Message}");
        }
    }

    private void OnDestroy()
    {
        Close();
        if (_blockerRoot != null)
        {
            Destroy(_blockerRoot);
        }
        _harmony?.UnpatchSelf();
    }

    // Real pause menu (its root shown) or main menu, and no game popup on top.
    // Not Menu.IsVisible(): it is also true during in-game popups and a few frames after menu hide.
    private static bool ComputeCanShow() =>
        !UnifiedPopup.IsVisible()
        && (FejdStartup.instance != null
            || (Menu.instance != null && Menu.instance.m_root != null && Menu.instance.m_root.gameObject.activeSelf));

    private void Update()
    {
        try
        {
            // Configuration manager installed: its window (F1) already list every MC mod with Enabled and Status, so
            // me hide button and window (user choice 2026-10-02). Spawn notice stay, it point to F1 then.
            _canShow = ConfigManagerName() == null && ComputeCanShow();
            _scale = Mathf.Clamp(Screen.height / 1080f, 1f, 3f);
            if (!_canShow && _open)
            {
                Close();
            }
            UpdateBlockers();
        }
        catch (Exception e)
        {
            PatchGuard.Report("FeaturePanel.Update", e);
        }
    }

    private void Open(float width, float height)
    {
        _open = true;
        _window = new Rect((width - 820f) / 2f, 60f, 820f, Mathf.Min(680f, height - 120f));
        // Arrow keys / gamepad no move game UI selection behind panel.
        _navEventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (_navEventSystem != null)
        {
            _navWasOn = _navEventSystem.sendNavigationEvents;
            _navEventSystem.sendNavigationEvents = false;
        }
        UpdateBlockers();
    }

    private void Close()
    {
        _open = false;
        if (_navEventSystem != null)
        {
            _navEventSystem.sendNavigationEvents = _navWasOn;
            _navEventSystem = null;
        }
        UpdateBlockers();
    }

    private void UpdateBlockers()
    {
        if (_blockerRoot == null)
        {
            if (!_canShow)
            {
                return; // make them lazily, first time a menu show
            }
            _blockerRoot = new GameObject("MC.FeaturePanel.InputBlocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _blockerRoot.transform.SetParent(transform, false); // plugin object already survive scene loads
            var canvas = _blockerRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            _fullBlocker = NewBlocker("Window", stretch: true).gameObject;
            _buttonBlocker = NewBlocker("Button", stretch: false);
        }

        _fullBlocker.SetActive(_canShow && _open);
        var showButton = _canShow && !_open;
        _buttonBlocker.gameObject.SetActive(showButton);
        if (showButton)
        {
            _buttonBlocker.anchoredPosition = new Vector2(-Margin * _scale, -Margin * _scale);
            _buttonBlocker.sizeDelta = new Vector2(ButtonWidth * _scale, ButtonHeight * _scale);
        }
    }

    private RectTransform NewBlocker(string name, bool stretch)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_blockerRoot.transform, false);
        var image = go.GetComponent<Image>();
        image.color = Color.clear; // alpha 0 still catch raycasts
        image.raycastTarget = true;
        var rt = (RectTransform)go.transform;
        if (stretch)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = Vector2.one; // top-right corner, like the IMGUI button
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.one;
        }
        go.SetActive(false);
        return rt;
    }

    private void OnGUI()
    {
        try
        {
            if (!_canShow)
            {
                return;
            }

            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));
            var width = Screen.width / _scale;
            var height = Screen.height / _scale;
            EnsureStyles();

            var features = FeatureRegistry.All();
            if (!_open)
            {
                var issues = features.Count(NeedsAttention);
                var label = issues > 0 ? $"MC Mods ({issues} need attention)" : $"MC Mods ({features.Count})";
                if (GUI.Button(new Rect(width - ButtonWidth - Margin, Margin, ButtonWidth, ButtonHeight), label,
                        issues > 0 ? _warnButton : GUI.skin.button))
                {
                    Open(width, height);
                }
                return;
            }

            _window = GUILayout.Window(WindowId, _window, id => DrawWindow(features), "MC Mods — features");
        }
        catch (Exception e)
        {
            PatchGuard.Report("FeaturePanel.OnGUI", e);
        }
    }

    private void DrawWindow(List<FeatureView> features)
    {
        GUILayout.Label("Turn features on or off. Changes apply immediately and are saved in each mod's config file "
                        + "(BepInEx/config/<mod>.cfg), so mod managers and ConfigurationManager see them too.", _wrap);
        _scroll = GUILayout.BeginScrollView(_scroll);

        var categories = CategoryOrder.Concat(features.Select(f => f.Category).Where(c => !CategoryOrder.Contains(c)).Distinct());
        foreach (var category in categories)
        {
            var items = features.Where(f => f.Category == category).OrderBy(f => f.Name).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            GUILayout.Label(category, _header);
            foreach (var f in items)
            {
                DrawFeature(f);
            }
        }

        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", GUILayout.Width(120f)))
        {
            Close();
        }
        GUILayout.EndHorizontal();
        GUI.DragWindow();
    }

    private void DrawFeature(FeatureView f)
    {
        GUILayout.BeginVertical(_box);
        GUILayout.BeginHorizontal();
        var enabled = f.Enabled;
        var value = enabled != null && enabled.Value;
        var newValue = GUILayout.Toggle(value, " " + f.Name, _toggle, GUILayout.Width(330f));
        if (enabled != null && newValue != value)
        {
            enabled.Value = newValue; // SettingChanged -> RefreshAll -> saved to cfg
        }
        GUILayout.Label($"v{f.Version} ({f.Build}) · {f.Scope} · {SideText(f.Side)} · {MultiplayerText(f.Multiplayer)}", _small);
        GUILayout.EndHorizontal();

        GUILayout.Label(f.Status, StatusStyle(f.State));

        if (f.Requires.Length > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Needs: " + string.Join(", ", f.Requires.Select(NameOf)), _small);
            // Dependency off by user? Me give one-click fix, so no hunting in list.
            foreach (var guid in f.Requires)
            {
                var dep = FeatureRegistry.Find(guid);
                if (dep != null && dep.Value.IsDisabledByUser && dep.Value.Enabled != null
                    && GUILayout.Button($"Turn on {dep.Value.Name}", GUILayout.Width(220f)))
                {
                    dep.Value.Enabled.Value = true;
                }
            }
            GUILayout.EndHorizontal();
        }

        if (!string.IsNullOrEmpty(f.MultiplayerNotes))
        {
            GUILayout.Label(f.MultiplayerNotes, _small);
        }
        GUILayout.EndVertical();
    }

    // Not active for a reason user did not choose and can act on. Server-only mod on someone else server = normal.
    private static bool NeedsAttention(FeatureView f) =>
        !f.IsActive && !f.IsDisabledByUser
        && f.State != nameof(ModState.WaitingForServer)
        && f.State != nameof(ModState.Starting)
        && f.State != nameof(ModState.ServerOnly);

    // Hard failures first in notices.
    private static int Severity(FeatureView f)
    {
        switch (f.State)
        {
            case nameof(ModState.Error): return 0;
            case nameof(ModState.MissingDependency):
            case nameof(ModState.Conflict): return 1;
            case nameof(ModState.ServerMissing):
            case nameof(ModState.ServerMismatch): return 2;
            default: return 3;
        }
    }

    private static string NameOf(string guid) => FeatureRegistry.Find(guid)?.Name ?? guid;

    private static string SideText(string side)
    {
        switch (side)
        {
            case ModDescriptor.Sides.Client: return "client-side (only you need it)";
            case ModDescriptor.Sides.Server: return "server-side (host/server needs it)";
            case ModDescriptor.Sides.Both: return "server + every player need it";
            default: return side;
        }
    }

    private static string MultiplayerText(string mode)
    {
        switch (mode)
        {
            case ModDescriptor.MultiplayerModes.Compatible: return "multiplayer OK";
            case ModDescriptor.MultiplayerModes.Limited: return "multiplayer: limited";
            case ModDescriptor.MultiplayerModes.SinglePlayer: return "single-player only";
            default: return mode;
        }
    }

    private GUIStyle StatusStyle(string state)
    {
        switch (state)
        {
            case nameof(ModState.Active): return _statusOk;
            case nameof(ModState.Disabled):
            case nameof(ModState.ServerOnly): return _statusOff;
            case nameof(ModState.Error): return _statusError;
            default: return _statusWarn;
        }
    }

    private void EnsureStyles()
    {
        if (_stylesReady)
        {
            return;
        }

        _header = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, margin = new RectOffset(4, 4, 12, 4) };
        _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        _small.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        _wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
        _box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 6, 6), margin = new RectOffset(4, 4, 3, 3) };
        _toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 15, fontStyle = FontStyle.Bold };
        _warnButton = new GUIStyle(GUI.skin.button);
        _warnButton.normal.textColor = new Color(1f, 0.75f, 0.3f);
        _statusOk = Colored(new Color(0.45f, 0.9f, 0.45f));
        _statusOff = Colored(new Color(0.65f, 0.65f, 0.65f));
        _statusWarn = Colored(new Color(1f, 0.72f, 0.25f));
        _statusError = Colored(new Color(1f, 0.4f, 0.35f));
        _stylesReady = true;
    }

    private static GUIStyle Colored(Color c)
    {
        var s = new GUIStyle(GUI.skin.label) { wordWrap = true };
        s.normal.textColor = c;
        return s;
    }

    // Known configuration managers (plugin GUID, read in their sources 2026-10-02): shudnal's, aedenthorn's (Nexus 740
    // and cjayride's fork share it; his universal one differ only in case), upstream BepInEx. Any other plugin whose
    // name end with "Configuration Manager" count too (forks). Plugin that failed to load (no instance) no count.
    private static readonly string[] ConfigManagerGuids =
    {
        "_shudnal.ConfigurationManager",
        "aedenthorn.ConfigurationManager",
        "com.bepis.bepinex.configurationmanager",
    };

    private static bool _cmChecked;
    private static string _cmName;

    // Name of the loaded configuration manager, or null. Me check once, after every plugin loaded (panel is added in
    // ModPlugin.Start, notices come later still). Pure part in IsConfigManager (probe and self tests can hammer it).
    internal static string ConfigManagerName()
    {
        if (_cmChecked)
        {
            return _cmName;
        }
        _cmChecked = true;
        foreach (var pair in BepInEx.Bootstrap.Chainloader.PluginInfos)
        {
            var info = pair.Value;
            if (info?.Metadata == null || info.Instance == null || !IsConfigManager(info.Metadata.GUID, info.Metadata.Name))
            {
                continue;
            }
            _cmName = string.IsNullOrEmpty(info.Metadata.Name) ? info.Metadata.GUID : info.Metadata.Name;
            Log.Info($"{_cmName} is installed: the MC Mods button is hidden; every MC mod's settings, including Enabled "
                     + "and Status, are in its window (F1).");
            break;
        }
        return _cmName;
    }

    internal static bool IsConfigManager(string guid, string name)
    {
        foreach (var g in ConfigManagerGuids)
        {
            if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        var compact = (name ?? "").Replace(" ", "");
        return compact.EndsWith("ConfigurationManager", StringComparison.OrdinalIgnoreCase);
    }

    // One combined line (TopLeft slot show one message at a time), only when the problem set changed
    // (no repeat on every death). Hidden HUD: skip and retry next spawn (user hid HUD on purpose).
    private static void OnPlayerSpawned(Player __instance)
    {
        try
        {
            if (!ReferenceEquals(__instance, Player.m_localPlayer) || MessageHud.instance == null)
            {
                return;
            }

            var problems = FeatureRegistry.All().Where(NeedsAttention).OrderBy(Severity).ThenBy(f => f.Name).ToList();
            if (problems.Count == 0)
            {
                _lastNotice = null;
                return;
            }

            var names = string.Join(", ", problems.Select(f => f.Name));
            if (names == _lastNotice || Hud.IsUserHidden())
            {
                return;
            }

            _lastNotice = names;
            var cm = ConfigManagerName();
            var where = cm == null ? "Esc > MC Mods" : $"F1 ({cm}), each mod's General > Status,";
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft,
                $"MC Mods: {problems.Count} feature(s) inactive ({names}). {where} for details.");
        }
        catch (Exception e)
        {
            PatchGuard.Report("FeaturePanel.OnPlayerSpawned", e);
        }
    }
}
