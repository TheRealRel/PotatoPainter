// Paint on the ground follows the ground as the GPU draws it, not as its collider lies. The game's ground shader
// (Custom/Heightmap, bundle b7b69eaf) moves the drawn ground away from the collision mesh (read from its compiled programs,
// 2026-10-30; claude/potatopainter-research-3.md):
//  - always (vertex shader, or domain shader with tessellation): Mountain and Deep North (vertex colour (0,1,0,0)) up
//    0.1 m where the paint mask has no dirt, cultivation or paving; Ashlands (1,0,0,1) up 0.1 m, plus 0.8 m x mask alpha;
//    beyond 100 m from the camera, up to 1 m away from the water level (smoothstep over 200 m);
//  - with the Tessellation graphics option (shader keyword TESSELATION_ON): each 1 m ground triangle is tessellated
//    (fractional_odd, factor _Tess x clamp(1 - (distance - 10) x 0.025, 0.01, 1) per corner, edges the mean of their two
//    corners, inside the mean of all three) and every tessellated point gets bumps
//    sin(1.4x) cos(1.5z) sin(2.235x) cos(2.435z) x (0.25 - 0.15 x sat(10 x paved + 4 x dirt)) x (1 - Mistlands weight),
//    limited where Mountain snow already raised it, none on a chunk's border. These are the "bubbles" that hid parts of
//    ground runes in test 0.1.7.
// So each ground sheet is laid on that drawn surface: the same tessellation (Tess.cs, the D3D11 reference tessellator),
// the same displacement per tessellated point, the same linear interpolation between them. The tessellation changes with
// the camera's distance, so visible ground sheets are refreshed whenever the camera moves.
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PotatoPainter
{
    internal static class Ground
    {
        private static readonly AccessTools.FieldRef<Heightmap, Mesh> RenderMesh = AccessTools.FieldRefAccess<Heightmap, Mesh>("m_renderMesh");
        private static readonly AccessTools.FieldRef<Heightmap, Texture2D> PaintMask = AccessTools.FieldRefAccess<Heightmap, Texture2D>("m_paintMask");
        private static readonly AccessTools.FieldRef<Heightmap, Material> MaterialInstance = AccessTools.FieldRefAccess<Heightmap, Material>("m_materialInstance");

        internal sealed class Sheet
        {
            internal Mesh Mesh; internal Renderer R; internal Heightmap Hm;
            internal Transform Space;                 // the sheet's own space: the heightmap (placed paint) or null (world, preview)
            internal int[] Verts;                     // sheet vertices that lie on this ground
            internal int[] Tri;                       // per entry: index into Tris
            internal Vector2[] Domain;                // per entry: (u, v) on its ground triangle (weights of corners 0 and 1)
            internal Vector3[] Base, Normal;          // per entry: point on the collision surface and its normal, in Space
            internal float Lift;
            internal readonly List<Corner3> Tris = new List<Corner3>();
            internal Vector3[] Buf;
            internal Vector3 LastCam = Vector3.positiveInfinity; internal bool LastTess; internal int LastFrame = -1;
        }

        internal struct Corner3 { internal Vector3 P0, P1, P2; internal Color C0, C1, C2; internal Vector2 UV0, UV1, UV2; }

        internal static readonly List<Sheet> All = new List<Sheet>();
        private static readonly Tess tess = new Tess();

        // Called by Trace.Build for a sheet traced on a heightmap: entries for its vertices on that ground.
        internal static Sheet Make(Heightmap hm, Mesh mesh, Renderer r, Transform space, float lift, List<(int vert, int i0, int i1, int i2, Vector3 bary, Vector3 baseInSpace, Vector3 normalInSpace)> on)
        {
            var rm = hm ? RenderMesh(hm) : null;
            if (!hm || !rm || !rm.isReadable || on.Count == 0) return null;
            var verts = rm.vertices; var colors = rm.colors; var uvs = rm.uv;
            if (colors == null || colors.Length != verts.Length || uvs == null || uvs.Length != verts.Length) return null;
            var s = new Sheet { Mesh = mesh, R = r, Hm = hm, Space = space, Lift = lift, Verts = new int[on.Count], Tri = new int[on.Count], Domain = new Vector2[on.Count], Base = new Vector3[on.Count], Normal = new Vector3[on.Count] };
            var triIndex = new Dictionary<(int, int, int), int>();
            var toWorld = hm.transform.localToWorldMatrix;
            for (int k = 0; k < on.Count; k++)
            {
                var e = on[k];
                var key = (e.i0, e.i1, e.i2);
                if (!triIndex.TryGetValue(key, out int t))
                {
                    t = s.Tris.Count; triIndex[key] = t;
                    s.Tris.Add(new Corner3
                    {
                        P0 = toWorld.MultiplyPoint3x4(verts[e.i0]), P1 = toWorld.MultiplyPoint3x4(verts[e.i1]), P2 = toWorld.MultiplyPoint3x4(verts[e.i2]),
                        C0 = colors[e.i0], C1 = colors[e.i1], C2 = colors[e.i2], UV0 = uvs[e.i0], UV1 = uvs[e.i1], UV2 = uvs[e.i2],
                    });
                }
                s.Verts[k] = e.vert; s.Tri[k] = t; s.Domain[k] = new Vector2(e.bary.x, e.bary.y);
                s.Base[k] = e.baseInSpace; s.Normal[k] = e.normalInSpace;
            }
            s.Buf = mesh.vertices;
            All.Add(s);
            Refresh(s, true);
            return s;
        }

        internal static void Remove(Sheet s) { if (s != null) All.Remove(s); }

        // Every frame (Plugin.LateUpdate): visible ground sheets follow the camera's tessellation.
        internal static void Update()
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var s = All[i];
                if (s == null || !s.Mesh || !s.Hm) { All.RemoveAt(i); continue; }
                if (s.R && !s.R.isVisible) continue;
                Refresh(s, false);
            }
        }

        private static void Refresh(Sheet s, bool force)
        {
            var cam = Utils.GetMainCamera();
            if (!cam) return;
            var camPos = cam.transform.position;
            bool tessOn = Shader.IsKeywordEnabled("TESSELATION_ON");
            if (!force && s.LastFrame == Time.frameCount) return;
            if (!force && tessOn == s.LastTess && (camPos - s.LastCam).sqrMagnitude < 1e-4f) return;
            s.LastCam = camPos; s.LastTess = tessOn; s.LastFrame = Time.frameCount;

            var mat = MaterialInstance(s.Hm);
            float tessAmount = mat && mat.HasProperty("_Tess") ? mat.GetFloat("_Tess") : 4f;
            float water = mat && mat.HasProperty("_WaterLevel") ? mat.GetFloat("_WaterLevel") : 30f;
            var mask = PaintMask(s.Hm);
            var hmLocal = s.Hm.transform.worldToLocalMatrix;
            var toSpace = s.Space ? s.Space.worldToLocalMatrix : Matrix4x4.identity;
            var upInSpace = toSpace.MultiplyVector(Vector3.up);   // the shader adds its offset along the heightmap's local y

            // Per ground triangle: the drawn points and their offsets.
            var perTri = new (float[] u, float[] v, float[] dy, int[] idx, int nIdx)[s.Tris.Count];
            for (int t = 0; t < s.Tris.Count; t++)
            {
                var c = s.Tris[t];
                if (tessOn)
                {
                    float f0 = Factor(c.P0, camPos), f1 = Factor(c.P1, camPos), f2 = Factor(c.P2, camPos);
                    float t0 = f0 * tessAmount, t1 = f1 * tessAmount, t2 = f2 * tessAmount;
                    tess.TessellateTri((t1 + t2) * 0.5f, (t0 + t2) * 0.5f, (t0 + t1) * 0.5f, (t0 + t1 + t2) * 0.333333f);
                }
                else tess.TessellateTri(1f, 1f, 1f, 1f);
                int n = tess.NumPoints;
                var u = new float[n]; var v = new float[n]; var dy = new float[n];
                for (int j = 0; j < n; j++)
                {
                    float a = tess.U[j], b = tess.V[j], w = 1f - a - b;
                    u[j] = a; v[j] = b;
                    var p = c.P1 * b + c.P0 * a + c.P2 * w;
                    var col = c.C1 * b + c.C0 * a + c.C2 * w;
                    var uv = c.UV1 * b + c.UV0 * a + c.UV2 * w;
                    dy[j] = Offset(p, hmLocal.MultiplyPoint3x4(p).y, col, uv, Sample(mask, uv), camPos, water, tessOn);
                }
                var idx = new int[tess.NumIndices];
                System.Array.Copy(tess.Index, idx, tess.NumIndices);
                perTri[t] = (u, v, dy, idx, tess.NumIndices);
            }

            for (int k = 0; k < s.Verts.Length; k++)
            {
                var d = perTri[s.Tri[k]];
                float off = Interpolate(d.u, d.v, d.dy, d.idx, d.nIdx, s.Domain[k]);
                s.Buf[s.Verts[k]] = s.Base[k] + upInSpace * off + s.Normal[k] * s.Lift;
            }
            s.Mesh.vertices = s.Buf;
            s.Mesh.RecalculateBounds();
        }

        // Hull shader, per corner: clamp(1 - (distance - 10) x 0.025, 0.01, 1).
        private static float Factor(Vector3 corner, Vector3 cam)
        {
            float d = Vector3.Distance(corner, cam);
            return Mathf.Min(Mathf.Max(-(d - 10f) * 0.025f + 1f, 0.01f), 1f);
        }

        // The paint mask (_ClearedMaskTex: r dirt, g cultivated, b paved, a vegetation) as the GPU reads it: an sRGB texture
        // (Texture2D(w, h), clamped), so colour channels are decoded before bilinear filtering, alpha is not.
        private static Color Sample(Texture2D mask, Vector2 uv)
        {
            if (!mask || !mask.isReadable) return new Color(0, 0, 0, 1);
            int w = mask.width, h = mask.height;
            float x = uv.x * w - 0.5f, y = uv.y * h - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            Color P(int i, int j) { var c = mask.GetPixel(Mathf.Clamp(i, 0, w - 1), Mathf.Clamp(j, 0, h - 1)); var l = c.linear; l.a = c.a; return l; }
            var a = Color.Lerp(P(x0, y0), P(x0 + 1, y0), fx);
            var b = Color.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx);
            return Color.Lerp(a, b, fy);
        }

        private static float Sat(float x) => Mathf.Clamp01(x);

        // The shader's vertical offset at one drawn point (domain shader with tessellation, vertex shader without).
        private static float Offset(Vector3 world, float localY, Color c, Vector2 uv, Color m, Vector3 cam, float water, bool tessOn)
        {
            float wMountain = Mathf.Max(1f - Mathf.Max(Mathf.Max(Mathf.Abs(c.r), Mathf.Abs(c.g - 1f)), Mathf.Max(Mathf.Abs(c.b), Mathf.Abs(c.a))), 0f);
            float wAsh = Mathf.Max(1f - Mathf.Max(Mathf.Max(Mathf.Abs(c.g), Mathf.Abs(c.r - 1f)), Mathf.Max(Mathf.Abs(c.b), Mathf.Abs(c.a - 1f))), 0f);
            float wMist = Mathf.Max(1f - Mathf.Max(Mathf.Max(Mathf.Abs(c.r), Mathf.Abs(c.g)), Mathf.Max(Mathf.Abs(c.b - 1f), Mathf.Abs(c.a - 1f))), 0f);
            float snow = wMountain * (1f - Sat(m.b * 10f + (m.r + m.g)));
            float dy = snow * 0.1f + wAsh * 0.1f + wAsh * m.a * 0.8f;
            if (tessOn)
            {
                bool border = uv.x < 0.001f || uv.y < 0.001f || uv.x > 0.999f || uv.y > 0.999f;
                float amp = (0.25f - 0.15f * Sat(m.b * 10f + m.r * 4f)) * (1f - wMist) * Mathf.Min(1f, 1f - snow);
                float bump = Mathf.Sin(1.4f * world.x) * Mathf.Cos(1.5f * world.z) * Mathf.Sin(2.235f * world.x) * Mathf.Cos(2.435f * world.z);
                if (!border) dy += bump * amp;
            }
            float far = Sat((new Vector2(world.x - cam.x, world.z - cam.z).magnitude - 100f) * 0.005f);
            far = far * far * (3f - 2f * far);
            dy += localY + dy > water ? far : -far;
            return dy;
        }

        // The offset at (u, v) inside the drawn triangles: the triangle containing it, linearly.
        private static float Interpolate(float[] u, float[] v, float[] dy, int[] idx, int n, Vector2 p)
        {
            float best = float.NegativeInfinity, value = 0f;
            for (int t = 0; t + 2 < n; t += 3)
            {
                int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                float x1 = u[a], y1 = v[a], x2 = u[b], y2 = v[b], x3 = u[c], y3 = v[c];
                float den = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3);
                if (Mathf.Abs(den) < 1e-12f) continue;
                float l1 = ((y2 - y3) * (p.x - x3) + (x3 - x2) * (p.y - y3)) / den;
                float l2 = ((y3 - y1) * (p.x - x3) + (x1 - x3) * (p.y - y3)) / den;
                float l3 = 1f - l1 - l2;
                float worst = Mathf.Min(l1, Mathf.Min(l2, l3));
                if (worst > best) { best = worst; value = l1 * dy[a] + l2 * dy[b] + l3 * dy[c]; if (worst >= 0f) return value; }
            }
            return value;
        }
    }
}
