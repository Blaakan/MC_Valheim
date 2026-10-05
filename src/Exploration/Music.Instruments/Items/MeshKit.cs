using System;
using System.Collections.Generic;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = small workshop for meshes made in code: points, normals, uv and triangles per material slot, plus shapes
// (profile swept along a rail = lathe or extruded outline, flat polygon, box of 8 corners, tube, dome, fan patch).
// Me stay pure maths until ToMesh (no Mesh, no Mathf.PerlinNoise, no Quaternion maker): preview tool run same code
// outside game. Winding: me turn every face so its front (Unity: clockwise on screen) look where its normals look.
// Shapes just give right normals.
internal sealed class MeshKit
{
    internal readonly List<Vector3> Verts = new List<Vector3>();
    internal readonly List<Vector3> Normals = new List<Vector3>();
    internal readonly List<Vector2> Uvs = new List<Vector2>();
    internal readonly List<int>[] Tris;

    internal MeshKit(int slots)
    {
        Tris = new List<int>[slots];
        for (var i = 0; i < slots; i++)
        {
            Tris[i] = new List<int>();
        }
    }

    internal int Add(Vector3 p, Vector3 n, Vector2 uv)
    {
        Verts.Add(p);
        Normals.Add(Unit(n));
        Uvs.Add(uv);
        return Verts.Count - 1;
    }

    // Triangle a b c in any turn: me flip it so its front face where its normals look.
    internal void Tri(int slot, int a, int b, int c)
    {
        var face = Vector3.Cross(Verts[b] - Verts[a], Verts[c] - Verts[a]);
        var n = Normals[a] + Normals[b] + Normals[c];
        var t = Tris[slot];
        t.Add(a);
        if (Vector3.Dot(face, n) >= 0f)
        {
            t.Add(b);
            t.Add(c);
        }
        else
        {
            t.Add(c);
            t.Add(b);
        }
    }

    // Quad a b c d going round its edge (either way): two triangles cut on a-c, front where the normals look.
    // Cross of the diagonals = quad facing, still good when two corners sit on the same spot (lathe centre).
    internal void Quad(int slot, int a, int b, int c, int d)
    {
        var face = Vector3.Cross(Verts[c] - Verts[a], Verts[d] - Verts[b]);
        var n = Normals[a] + Normals[b] + Normals[c] + Normals[d];
        var t = Tris[slot];
        if (Vector3.Dot(face, n) >= 0f)
        {
            t.Add(a);
            t.Add(b);
            t.Add(c);
            t.Add(a);
            t.Add(c);
            t.Add(d);
        }
        else
        {
            t.Add(a);
            t.Add(c);
            t.Add(b);
            t.Add(a);
            t.Add(d);
            t.Add(c);
        }
    }

    internal int TriangleCount
    {
        get
        {
            var n = 0;
            for (var i = 0; i < Tris.Length; i++)
            {
                n += Tris[i].Count / 3;
            }
            return n;
        }
    }

    // One smooth run of a profile in its plane: x = r (out from the rail), y = h (along the sweep axis). Normals smooth
    // inside a run; two runs meet at a hard edge. Side +1 = normal on the right of the walk (r right, h up), -1 = left
    // (so: walk with the solid on the left for +1, on the right for -1).
    internal sealed class Run
    {
        internal readonly int Slot;
        internal readonly float Side;
        internal readonly List<Vector2> Points = new List<Vector2>();

        internal Run(int slot, float side)
        {
            Slot = slot;
            Side = side;
        }

        internal Run To(float r, float h)
        {
            var p = new Vector2(r, h);
            if (Points.Count == 0 || (Points[Points.Count - 1] - p).sqrMagnitude > 1e-14f)
            {
                Points.Add(p);
            }
            return this;
        }

        // Arc round (cr, ch) from angle a0 to a1 (degrees) in steps pieces. First point on top of last one: me skip it.
        internal Run Arc(float cr, float ch, float radius, float a0, float a1, int steps)
        {
            for (var i = 0; i <= steps; i++)
            {
                var a = (a0 + (a1 - a0) * i / steps) * Mathf.Deg2Rad;
                To(cr + Mathf.Cos(a) * radius, ch + Mathf.Sin(a) * radius);
            }
            return this;
        }

