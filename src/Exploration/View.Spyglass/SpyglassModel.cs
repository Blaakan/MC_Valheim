using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewSpyglassMod;

// Me = the spyglass 3D model, made in code once (no asset bundle): a lathe (shape turned around its axis) of three
// telescope tubes of bronze with darker rings, leather wrap on the big tube, flared lens end, crystal lenses at both
// ends. Model space: origin = where the hand grip it, +Z = toward the big lens, eyepiece at z = -GripToEye, unit =
// metre. Four materials, each a copy of a vanilla item material (so the game's own shader light it like other items)
// with a small texture drawn here instead of the vanilla one. Mesh and materials shared by the hand copy and the
// dropped copy, kept whole session.
internal static class SpyglassModel
{
    internal const string ModelName = "MC_SpyglassModel";
    internal const string EyepieceName = "MC_SpyglassEyepiece";
    internal const float Length = 0.36f;
    internal const float GripToEye = 0.115f;   // eyepiece this far behind the grip
    internal const float LensRadius = 0.0242f; // widest point (lens end)
    private const int Segments = 20;

    private const int MatBronze = 0;
    private const int MatDark = 1;
    private const int MatLeather = 2;
    private const int MatGlass = 3;

    private static Mesh _mesh;
    private static Material[] _materials;

    // Tube profile from the eyepiece (z = 0) to the lens (z = Length): band from z0 to z1, radius r0 -> r1.
    private struct Band
    {
        internal float Z0;
        internal float Z1;
        internal float R0;
        internal float R1;
        internal int Mat;

        internal Band(float z0, float z1, float r0, float r1, int mat)
        {
            Z0 = z0;
            Z1 = z1;
            R0 = r0;
            R1 = r1;
            Mat = mat;
        }
    }

    private static readonly Band[] Profile =
    {
        new Band(0.000f, 0.016f, 0.0165f, 0.0165f, MatDark),    // eye cup
        new Band(0.016f, 0.100f, 0.0128f, 0.0128f, MatBronze),  // eyepiece tube
        new Band(0.100f, 0.112f, 0.0158f, 0.0158f, MatDark),    // ring 1
        new Band(0.112f, 0.195f, 0.0155f, 0.0155f, MatBronze),  // middle tube
        new Band(0.195f, 0.207f, 0.0188f, 0.0188f, MatDark),    // ring 2
        new Band(0.207f, 0.225f, 0.0185f, 0.0185f, MatBronze),  // main tube
        new Band(0.225f, 0.305f, 0.0195f, 0.0195f, MatLeather), // leather wrap
        new Band(0.305f, 0.322f, 0.0185f, 0.0185f, MatBronze),  // main tube
        new Band(0.322f, 0.348f, 0.0185f, 0.0235f, MatBronze),  // flare
        new Band(0.348f, Length, 0.0242f, 0.0242f, MatDark),    // lip
    };

    internal static Mesh Mesh => _mesh ??= BuildMesh();

    // New model object (layer of the parent's item) under parent. localScale: size factor (1 = 36 cm long).
    // Dedicated server (no graphics device): object, eyepiece marker and collider only, no mesh, texture or material.
    internal static GameObject Create(Transform parent, Material baseMaterial, float scale)
    {
        var go = new GameObject(ModelName);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * scale;
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = Mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = Materials(baseMaterial);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
        var eye = new GameObject(EyepieceName);
        eye.layer = go.layer;
        eye.transform.SetParent(go.transform, false);
        eye.transform.localPosition = new Vector3(0f, 0f, -GripToEye);
        return go;
    }

    // Centre of the tube in model space (dropped copy is centred on the vanilla model it replaces).
    internal static Vector3 Centre => new Vector3(0f, 0f, Length * 0.5f - GripToEye);

    private static Material[] Materials(Material baseMaterial)
    {
        if (_materials != null)
        {
            return _materials;
        }
        _materials = new[]
        {
            Make(baseMaterial, "MC_Spyglass_Bronze", Brushed(new Color(0.80f, 0.56f, 0.27f), 0.07f), 0.75f, 0.55f),
            Make(baseMaterial, "MC_Spyglass_DarkBronze", Brushed(new Color(0.40f, 0.26f, 0.13f), 0.06f), 0.7f, 0.45f),
            Make(baseMaterial, "MC_Spyglass_Leather", LeatherTexture(), 0f, 0.2f),
            Make(baseMaterial, "MC_Spyglass_Glass", Flat(new Color(0.20f, 0.36f, 0.46f)), 0.3f, 0.95f),
        };
        return _materials;
    }

