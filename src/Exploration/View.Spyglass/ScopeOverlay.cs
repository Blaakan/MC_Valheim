using MC.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace MC.Exploration.ViewSpyglassMod;

// Me = the round spyglass view drawn over the world: sharp circle in the middle, blurred ring around it, darker
// toward the screen edge. No shader of our own (no asset bundle): blur = the frame shrunk a few times
// (ScopeCapture, bilinear = soft) and drawn back full screen by a UI ring mesh whose vertex alpha go from 0 at the
// circle to 1 outside (UI/Default multiply texture by vertex colour). Dark edge = black ring mesh the same way. Own
// screen-space canvas just under the HUD canvas (health, hotbar stay readable on top), above the game's
// render-scale picture (FrameBufferScaler) when that one is on. Built on first use, hidden while the spyglass is down,
// destroyed when the feature turns off.
internal static class ScopeOverlay
{
    internal const float DefaultClearSize = 0.7f;
    internal const float DefaultEdgeDarkness = 0.85f;

    private static GameObject _root;
    private static Canvas _canvas;
    private static BlurRing _blur;
    private static DarkRing _dark;
    private static bool _shown;

    internal static bool Shown => _shown;

#if DEBUG
    internal static int SortingOrder => _canvas != null ? _canvas.sortingOrder : int.MinValue;
    internal static string Placement { get; private set; } = "";

    // Self tests: view settings forced in memory (null = config), and what overlay draw now: built at all, blur ring
    // drawn with a picture, numbers of each ring (x = circle size as share of screen height, y = iris, 1 when fully
    // closed in, z = strength 0..1), screen rectangle the rings fill.
    internal static float? TestClearViewSize;
    internal static bool? TestEdgeBlur;
    internal static float? TestEdgeDarkness;
    internal static bool TestBuilt => _root != null;
    internal static bool TestBlurOn => _blur != null && _blur.enabled && _blur.texture != null;
    internal static Vector3 TestDark => _dark != null ? _dark.TestState : -Vector3.one;
    internal static Vector3 TestBlur => _blur != null ? _blur.TestState : -Vector3.one;
    internal static Rect TestRect => _dark != null ? _dark.TestRect : default;

    // Self tests: mesh builds of the dark ring and of the blur ring so far (-1 = no overlay). A setting that shows
    // on screen = a new build with the new numbers.
    internal static int TestDarkBuilds => _dark != null ? _dark.TestBuilds : -1;
    internal static int TestBlurBuilds => _blur != null ? _blur.TestBuilds : -1;
#endif

    // Personal settings (section View), each read in one place: self test may force them in memory, never the
    // config file.
    private static float ClearViewSize
    {
        get
        {
#if DEBUG
            if (TestClearViewSize.HasValue)
            {
                return TestClearViewSize.Value;
            }
#endif
            return Plugin.ClearViewSize != null ? Plugin.ClearViewSize.Value : DefaultClearSize;
        }
    }

    private static float EdgeDarkness
    {
        get
        {
#if DEBUG
            if (TestEdgeDarkness.HasValue)
            {
                return TestEdgeDarkness.Value;
            }
#endif
            return Plugin.EdgeDarkness != null ? Plugin.EdgeDarkness.Value : DefaultEdgeDarkness;
        }
    }

    private static bool EdgeBlur
    {
        get
        {
#if DEBUG
            if (TestEdgeBlur.HasValue)
            {
                return TestEdgeBlur.Value;
            }
#endif
            return Plugin.EdgeBlur == null || Plugin.EdgeBlur.Value;
        }
    }

    // o: 0 (nothing) .. 1 (full scope view). The circle starts bigger than the screen and closes to its size.
    internal static void Show(float o)
    {
        if (o <= 0.001f)
        {
            Hide();
            return;
        }
        if (_root == null && !Build())
        {
            return;
        }
        var clear = ClearViewSize;
        var dark = EdgeDarkness;
        var blurOn = EdgeBlur;
        var iris = Mathf.Lerp(3.2f, 1f, o);
        if (!_shown)
        {
            _root.SetActive(true);
            _shown = true;
        }
        _dark.Set(clear, iris, o * dark);
        var texture = blurOn ? ScopeCapture.Enable() : null;
        if (texture != null)
        {
            _blur.texture = texture;
            _blur.enabled = true;
            _blur.Set(clear, iris, o);
        }
        else
        {
            _blur.enabled = false;
        }
    }

    internal static void Hide()
    {
        ScopeCapture.Disable();
        if (!_shown)
        {
            return;
        }
        _shown = false;
        if (_root != null)
        {
            _root.SetActive(false);
        }
    }

    internal static void Destroy()
    {
        Hide();
        if (_root != null)
        {
            Object.Destroy(_root);
        }
        _root = null;
        _canvas = null;
        _blur = null;
        _dark = null;
    }

