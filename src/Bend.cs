// Paint on a tree bends with the bark in the wind, on the CPU (probe test 14). The game bends only the bark's own
// vertices (Custom/Vegetation's DX11 vertex program) and draws flat triangles between them, so each paint point is placed
// between its three bent bark vertices with the same weights as at rest, plus the lift along the bent triangle's normal.
using System.Collections.Generic;
using UnityEngine;

namespace PotatoPainter
{
    internal static class Bend
    {
        internal sealed class Bender
        {
            internal Mesh Mesh; internal Renderer Host; internal Renderer Paint; internal Material HostMat;
            internal Vector3[] HostLocal; internal Vector2[] HostUV2; internal int[] Corners; internal Vector3[] Bary; internal bool[] Ok;
            internal float LiftLocal; internal Vector3[] Buf, Nrm; internal Vector3[] Bent; internal int[] BentFrame;
        }

        internal static readonly List<Bender> All = new List<Bender>();
        private static readonly int Wind1 = Shader.PropertyToID("_GlobalWind1"), Wind2 = Shader.PropertyToID("_GlobalWind2"), WindAlpha = Shader.PropertyToID("_GlobalWindAlpha");

        internal static bool Sways(Material m) => m && m.shader && m.shader.name == "Custom/Vegetation"
            && (m.GetFloat("_SwayDistance") != 0f || m.GetFloat("_RippleDistance") != 0f || m.GetFloat("_PushDistance") != 0f);

        // Custom/Vegetation, sway part: h = saturate(v.y / _Height) (or saturate(uv2.y) when _UV2Height); for each global
        // wind W (lerped by _GlobalWindAlpha from _GlobalWind1 to _GlobalWind2): s = W.w * 0.5 + 0.5, px = t s _SwaySpeed + objX,
        // pz = 0.61234 t s _SwaySpeed + objZ, sway = (WorldToObject W.xyz) _SwayDistance W.w h^2 (sin(pz) sin(px) + 0.5);
        // v' = normalize(v + sway) |v|. t = _Time.x.
        private static Vector3 Bent(Vector3 v, float uv2y, Material m, Matrix4x4 l2w, Matrix4x4 w2l, float t, Vector4 w1, Vector4 w2, float alpha)
        {
            float H = m.GetFloat("_Height"), speed = m.GetFloat("_SwaySpeed"), dist = m.GetFloat("_SwayDistance");
            bool useUV2 = m.HasProperty("_UV2Height") && m.GetFloat("_UV2Height") != 0f;
            float h = Mathf.Clamp01(useUV2 ? uv2y : (H != 0f ? v.y / H : 0f)); float h2 = h * h;
            float objX = l2w.m03, objZ = l2w.m23;
            Vector3 Block(Vector4 W)
            {
                float sp = (W.w * 0.5f + 0.5f) * speed;
                float px = t * sp + objX, pz = sp * t * 0.61234f + objZ;
                Vector3 wo = w2l.MultiplyVector(new Vector3(W.x, W.y, W.z));
                return wo * (dist * W.w * h2 * (Mathf.Sin(pz) * Mathf.Sin(px) + 0.5f));
            }
            Vector3 a = Block(w1), b = Block(w2);
            var sway = a + (b - a) * alpha;
            float len = v.magnitude;
            var moved = v + sway;
            return moved.sqrMagnitude > 1e-12f ? moved.normalized * len : v;
        }

        // Every frame, for the paint copies being drawn.
        internal static void Update()
        {
            if (All.Count == 0) return;
            float t = Time.timeSinceLevelLoad / 20f;
            var w1 = Shader.GetGlobalVector(Wind1); var w2 = Shader.GetGlobalVector(Wind2); float alpha = Shader.GetGlobalFloat(WindAlpha);
            int frame = Time.frameCount;
            All.RemoveAll(b => !b.Paint || !b.Host);
            foreach (var b in All)
            {
                if (!b.Paint.isVisible) continue;
                var l2w = b.Host.transform.localToWorldMatrix; var w2l = b.Host.transform.worldToLocalMatrix;
                Vector3 Corner(int idx)
                {
                    if (b.BentFrame[idx] != frame) { b.Bent[idx] = Bent(b.HostLocal[idx], b.HostUV2 != null ? b.HostUV2[idx].y : 0f, b.HostMat, l2w, w2l, t, w1, w2, alpha); b.BentFrame[idx] = frame; }
                    return b.Bent[idx];
                }
                for (int i = 0; i < b.Ok.Length; i++)
                {
                    if (!b.Ok[i]) continue;
                    Vector3 p0 = Corner(b.Corners[3 * i]), p1 = Corner(b.Corners[3 * i + 1]), p2 = Corner(b.Corners[3 * i + 2]);
                    var nrm = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                    if (Vector3.Dot(nrm, b.Nrm[i]) < 0f) nrm = -nrm;   // keep the side the paint was put on
                    b.Buf[i] = p0 * b.Bary[i].x + p1 * b.Bary[i].y + p2 * b.Bary[i].z + nrm * b.LiftLocal;
                }
                b.Mesh.vertices = b.Buf;
                b.Mesh.RecalculateBounds();
            }
        }
    }
}
