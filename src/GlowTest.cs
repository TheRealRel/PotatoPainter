// Test-only (0.4.3, the user: "Bloom also effects the glow drastically. It triggers bloom pretty bad and looks flat when
// it's off."): reads the finished screen while an etched rune glows in a set of colours at every strength, with its light
// off and on, so each colour can get a glow cap where it stops clipping (screenshots of the hand had half of the bright
// pixels at 250+ in a channel, even with bloom off). Starts on every etched rune placed by this player; the camera and the
// rune stay put for about half a minute. Same screen reading as probe tests 19 to 22. Take out before release.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PotatoPainter
{
    internal static class GlowTest
    {
        private static readonly (string name, Color hue)[] Colours =
        {
            ("ember", Look.Ember), ("red", new Color(1f, 0f, 0f)), ("orange", new Color(1f, 0.5f, 0f)), ("yellow", new Color(1f, 1f, 0f)),
            ("green", new Color(0f, 1f, 0f)), ("cyan", new Color(0f, 1f, 1f)), ("blue", new Color(0f, 0f, 1f)), ("violet", new Color(0.5f, 0f, 1f)),
            ("magenta", new Color(1f, 0f, 1f)), ("white", new Color(1f, 1f, 1f)),
        };
        private static readonly float[] Strengths = { 0f, 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };
        private static bool running;

        internal static void Run(Paint p)
        {
            if (running || !Plugin.Instance || p == null) return;
            Plugin.Instance.StartCoroutine(Sweep(p));
        }

        private static IEnumerator Sweep(Paint p)
        {
            running = true;
            Paint.HidePreview = true;
            GameObject testLight = null;
            var player = Player.m_localPlayer;
            try
            {
                var colours = new List<(string, Color)> { ("yours", RuneLight.Hue(p.Glow)) };
                colours.AddRange(Colours);
                int shots = 1 + 2 * colours.Count * Strengths.Length;
                player?.Message(MessageHud.MessageType.TopLeft, $"Glow test: hold still, about {Mathf.CeilToInt(shots * 0.15f + 2f)} s");
                // Its own light fades in over a second (LightLod); the preview goes away.
                yield return new WaitForSeconds(1.5f);
                if (!Alive(p)) { Plugin.Log.LogInfo("Glow test stopped: the rune is gone"); yield break; }
                var cam = Utils.GetMainCamera();
                Plugin.Log.LogInfo($"Glow test start: {p.Rune.Art}, size {p.Size:0.00} m, {(cam ? Vector3.Distance(cam.transform.position, p.Frame.Center).ToString("0.0") : "?")} m from the camera, glow {p.Glow} (strength {p.Glow.maxColorComponent:0.00}), surface {p.Col}, light {(p.Lit ? "on" : "off")}; {shots} screens; game point light limit {LightLod.m_lightLimit}; {LightNow()}; {CameraNow()}");
                yield return Shot(p, "as placed", null);

                testLight = new GameObject(Trace.Prefix + "glow test light");
                testLight.transform.position = p.Frame.Center + p.Frame.Normal * RuneLight.Off;
                var light = testLight.AddComponent<Light>();
                light.enabled = false;
                foreach (bool lit in new[] { false, true })
                {
                    Plugin.Log.LogInfo($"Glow test, rune light {(lit ? $"on (range {RuneLight.RangePerSize} x size, intensity {RuneLight.IntensityPerGlow} x strength)" : "off")}; {LightNow()}");
                    foreach (var (name, hue) in colours)
                        foreach (float k in Strengths)
                        {
                            if (!Alive(p)) { Plugin.Log.LogInfo("Glow test stopped: the rune is gone"); yield break; }
                            var glow = hue * k; glow.a = 1f;
                            if (p.Drawn?.Light) p.Drawn.Light.SetActive(false);
                            foreach (var r in p.Drawn.Strokes) if (r) Block(r, hue * Settings.EtchedSurface, glow);
                            foreach (var h in p.Drawn.Halos) if (h) Look.HaloColour(h, glow);
                            light.enabled = lit && k > 0f;
                            if (light.enabled) RuneLight.Setup(light, p.Size, glow);
                            yield return Shot(p, $"light {(lit ? "on" : "off")} {name} x{k:0.00} (emission {glow})", light.enabled ? light : null);
                        }
                }
                Plugin.Log.LogInfo($"Glow test done; {LightNow()}");
                player?.Message(MessageHud.MessageType.TopLeft, "Glow test done");
            }
            finally
            {
                if (testLight) UnityEngine.Object.Destroy(testLight);
                if (Alive(p))
                {
                    foreach (var r in p.Drawn.Strokes) if (r) Look.Colour(r, p.Col, p.Glow);
                    foreach (var h in p.Drawn.Halos) if (h) Look.HaloColour(h, p.Glow);
                    if (p.Drawn.Light) p.Drawn.Light.SetActive(true);
                }
                Paint.HidePreview = false;
                running = false;
            }
        }

        private static bool Alive(Paint p) => p && p.HasData && p.Drawn != null;

        // Every stroke's colour and glow, the glow set even when black (Look.Colour leaves the material's own then).
        private static void Block(Renderer r, Color col, Color glow)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", new Color(col.r, col.g, col.b, 1f));
            block.SetColor("_EmissionColor", new Color(glow.r, glow.g, glow.b, 1f));
            r.SetPropertyBlock(block);
        }

        private static IEnumerator Shot(Paint p, string what, Light light)
        {
            yield return null; yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                Measure(shot, p, what, light);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Glow test screen reading failed: " + e); }
            finally { if (shot) UnityEngine.Object.Destroy(shot); }
        }

        private sealed class Tally
        {
            private readonly HashSet<int> seen = new HashSet<int>();
            private int n, maxed, white; private double r, g, b, top, luma, sat, hx, hy;
            internal void Add(int key, Color c)
            {
                if (!seen.Add(key)) return;
                n++; r += c.r; g += c.g; b += c.b;
                float hi = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), lo = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                top += hi; luma += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                if (hi >= 250f / 255f) maxed++;
                if (lo >= 250f / 255f) white++;
                Color.RGBToHSV(c, out float h, out float s, out _);
                sat += s; hx += s * Math.Cos(h * 2 * Math.PI); hy += s * Math.Sin(h * 2 * Math.PI);
            }
            public override string ToString()
            {
                if (n == 0) return "no points seen";
                double hue = (Math.Atan2(hy, hx) * 180 / Math.PI + 360) % 360;
                return $"n {n} RGB ({r / n * 255:0},{g / n * 255:0},{b / n * 255:0}) top {top / n * 255:0} luma {luma / n * 255:0} hue {hue:0} sat {sat / n:0.00}, {100.0 * maxed / n:0}% at 250+, {100.0 * white / n:0}% white";
            }
        }

        private static readonly Vector3[] Bary = { new Vector3(1f / 3, 1f / 3, 1f / 3), new Vector3(2f / 3, 1f / 6, 1f / 6), new Vector3(1f / 6, 2f / 3, 1f / 6), new Vector3(1f / 6, 1f / 6, 2f / 3) };

        // Strokes: points of the drawn sheets inside the drawing (alpha 0.98+). Through: points where it is cut out (0.02 or
        // less: the surface, the halo and the light on it). Around: the surface just outside the rune (0.75 to 1.5 x its size
        // from the centre, on its plane), where only the light reaches. Points behind something, near the crosshair (90 px) or
        // off screen are skipped.
        private static void Measure(Texture2D shot, Paint p, string what, Light light)
        {
            var cam = Utils.GetMainCamera();
            var tex = Look.Texture(p.Rune);
            if (!cam || !tex || !Alive(p)) return;
            Vector3 eye = cam.transform.position;
            float sx = shot.width / (float)Screen.width, sy = shot.height / (float)Screen.height;
            var strokes = new Tally(); var through = new Tally(); var around = new Tally();
            int hidden = 0, centre = 0, off = 0, sheets = 0;

            bool Pixel(Vector3 world, out int key, out Color c)
            {
                key = 0; c = default;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f || sp.x < 0 || sp.y < 0 || sp.x >= Screen.width || sp.y >= Screen.height) { off++; return false; }
                if (Mathf.Abs(sp.x - Screen.width * 0.5f) < 90f && Mathf.Abs(sp.y - Screen.height * 0.5f) < 90f) { centre++; return false; }
                Vector3 back = world + (eye - world).normalized * 0.02f;
                if (Physics.Linecast(eye, back, out _, ~0, QueryTriggerInteraction.Ignore)) { hidden++; return false; }
                int px = Mathf.Clamp((int)(sp.x * sx), 0, shot.width - 1), py = Mathf.Clamp((int)(sp.y * sy), 0, shot.height - 1);
                key = py * shot.width + px; c = shot.GetPixel(px, py);
                return true;
            }

            foreach (var r in p.Drawn.Strokes)
            {
                if (!r || !r.isVisible || !r.gameObject.activeInHierarchy) continue;
                var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null;
                if (!mesh || !mesh.isReadable) continue;
                sheets++;
                var v = mesh.vertices; var uv = mesh.uv; var tri = mesh.triangles; var tr = r.transform;
                for (int t = 0; t + 2 < tri.Length; t += 3)
                    foreach (var b in Bary)
                    {
                        int i0 = tri[t], i1 = tri[t + 1], i2 = tri[t + 2];
                        Vector2 u = uv[i0] * b.x + uv[i1] * b.y + uv[i2] * b.z;
                        float a = tex.GetPixelBilinear(u.x, u.y).a;
                        bool inStroke = a >= 0.98f, cut = a <= 0.02f;
                        if (!inStroke && !cut) continue;
                        if (Pixel(tr.TransformPoint(v[i0] * b.x + v[i1] * b.y + v[i2] * b.z), out int key, out var c)) (inStroke ? strokes : through).Add(key, c);
                    }
            }
            var f = p.Frame; var side = Vector3.Cross(f.Normal, f.Up);
            foreach (float ring in new[] { 0.75f, 1f, 1.25f, 1.5f })
                for (int i = 0; i < 48; i++)
                {
                    float ang = i * Mathf.PI * 2f / 48f;
                    var at = f.Center + (side * Mathf.Cos(ang) + f.Up * Mathf.Sin(ang)) * (ring * p.Size) + f.Normal * 0.004f;
                    if (Pixel(at, out int key, out var c)) around.Add(key, c);
                }
            Plugin.Log.LogInfo($"Glow test {what}: {sheets} sheets drawn{(light ? $", light {light.color} range {light.range:0.00} intensity {light.intensity:0.00}" : "")}; strokes: {strokes}; through: {through}; around: {around}; skipped {hidden} behind something, {centre} at the crosshair, {off} off screen");
        }

        private static string LightNow()
        {
            try
            {
                var env = EnvMan.instance;
                var sun = env ? env.m_dirLight : null;
                return $"day fraction {(env ? env.GetDayFraction() : -1f):0.000}, daylight {EnvMan.IsDaylight()}, weather {(env ? env.GetCurrentEnvironment()?.m_name : "?")}, sun {(sun ? $"intensity {sun.intensity:0.00} colour {sun.color} enabled {sun.isActiveAndEnabled}" : "none")}, ambient {RenderSettings.ambientMode} {RenderSettings.ambientLight} intensity {RenderSettings.ambientIntensity:0.00}";
            }
            catch (Exception e) { return "light: failed " + e.Message; }
        }

        private static string CameraNow()
        {
            try
            {
                var cam = Camera.main;
                if (!cam) return "no camera";
                string s = $"camera HDR {cam.allowHDR}, path {cam.actualRenderingPath}, colour space {QualitySettings.activeColorSpace}, screen {Screen.width}x{Screen.height}";
                var pp = cam.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>();
                if (pp && pp.profile)
                {
                    var pr = pp.profile; var cg = pr.colorGrading.settings; var bl = pr.bloom.settings.bloom;
                    s += $", post-processing {(pp.enabled ? "on" : "off")}: colour grading {pr.colorGrading.enabled} (tonemapper {cg.tonemapping.tonemapper}, post exposure {cg.basic.postExposure}, contrast {cg.basic.contrast}, saturation {cg.basic.saturation}), bloom {pr.bloom.enabled} (intensity {bl.intensity}, threshold {bl.threshold}, soft knee {bl.softKnee}), eye adaptation {pr.eyeAdaptation.enabled}";
                }
                else s += ", no post-processing";
                return s;
            }
            catch (Exception e) { return "camera: failed " + e.Message; }
        }
    }
}