        // Normal of each point (x = r part, y = h part): mean of the pieces next to it.
        internal Vector2[] PointNormals()
        {
            var n = Points.Count;
            var piece = new Vector2[Math.Max(0, n - 1)];
            for (var i = 0; i < n - 1; i++)
            {
                var d = Points[i + 1] - Points[i];
                piece[i] = Unit(new Vector2(d.y, -d.x)) * Side;
            }
            var result = new Vector2[n];
            for (var i = 0; i < n; i++)
            {
                var s = Vector2.zero;
                if (i > 0)
                {
                    s += piece[i - 1];
                }
                if (i < n - 1)
                {
                    s += piece[i];
                }
                result[i] = Unit(s);
            }
            return result;
        }

        // Walked length up to each point.
        internal float[] Lengths()
        {
            var result = new float[Points.Count];
            for (var i = 1; i < Points.Count; i++)
            {
                result[i] = result[i - 1] + (Points[i] - Points[i - 1]).magnitude;
            }
            return result;
        }
    }

    // Rail point: profile x go along Out from Base; U = texture u there.
    internal struct RailPoint
    {
        internal Vector3 Base;
        internal Vector3 Out;
        internal float U;
    }

    // How a sweep lay its texture: u from the rail, v from the run (walked length, or height h), per metre.
    internal struct UvMap
    {
        internal float VPerMetre;
        internal float V0;
        internal bool ByHeight;
        internal bool Swap;                     // u <-> v
        internal Func<Vector3, Vector2> Planar; // not null: uv from the point itself

        internal static UvMap Length(float perMetre) => new UvMap { VPerMetre = perMetre };
    }

