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
            var swayed = moved.sqrMagnitude > 1e-12f ? moved.normalized * len : v;
            return swayed + Flutter(v, h, useUV2, m, t, w1, w2, alpha);
        }

        // Custom/Vegetation, ripple part (read from the DX11 deferred vertex program, raw operand swizzles decoded,
        // 2026-10-30): added after the sway, in object space. Per global wind W: s = W.w * 0.5 + 0.5, ts = t s
        // _RippleSpeed, A = sin(0.61234 ts + 10x + 10y); offset = (A sin(ts + 10x + 10y) 0.1, sin(ts + 10z + 10x) 0.05,
        // A sin(ts + 10z + 10y) 0.1) x _RippleDistance x W.w x saturate((|v.xz| - _RippleDeadzoneMin) / (Max - Min)),
        // x h too when _UV2Height is on; lerped from wind 1 to wind 2 by _GlobalWindAlpha. Inside the deadzone (a fir's
        // trunk) nothing moves.
        private static Vector3 Flutter(Vector3 v, float h, bool useUV2, Material m, float t, Vector4 w1, Vector4 w2, float alpha)
        {
            float rd = m.HasProperty("_RippleDistance") ? m.GetFloat("_RippleDistance") : 0f;
            if (rd == 0f) return Vector3.zero;
            float w = Weight(v, m);
            if (w == 0f) return Vector3.zero;
            float rs = m.GetFloat("_RippleSpeed");
            Vector3 Block(Vector4 W)
            {
                float ts = t * (W.w * 0.5f + 0.5f) * rs;
                float A = Mathf.Sin(0.61234f * ts + 10f * v.x + 10f * v.y);
                float k = rd * w * W.w * (useUV2 ? h : 1f);
                return new Vector3(A * Mathf.Sin(ts + 10f * v.x + 10f * v.y) * 0.1f, Mathf.Sin(ts + 10f * v.z + 10f * v.x) * 0.05f, A * Mathf.Sin(ts + 10f * v.z + 10f * v.y) * 0.1f) * k;
            }
            Vector3 a = Block(w1), b = Block(w2);
            return a + (b - a) * alpha;
        }

        // How much of the ripple a vertex (object space) gets: 0 inside the deadzone, 1 outside it.
        internal static float Weight(Vector3 v, Material m)
        {
            if (!m || !m.HasProperty("_RippleDistance") || m.GetFloat("_RippleDistance") == 0f) return 0f;
            float min = m.GetFloat("_RippleDeadzoneMin"), max = m.GetFloat("_RippleDeadzoneMax");
            float r = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            return max > min ? Mathf.Clamp01((r - min) / (max - min)) : (r > min ? 1f : 0f);
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