    // Copy of the vanilla material with our texture as its main texture; its own detail textures (laid out for the
    // vanilla mesh) taken off. Null base (no vanilla material found) = plain default material.
    private static Material Make(Material baseMaterial, string name, Texture2D tex, float metallic, float smoothness)
    {
        Material m;
        if (baseMaterial != null)
        {
            m = new Material(baseMaterial);
        }
        else
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            m = new Material(shader);
        }
        m.name = name;
        m.hideFlags = HideFlags.HideAndDontSave;
        foreach (var prop in m.GetTexturePropertyNames())
        {
            if (prop == "_MainTex")
            {
                continue;
            }
            if (m.HasProperty(prop) && m.GetTexture(prop) != null)
            {
                m.SetTexture(prop, null);
            }
        }
        m.mainTexture = tex;
        m.mainTextureScale = Vector2.one;
        m.mainTextureOffset = Vector2.zero;
        if (m.HasProperty("_Color"))
        {
            m.SetColor("_Color", Color.white);
        }
        if (m.HasProperty("_Metallic"))
        {
            m.SetFloat("_Metallic", metallic);
        }
        if (m.HasProperty("_Glossiness"))
        {
            m.SetFloat("_Glossiness", smoothness);
        }
        m.DisableKeyword("_NORMALMAP");
        m.DisableKeyword("_METALLICGLOSSMAP");
        m.DisableKeyword("_EMISSION");
        return m;
    }

    // Debug: what the vanilla base material offers (shader name, texture slots), to check Make in game.
    internal static string Describe(Material m)
    {
        if (m == null)
        {
            return "none";
        }
        var names = new List<string>();
        foreach (var prop in m.GetTexturePropertyNames())
        {
            names.Add(prop + (m.GetTexture(prop) != null ? "*" : ""));
        }
        return $"{m.name} shader={(m.shader != null ? m.shader.name : "null")} textures=[{string.Join(", ", names.ToArray())}]"
               + $" color={(m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "-")}"
               + $" metallic={(m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString("0.##") : "-")}"
               + $" gloss={(m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness").ToString("0.##") : "-")}";
    }

    private static Texture2D NewTexture(string name)
    {
        return new Texture2D(32, 64, TextureFormat.RGBA32, true)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.HideAndDontSave,
        };
    }

    // Metal brushed along the tube: colour with faint streaks (v = along the tube, u = around).
    private static Texture2D Brushed(Color c, float streak)
    {
        var tex = NewTexture("MC_Spyglass_Metal");
        var px = new Color[tex.width * tex.height];
        for (var y = 0; y < tex.height; y++)
        {
            for (var x = 0; x < tex.width; x++)
            {
                var n = Mathf.PerlinNoise(x * 0.9f, y * 0.08f) - 0.5f;
                var k = 1f + n * streak * 2f;
                px[y * tex.width + x] = new Color(c.r * k, c.g * k, c.b * k, 1f);
            }
        }
        tex.SetPixels(px);
        tex.Apply(true, true);
        return tex;
    }

    // Brown leather with grain and two stitch rows near its ends (v covers the whole tube length).
    private static Texture2D LeatherTexture()
    {
        var tex = NewTexture("MC_Spyglass_Leather");
        var px = new Color[tex.width * tex.height];
        var baseColor = new Color(0.30f, 0.17f, 0.09f);
        var stitch = new Color(0.66f, 0.55f, 0.38f);
        for (var y = 0; y < tex.height; y++)
        {
            var v = y / (float)tex.height;
            for (var x = 0; x < tex.width; x++)
            {
                var n = Mathf.PerlinNoise(x * 0.5f + 7f, y * 0.5f + 3f) - 0.5f;
                var c = baseColor * (1f + n * 0.35f);
                // Leather spans z 0.225..0.305 of 0.36: stitch rows just inside both ends.
                var z = v * Length;
                var rowA = Mathf.Abs(z - 0.231f) < 0.0018f;
                var rowB = Mathf.Abs(z - 0.299f) < 0.0018f;
                if ((rowA || rowB) && x % 4 < 2)
                {
                    c = stitch;
                }
                c.a = 1f;
                px[y * tex.width + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply(true, true);
        return tex;
    }

    private static Texture2D Flat(Color c)
    {
        var tex = NewTexture("MC_Spyglass_Glass");
        var px = new Color[tex.width * tex.height];
        for (var i = 0; i < px.Length; i++)
        {
            px[i] = c;
        }
        tex.SetPixels(px);
        tex.Apply(true, true);
        return tex;
    }

    // Lathe: every band = ring of quads; radius steps between bands = flat rings; both ends = lens disc inside a
    // dark rim. Normals smooth around, hard at band edges. uv: u around, v along (0 eyepiece, 1 lens).
    private static Mesh BuildMesh()
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>[4];
        for (var i = 0; i < tris.Length; i++)
        {
            tris[i] = new List<int>();
        }

        for (var b = 0; b < Profile.Length; b++)
        {
            var band = Profile[b];
            var dz = band.Z1 - band.Z0;
            var dr = band.R1 - band.R0;
            var len = Mathf.Sqrt(dz * dz + dr * dr);
            var nRadial = dz / len;
            var nAxial = -dr / len;
            var start = verts.Count;
            for (var s = 0; s <= Segments; s++)
            {
                var a = s / (float)Segments * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                var n = (radial * nRadial + Vector3.forward * nAxial).normalized;
                verts.Add(radial * band.R0 + Vector3.forward * (band.Z0 - GripToEye));
                verts.Add(radial * band.R1 + Vector3.forward * (band.Z1 - GripToEye));
                normals.Add(n);
                normals.Add(n);
                uvs.Add(new Vector2(s / (float)Segments, band.Z0 / Length));
                uvs.Add(new Vector2(s / (float)Segments, band.Z1 / Length));
            }
            for (var s = 0; s < Segments; s++)
            {
                var i0 = start + s * 2;
                Quad(tris[band.Mat], i0, i0 + 1, i0 + 3, i0 + 2);
            }
            // Step to the next band: flat ring facing the smaller side.
            if (b + 1 < Profile.Length)
            {
                var next = Profile[b + 1];
                if (Mathf.Abs(next.R0 - band.R1) > 0.0001f)
                {
                    var outward = next.R0 > band.R1 ? -1f : 1f; // bigger next = ring faces the eyepiece
                    var mat = next.R0 > band.R1 ? next.Mat : band.Mat;
                    Annulus(verts, normals, uvs, tris[mat], band.Z1, Mathf.Min(band.R1, next.R0),
                        Mathf.Max(band.R1, next.R0), outward);
                }
            }
        }
        // Eyepiece end: dark rim, small lens.
        Annulus(verts, normals, uvs, tris[MatDark], 0f, 0.0075f, Profile[0].R0, -1f);
        Disc(verts, normals, uvs, tris[MatGlass], 0.0006f, 0.0075f, -1f);
        // Lens end: thin dark rim, big lens set slightly in.
        var last = Profile[Profile.Length - 1];
        Annulus(verts, normals, uvs, tris[MatDark], Length, 0.0205f, last.R1, 1f);
        Disc(verts, normals, uvs, tris[MatGlass], Length - 0.0008f, 0.0205f, 1f);

        var mesh = new Mesh { name = "MC_Spyglass" };
        mesh.hideFlags = HideFlags.HideAndDontSave;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = tris.Length;
        for (var i = 0; i < tris.Length; i++)
        {
            mesh.SetTriangles(tris[i], i);
        }
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        mesh.UploadMeshData(false);
        return mesh;
    }

    // a b c d counter-clockwise seen from outside (Unity front face = clockwise in screen: me emit a c b / a d c).
    private static void Quad(List<int> t, int a, int b, int c, int d)
    {
        t.Add(a);
        t.Add(c);
        t.Add(b);
        t.Add(a);
        t.Add(d);
        t.Add(c);
    }

    // Flat ring at profile z, radii rIn..rOut, facing +Z (facing 1) or -Z (facing -1).
    private static void Annulus(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, float z, float rIn,
        float rOut, float facing)
    {
        var start = v.Count;
        var normal = Vector3.forward * facing;
        for (var s = 0; s <= Segments; s++)
        {
            var a = s / (float)Segments * Mathf.PI * 2f;
            var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            v.Add(radial * rIn + Vector3.forward * (z - GripToEye));
            v.Add(radial * rOut + Vector3.forward * (z - GripToEye));
            n.Add(normal);
            n.Add(normal);
            uv.Add(new Vector2(s / (float)Segments, z / Length));
            uv.Add(new Vector2(s / (float)Segments, z / Length));
        }
        for (var s = 0; s < Segments; s++)
        {
            var i0 = start + s * 2;
            if (facing > 0f)
            {
                Quad(t, i0, i0 + 2, i0 + 3, i0 + 1);
            }
            else
            {
                Quad(t, i0, i0 + 1, i0 + 3, i0 + 2);
            }
        }
    }

    private static void Disc(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, float z, float r,
        float facing)
    {
        var centre = v.Count;
        var normal = Vector3.forward * facing;
        v.Add(Vector3.forward * (z - GripToEye));
        n.Add(normal);
        uv.Add(new Vector2(0.5f, z / Length));
        for (var s = 0; s <= Segments; s++)
        {
            var a = s / (float)Segments * Mathf.PI * 2f;
            v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z - GripToEye));
            n.Add(normal);
            uv.Add(new Vector2(s / (float)Segments, z / Length));
        }
        for (var s = 0; s < Segments; s++)
        {
            var a = centre + 1 + s;
            if (facing > 0f)
            {
                t.Add(centre);
                t.Add(a);
                t.Add(a + 1);
            }
            else
            {
                t.Add(centre);
                t.Add(a + 1);
                t.Add(a);
            }
        }
    }
}