    // Rail on a circle round centre in the plane e1 e2, angles a0..a1 (radians), segments pieces. Full turn: last
    // point reuse the first direction so the seam close exactly.
    internal static RailPoint[] Circle(Vector3 centre, Vector3 e1, Vector3 e2, float a0, float a1, int segments,
        float uRepeat)
    {
        var rail = new RailPoint[segments + 1];
        var full = Mathf.Abs(Mathf.Abs(a1 - a0) - Mathf.PI * 2f) < 1e-4f;
        for (var i = 0; i <= segments; i++)
        {
            var k = full && i == segments ? 0 : i;
            var a = a0 + (a1 - a0) * k / segments;
            rail[i] = new RailPoint
            {
                Base = centre,
                Out = e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a),
                U = i / (float)segments * uRepeat,
            };
        }
        return rail;
    }

    // Part of a rail (first..last, both kept).
    internal static RailPoint[] Slice(RailPoint[] rail, int first, int last)
    {
        var part = new RailPoint[last - first + 1];
        Array.Copy(rail, first, part, 0, part.Length);
        return part;
    }

    // Profile run swept along a rail: point = Base + Out*r + axis*h, normal = Out*nr + axis*nh.
    // skip(railPiece, runPiece) true = leave that quad out (a hole).
    internal void Sweep(RailPoint[] rail, Vector3 axis, Run run, UvMap uv, Func<int, int, bool> skip = null)
    {
        var pts = run.Points;
        if (pts.Count < 2 || rail.Length < 2)
        {
            return;
        }
        var normals = run.PointNormals();
        var lengths = run.Lengths();
        var cols = pts.Count;
        var start = Verts.Count;
        for (var k = 0; k < rail.Length; k++)
        {
            var rp = rail[k];
            for (var j = 0; j < cols; j++)
            {
                var p = rp.Base + rp.Out * pts[j].x + axis * pts[j].y;
                var n = rp.Out * normals[j].x + axis * normals[j].y;
                Vector2 tex;
                if (uv.Planar != null)
                {
                    tex = uv.Planar(p);
                }
                else
                {
                    var v = uv.V0 + (uv.ByHeight ? pts[j].y : lengths[j]) * uv.VPerMetre;
                    tex = uv.Swap ? new Vector2(v, rp.U) : new Vector2(rp.U, v);
                }
                Add(p, n, tex);
            }
        }
        for (var k = 0; k < rail.Length - 1; k++)
        {
            for (var j = 0; j < cols - 1; j++)
            {
                if (skip != null && skip(k, j))
                {
                    continue;
                }
                var a = start + k * cols + j;
                Quad(run.Slot, a, a + 1, a + cols + 1, a + cols);
            }
        }
    }

    // Flat simple polygon (any turn, no hole): 2D points put in space by place, one normal, uv from the 2D point.
    internal void Polygon(int slot, IList<Vector2> pts, Func<Vector2, Vector3> place, Vector3 normal,
        Func<Vector2, Vector2> uvOf)
    {
        var start = Verts.Count;
        for (var i = 0; i < pts.Count; i++)
        {
            Add(place(pts[i]), normal, uvOf(pts[i]));
        }
        var tris = EarClip(pts);
        for (var i = 0; i + 2 < tris.Count; i += 3)
        {
            Tri(slot, start + tris[i], start + tris[i + 1], start + tris[i + 2]);
        }
    }

    // Patch from a centre over a closed ring (convex), each 2D point put on a surface by place / normal.
    internal void Fan(int slot, Vector2 centre, IList<Vector2> ring, Func<Vector2, Vector3> place,
        Func<Vector2, Vector3> normal, Func<Vector2, Vector2> uvOf)
    {
        var c = Add(place(centre), normal(centre), uvOf(centre));
        var first = Verts.Count;
        for (var i = 0; i < ring.Count; i++)
        {
            Add(place(ring[i]), normal(ring[i]), uvOf(ring[i]));
        }
        for (var i = 0; i < ring.Count; i++)
        {
            Tri(slot, c, first + i, first + (i + 1) % ring.Count);
        }
    }

    // Box from 8 corners: c[0..3] one face going round, c[4..7] the far face, c[4+i] across from c[i]. Flat sides.
    internal void Hexa(int slot, Vector3[] c, float uvPerMetre)
    {
        var centre = Vector3.zero;
        for (var i = 0; i < 8; i++)
        {
            centre += c[i];
        }
        centre /= 8f;
        AddHexaFace(slot, c[0], c[1], c[2], c[3], centre, uvPerMetre);
        AddHexaFace(slot, c[4], c[5], c[6], c[7], centre, uvPerMetre);
        AddHexaFace(slot, c[0], c[1], c[5], c[4], centre, uvPerMetre);
        AddHexaFace(slot, c[1], c[2], c[6], c[5], centre, uvPerMetre);
        AddHexaFace(slot, c[2], c[3], c[7], c[6], centre, uvPerMetre);
        AddHexaFace(slot, c[3], c[0], c[4], c[7], centre, uvPerMetre);
    }

    private void AddHexaFace(int slot, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 centre,
        float uvPerMetre)
    {
        var n = Unit(Vector3.Cross(p2 - p0, p3 - p1));
        if (Vector3.Dot(n, (p0 + p1 + p2 + p3) * 0.25f - centre) < 0f)
        {
            n = -n;
        }
        var t1 = Unit(p1 - p0);
        var t2 = Vector3.Cross(n, t1);
        var a = Add(p0, n, Vector2.zero);
        var b = Add(p1, n, new Vector2(Vector3.Dot(p1 - p0, t1), Vector3.Dot(p1 - p0, t2)) * uvPerMetre);
        var c = Add(p2, n, new Vector2(Vector3.Dot(p2 - p0, t1), Vector3.Dot(p2 - p0, t2)) * uvPerMetre);
        var d = Add(p3, n, new Vector2(Vector3.Dot(p3 - p0, t1), Vector3.Dot(p3 - p0, t2)) * uvPerMetre);
        Quad(slot, a, b, c, d);
    }

    // Round tube from p0 (radius r0) to p1 (radius r1), smooth sides, flat caps when asked.
    internal void Tube(int slot, Vector3 p0, Vector3 p1, float r0, float r1, int segments, bool cap0, bool cap1)
    {
        var axis = p1 - p0;
        var len = axis.magnitude;
        if (len < 1e-6f)
        {
            return;
        }
        axis /= len;
        Perp(axis, out var e1, out var e2);
        var rail = Circle(p0, e1, e2, 0f, Mathf.PI * 2f, segments, 1f);
        var uv = UvMap.Length(1f / Math.Max(len, 0.01f));
        Sweep(rail, axis, new Run(slot, 1f).To(r0, 0f).To(r1, len), uv);
        if (cap1)
        {
            Sweep(rail, axis, new Run(slot, 1f).To(r1, len).To(0f, len), uv);
        }
        if (cap0)
        {
            Sweep(rail, axis, new Run(slot, 1f).To(0f, 0f).To(r0, 0f), uv);
        }
    }

    // Low dome (stud, tack head) standing on centre, looking along up.
    internal void Dome(int slot, Vector3 centre, Vector3 up, float radius, float height, int segments, int rings)
    {
        up = Unit(up);
        Perp(up, out var e1, out var e2);
        var rail = Circle(centre, e1, e2, 0f, Mathf.PI * 2f, segments, 1f);
        var run = new Run(slot, 1f);
        for (var i = 0; i <= rings; i++)
        {
            var a = Mathf.PI * 0.5f * i / rings;
            run.To(Mathf.Cos(a) * radius, Mathf.Sin(a) * height);
        }
        Sweep(rail, up, run, UvMap.Length(1f / Math.Max(radius, 0.001f)));
    }

    // Two unit vectors square to axis and to each other.
    internal static void Perp(Vector3 axis, out Vector3 e1, out Vector3 e2)
    {
        var helper = Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right;
        e1 = Unit(Vector3.Cross(axis, helper));
        e2 = Unit(Vector3.Cross(axis, e1));
    }

    // Unit vector, tiny ones too (Unity normalized give zero under 1e-5, and millimetre pieces go smaller).
    internal static Vector3 Unit(Vector3 v)
    {
        var m = (float)Math.Sqrt(v.x * (double)v.x + v.y * (double)v.y + v.z * (double)v.z);
        return m > 1e-20f ? new Vector3(v.x / m, v.y / m, v.z / m) : Vector3.zero;
    }

    internal static Vector2 Unit(Vector2 v)
    {
        var m = (float)Math.Sqrt(v.x * (double)v.x + v.y * (double)v.y);
        return m > 1e-20f ? new Vector2(v.x / m, v.y / m) : Vector2.zero;
    }

    // Ear clipping of a simple polygon (any turn, no hole): triangles as index triples into pts. Corner points on a
    // straight edge stay in (no crack against a neighbour face that use them).
    internal static List<int> EarClip(IList<Vector2> pts)
    {
        var result = new List<int>();
        var n = pts.Count;
        if (n < 3)
        {
            return result;
        }
        var idx = new List<int>(n);
        var area = 0f;
        for (var i = 0; i < n; i++)
        {
            idx.Add(i);
            var a = pts[i];
            var b = pts[(i + 1) % n];
            area += a.x * b.y - b.x * a.y;
        }
        var turn = area >= 0f ? 1f : -1f;
        var guard = n * n + 16;
        while (idx.Count > 3 && guard-- > 0)
        {
            var cut = -1;
            var best = -1;
            var bestTurn = 0f;
            for (var i = 0; i < idx.Count && cut < 0; i++)
            {
                var ia = idx[(i + idx.Count - 1) % idx.Count];
                var ib = idx[i];
                var ic = idx[(i + 1) % idx.Count];
                var corner = Cross(pts[ia], pts[ib], pts[ic]) * turn;
                if (corner > bestTurn)
                {
                    bestTurn = corner;
                    best = i;
                }
                if (corner <= 1e-10f)
                {
                    continue; // hollow or straight corner: no ear
                }
                var empty = true;
                for (var j = 0; j < idx.Count && empty; j++)
                {
                    var q = idx[j];
                    if (q == ia || q == ib || q == ic)
                    {
                        continue;
                    }
                    var p = pts[q];
                    if (Same(p, pts[ia]) || Same(p, pts[ib]) || Same(p, pts[ic]))
                    {
                        continue;
                    }
                    if (InTriangle(p, pts[ia], pts[ib], pts[ic]))
                    {
                        empty = false;
                    }
                }
                if (empty)
                {
                    cut = i;
                }
            }
            if (cut < 0)
            {
                cut = best; // stuck (bad input): cut the most convex corner anyway
            }
            if (cut < 0)
            {
                break;
            }
            result.Add(idx[(cut + idx.Count - 1) % idx.Count]);
            result.Add(idx[cut]);
            result.Add(idx[(cut + 1) % idx.Count]);
            idx.RemoveAt(cut);
        }
        if (idx.Count == 3)
        {
            result.Add(idx[0]);
            result.Add(idx[1]);
            result.Add(idx[2]);
        }
        return result;
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    private static bool Same(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-14f;

    // Inside or on the edge.
    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var d1 = Cross(a, b, p);
        var d2 = Cross(b, c, p);
        var d3 = Cross(c, a, p);
        var neg = d1 < -1e-12f || d2 < -1e-12f || d3 < -1e-12f;
        var pos = d1 > 1e-12f || d2 > 1e-12f || d3 > 1e-12f;
        return !(neg && pos);
    }

#if !OFFLINE_PREVIEW
    // Unity mesh of the kit: one submesh per used slot; slots = which kit slot each submesh is. Throw part way = me
    // destroy the half made mesh, throw on.
    internal Mesh ToMesh(string name, out int[] slots)
    {
        var used = new List<int>();
        for (var i = 0; i < Tris.Length; i++)
        {
            if (Tris[i].Count > 0)
            {
                used.Add(i);
            }
        }
        var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        try
        {
            if (Verts.Count > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.SetVertices(Verts);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            mesh.subMeshCount = used.Count;
            for (var i = 0; i < used.Count; i++)
            {
                mesh.SetTriangles(Tris[used[i]], i);
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(false);
        }
        catch
        {
            UnityEngine.Object.Destroy(mesh);
            throw;
        }
        slots = used.ToArray();
        return mesh;
    }

    // Collider mesh of the kit: points and all triangles in one submesh, no normals or uv (physics need none). Me keep
    // it readable (no UploadMeshData(true)): MeshCollider read the points. Throw part way = me destroy it, throw on.
    internal Mesh ToCollisionMesh(string name)
    {
        var tris = new List<int>();
        for (var i = 0; i < Tris.Length; i++)
        {
            tris.AddRange(Tris[i]);
        }
        var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        try
        {
            mesh.SetVertices(Verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
        catch
        {
            UnityEngine.Object.Destroy(mesh);
            throw;
        }
        return mesh;
    }
#endif
}

// Me = noise for small textures made in code: value noise on a lattice that wrap, so texture tile with no seam.
// Pure maths (Mathf.PerlinNoise = engine code, and it no wrap).
internal static class TexNoise
{
    internal static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    private static int Wrap(int i, int period) => ((i % period) + period) % period;

    // Smooth noise 0..1 at (x, y) in lattice cells; the lattice repeat every px, py cells.
    internal static float Value(float x, float y, int px, int py, int seed)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        var xa = Wrap(x0, px);
        var xb = Wrap(x0 + 1, px);
        var ya = Wrap(y0, py);
        var yb = Wrap(y0 + 1, py);
        var a = Hash(xa, ya, seed) + (Hash(xb, ya, seed) - Hash(xa, ya, seed)) * fx;
        var b = Hash(xa, yb, seed) + (Hash(xb, yb, seed) - Hash(xa, yb, seed)) * fx;
        return a + (b - a) * fy;
    }

    // Octaves of Value at texture spot (u, v in 0..1): cx, cy cells on the first octave, each octave twice as fine.
    internal static float Fbm(float u, float v, int cx, int cy, int octaves, int seed)
    {
        var sum = 0f;
        var amp = 0.5f;
        var norm = 0f;
        for (var o = 0; o < octaves; o++)
        {
            sum += amp * Value(u * cx, v * cy, cx, cy, seed + o * 101);
            norm += amp;
            amp *= 0.5f;
            cx *= 2;
            cy *= 2;
        }
        return sum / norm;
    }
}