    private static bool Build()
    {
        try
        {
            _root = new GameObject(ModInfo.Guid + ".Overlay");
            Object.DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = ChooseSortingOrder();
            _canvas.pixelPerfect = false;
            _blur = Stretch<BlurRing>("Blur");
            _dark = Stretch<DarkRing>("Dark");
            _blur.raycastTarget = false;
            _dark.raycastTarget = false;
            _dark.color = Color.black;
            _root.SetActive(false);
            return true;
        }
        catch (System.Exception e)
        {
            PatchGuard.Report("ScopeOverlay.Build", e);
            if (_root != null)
            {
                Object.Destroy(_root);
            }
            _root = null;
            return false;
        }
    }

    private static T Stretch<T>(string name) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_root.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return go.AddComponent<T>();
    }

    // Just under the HUD canvas, above the render-scale picture (both orders are prefab data: read live).
    private static int ChooseSortingOrder()
    {
        var hud = RootCanvas(Hud.instance != null ? Hud.instance.m_rootObject : null);
        var fbs = Object.FindAnyObjectByType<FrameBufferScaler>(FindObjectsInactive.Include);
        var frame = RootCanvas(fbs != null ? fbs.gameObject : null);
        var order = hud != null ? hud.sortingOrder - 1 : 0;
        if (frame != null && frame != hud && order <= frame.sortingOrder)
        {
            order = frame.sortingOrder + 1;
        }
#if DEBUG
        Placement = $"hud canvas {(hud != null ? hud.name + " order " + hud.sortingOrder + " mode " + hud.renderMode : "none")}, "
                    + $"render-scale canvas {(frame != null ? frame.name + " order " + frame.sortingOrder : "none")}, ours {order}";
#endif
        return order;
    }

    private static Canvas RootCanvas(GameObject go)
    {
        if (go == null)
        {
            return null;
        }
        var c = go.GetComponentInParent<Canvas>(true);
        return c != null ? c.rootCanvas : null;
    }
}

// Ring mesh helper: rings from the circle's radius outward, alpha from a smooth step, last ring far beyond the
// corners. Radii in pixels of the rect (circle stay round on any screen shape).
internal static class RingMesh
{
    private const int Segments = 96;
    private const int Steps = 10;

    // inner: alpha 0 radius, outer: alpha full radius (both in rect pixels); full = alpha at and beyond outer.
    internal static void Fill(VertexHelper vh, Rect rect, Color32 tint, float inner, float outer, float full, bool screenUv)
    {
        vh.Clear();
        if (full <= 0.001f)
        {
            return;
        }
        var centre = rect.center;
        var far = Mathf.Max(outer * 1.01f, rect.size.magnitude);
        var rings = Steps + 2;
        for (var r = 0; r < rings; r++)
        {
            float radius;
            float a;
            if (r <= Steps)
            {
                var k = r / (float)Steps;
                radius = Mathf.Lerp(inner, outer, k);
                a = full * k * k * (3f - 2f * k);
            }
            else
            {
                radius = far;
                a = full;
            }
            var c = tint;
            c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
            for (var s = 0; s <= Segments; s++)
            {
                var ang = s / (float)Segments * Mathf.PI * 2f;
                var pos = centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
                var uv = screenUv
                    ? new Vector2((pos.x - rect.xMin) / rect.width, (pos.y - rect.yMin) / rect.height)
                    : Vector2.zero;
                vh.AddVert(new Vector3(pos.x, pos.y, 0f), c, uv);
            }
        }
        var row = Segments + 1;
        for (var r = 0; r < rings - 1; r++)
        {
            for (var s = 0; s < Segments; s++)
            {
                var i0 = r * row + s;
                var i1 = i0 + 1;
                var j0 = i0 + row;
                var j1 = j0 + 1;
                vh.AddTriangle(i0, j0, j1);
                vh.AddTriangle(i0, j1, i1);
            }
        }
    }
}

// Blurred picture outside the circle (texture = ScopeCapture's small copy of the frame, stretched to the screen).
internal sealed class BlurRing : RawImage
{
    private float _clear = ScopeOverlay.DefaultClearSize;
    private float _iris = 1f;
    private float _alpha = 1f;

    internal void Set(float clear, float iris, float alpha)
    {
        if (Mathf.Approximately(clear, _clear) && Mathf.Approximately(iris, _iris) && Mathf.Approximately(alpha, _alpha))
        {
            return;
        }
        _clear = clear;
        _iris = iris;
        _alpha = alpha;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        var rect = GetPixelAdjustedRect();
        var r = rect.height * 0.5f * _clear * _iris;
        RingMesh.Fill(vh, rect, new Color32(255, 255, 255, 255), r, r * 1.3f, _alpha, true);
#if DEBUG
        TestBuilds++;
#endif
    }

#if DEBUG
    internal Vector3 TestState => new Vector3(_clear, _iris, _alpha);

    // Self test: how many times Unity built the ring mesh (a change that shows on screen = a new build).
    internal int TestBuilds;
#endif
}

// Dark edge: black, from a bit outside the circle to the screen edge.
internal sealed class DarkRing : MaskableGraphic
{
    private float _clear = ScopeOverlay.DefaultClearSize;
    private float _iris = 1f;
    private float _alpha = 1f;

    internal void Set(float clear, float iris, float alpha)
    {
        if (Mathf.Approximately(clear, _clear) && Mathf.Approximately(iris, _iris) && Mathf.Approximately(alpha, _alpha))
        {
            return;
        }
        _clear = clear;
        _iris = iris;
        _alpha = alpha;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        var rect = GetPixelAdjustedRect();
        var r = rect.height * 0.5f * _clear * _iris;
        RingMesh.Fill(vh, rect, new Color32(0, 0, 0, 255), r * 1.02f, r * 1.7f, _alpha, false);
#if DEBUG
        TestBuilds++;
#endif
    }

#if DEBUG
    internal Vector3 TestState => new Vector3(_clear, _iris, _alpha);
    internal Rect TestRect => GetPixelAdjustedRect();

    // Self test: how many times Unity built the ring mesh (a change that shows on screen = a new build).
    internal int TestBuilds;
#endif
}

// Me = small blurred copy of what the main camera drew this frame (after its image effects, before any UI), for
// the blur ring. OnRenderImage of a component added last to the camera = last image effect. Shrunk by 2 four times
// (each bilinear step averages 2x2: a soft blur), then once back up to 1/8 size: that texture stay for the UI.
// Format without alpha channel (sampled alpha = 1, whatever the effects left there). On only while shown.
internal sealed class ScopeCapture : MonoBehaviour
{
    private static ScopeCapture _instance;
    private static RenderTextureFormat _format;
    private static bool _formatChosen;
    private RenderTexture _result;

#if DEBUG
    // Self tests: me on the camera at all, and me copying frames now.
    internal static bool TestExists => _instance != null;
    internal static bool TestOn => _instance != null && _instance.enabled;
#endif

    // Turn on (adding me to the main camera if needed); the texture the UI shows, null when no camera.
    internal static Texture Enable()
    {
        var cam = GameCamera.instance;
        if (cam == null)
        {
            return null;
        }
        if (_instance == null)
        {
            _instance = cam.gameObject.AddComponent<ScopeCapture>();
        }
        if (!_instance.enabled)
        {
            _instance.enabled = true;
        }
        return _instance._result;
    }

    internal static void Disable()
    {
        if (_instance != null && _instance.enabled)
        {
            _instance.enabled = false;
        }
    }

    internal static void Remove()
    {
        if (_instance != null)
        {
            Destroy(_instance);
        }
        _instance = null;
    }

    private static RenderTextureFormat Format()
    {
        if (_formatChosen)
        {
            return _format;
        }
        _formatChosen = true;
        if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float))
        {
            _format = RenderTextureFormat.RGB111110Float;
        }
        else if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB565))
        {
            _format = RenderTextureFormat.RGB565;
        }
        else
        {
            _format = RenderTextureFormat.Default;
        }
        return _format;
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        try
        {
            Shrink(src);
        }
        catch (System.Exception e)
        {
            PatchGuard.Report("ScopeCapture.OnRenderImage", e);
        }
        // Last call must write dst (Unity warns when an image effect leaves it empty).
        Graphics.Blit(src, dst);
    }

    private void Shrink(RenderTexture src)
    {
        var fmt = Format();
        var w = Mathf.Max(16, src.width / 8);
        var h = Mathf.Max(16, src.height / 8);
        if (_result == null || _result.width != w || _result.height != h)
        {
            if (_result != null)
            {
                _result.Release();
                Destroy(_result);
            }
            _result = new RenderTexture(w, h, 0, fmt, RenderTextureReadWrite.Default)
            {
                name = "MC_Spyglass_Blur",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _result.Create();
        }
        var a = Temp(src.width / 2, src.height / 2, fmt);
        Graphics.Blit(src, a);
        var b = Temp(src.width / 4, src.height / 4, fmt);
        Graphics.Blit(a, b);
        RenderTexture.ReleaseTemporary(a);
        var c = Temp(src.width / 8, src.height / 8, fmt);
        Graphics.Blit(b, c);
        RenderTexture.ReleaseTemporary(b);
        var d = Temp(src.width / 16, src.height / 16, fmt);
        Graphics.Blit(c, d);
        RenderTexture.ReleaseTemporary(c);
        Graphics.Blit(d, _result);
        RenderTexture.ReleaseTemporary(d);
    }

    private static RenderTexture Temp(int w, int h, RenderTextureFormat fmt)
    {
        var rt = RenderTexture.GetTemporary(Mathf.Max(8, w), Mathf.Max(8, h), 0, fmt, RenderTextureReadWrite.Default);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        return rt;
    }

    private void OnDestroy()
    {
        if (_result != null)
        {
            _result.Release();
            Destroy(_result);
            _result = null;
        }
        if (_instance == this)
        {
            _instance = null;
        }
    }
}
