// Test-only (0.4.3 to 0.4.5; the user: "Bloom also effects the glow drastically. It triggers bloom pretty bad and looks
// flat when it's off."): reads the finished screen while an etched rune glows in a set of colours at many strengths, so
// each colour gets a glow cap where it stops holding. 0.4.4 checked the caps (GlowCap) at 25% to 150% of the cap; the
// steps were too coarse to set caps from (claude/potatopainter-glow-0.4.4.md). 0.4.5 (the user, 2026-10-31: "Fine-step
// test" and "Lower the cap in sunlight"): every hue at full and half saturation, in 5% steps of the cap around where it
// goes flat (from 70% at night, 50% by day; down while flat, up until two flat readings in a row, at most 160%), each
// colour first with no glow (the sunlit surface alone), plus the light reaching the rune (sun direction, sunlit rays,
// sun and ambient), the inputs a daytime cap would use. 0.4.6 (the user: "Surface and glow dim in sun"; caps now from
// 0.4.5's night readings): at night each colour at 95/100/105% of the new cap; by day, for each colour, the most the
// surface colour and the glow can both be multiplied by (f) and still hold, with the glow at its cap and with none.
// Starts on every etched rune placed by this player; the camera and the rune stay put for about a minute. Same screen
// reading as probe tests 19 to 22. Take out before release.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PotatoPainter
{
    internal static class GlowTest
    {
        // Full saturation (as 0.4.3 and 0.4.4), then each hue at half saturation (HSV saturation 0.5, full value).
        private static readonly (string name, float h, Color hue)[] Full =
        {
            ("red", 0f, new Color(1f, 0f, 0f)), ("ember", 0.0445f, Look.Ember), ("orange", 0.0833f, new Color(1f, 0.5f, 0f)),
            ("yellow", 0.1667f, new Color(1f, 1f, 0f)), ("green", 0.3333f, new Color(0f, 1f, 0f)), ("cyan", 0.5f, new Color(0f, 1f, 1f)),
            ("blue", 0.6667f, new Color(0f, 0f, 1f)), ("violet", 0.75f, new Color(0.5f, 0f, 1f)), ("magenta", 0.8333f, new Color(1f, 0f, 1f)),
        };
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
                var colours = new List<(string name, Color hue, bool half)> { ("yours", RuneLight.Hue(p.Glow), false) };
                foreach (var c in Full) colours.Add((c.name, c.hue, false));
                foreach (var c in Full) { var h = Color.HSVToRGB(c.h, 0.5f, 1f); h.a = 1f; colours.Add((c.name + " half", h, true)); }
                player?.Message(MessageHud.MessageType.TopLeft, "Glow test: hold still, about a minute");
                // Its own light fades in over a second (LightLod); the preview goes away.
                yield return new WaitForSeconds(1.5f);
                if (!Alive(p)) { Plugin.Log.LogInfo("Glow test stopped: the rune is gone"); yield break; }
                var cam = Utils.GetMainCamera();
                Plugin.Log.LogInfo($"Glow test start: {p.Rune.Art}, size {p.Size:0.00} m, {(cam ? Vector3.Distance(cam.transform.position, p.Frame.Center).ToString("0.0") : "?")} m from the camera, kept glow {p.Glow} ({(p.GlowShare ? "share of the cap" : "strength")} {p.Glow.maxColorComponent:0.00}), drawn {p.Emission}, surface {p.Col}, light {(p.Lit ? "on" : "off")}; {colours.Count} colours; game point light limit {LightLod.m_lightLimit}; {LightNow()}; {CameraNow()}");
                Plugin.Log.LogInfo($"Glow test {LightAt(p)}");
                var read = new Reading();
                yield return Shot(p, "as placed", null, read);

                testLight = new GameObject(Trace.Prefix + "glow test light");
                testLight.transform.position = p.Frame.Center + p.Frame.Normal * RuneLight.Off;
                var light = testLight.AddComponent<Light>();
                light.enabled = false;
                bool bloom = GlowCap.Bloom, day = EnvMan.IsDaylight();
                int shots = 1;
                Plugin.Log.LogInfo($"Glow test, caps for bloom {(bloom ? "on" : "off")}, rune light on (range {RuneLight.RangePerSize} x size, intensity {RuneLight.IntensityPerGlow} x strength), {(day ? "day: dimming search" : "night: caps check")}; {LightNow()}");
                foreach (var (name, hue, half) in colours)
                {
                    float cap = GlowCap.Cap(hue, bloom), sat = half ? 0.25f : 0.5f;
                    // One reading: glow at a share of the cap, then surface colour and glow (and so the rune light) both times
                    // f, the way 0.4.7 would dim a sunlit rune.
                    IEnumerator At(string what, float share, float f)
                    {
                        var glow = hue * (share * cap * f); glow.a = 1f;
                        if (p.Drawn?.Light) p.Drawn.Light.SetActive(false);
                        foreach (var r in p.Drawn.Strokes) if (r) Block(r, hue * (Settings.EtchedSurface * f), glow);
                        foreach (var h in p.Drawn.Halos) if (h) Look.HaloColour(h, glow);
                        light.enabled = glow.maxColorComponent > 0f;
                        RuneLight.Setup(light, p.Size, glow);
                        shots++;
                        yield return Shot(p, $"{what} {name} {share * 100f:0}% of {cap:0.00} f {f:0.000} = x{share * cap * f:0.00} (emission {glow})", glow.maxColorComponent > 0f ? light : null, read);
                    }
                    if (!Alive(p)) { Plugin.Log.LogInfo("Glow test stopped: the rune is gone"); yield break; }
                    if (!day)
                    {
                        // Night: the new caps at 95, 100 and 105%.
                        foreach (float k in new[] { 0.95f, 1f, 1.05f }) yield return At("check", k, 1f);
                        continue;
                    }
                    // Day: the most f that still holds, with the glow at its cap and with no glow (the surface alone). Halving
                    // the range six times: within 1/64.
                    foreach (float share in new[] { 1f, 0f })
                    {
                        yield return At("dim", share, 1f);
                        if (read.Strokes == null) continue;
                        float lo = 0f, hi = 1f;
                        if (!read.Strokes.Flat(sat)) lo = 1f;
                        else
                            for (int i = 0; i < 6; i++)
                            {
                                if (!Alive(p)) { Plugin.Log.LogInfo("Glow test stopped: the rune is gone"); yield break; }
                                float mid = (lo + hi) * 0.5f;
                                yield return At("dim", share, mid);
                                if (read.Strokes == null) break;
                                if (read.Strokes.Flat(sat)) hi = mid; else lo = mid;
                            }
                        Plugin.Log.LogInfo($"Glow test holds {name} {share * 100f:0}% of {cap:0.00}: up to f {lo:0.000}");
                    }
                }
                Plugin.Log.LogInfo($"Glow test {LightAt(p)}");
                Plugin.Log.LogInfo($"Glow test screens: {shots}");
                Plugin.Log.LogInfo($"Glow test done; {LightNow()}");
                player?.Message(MessageHud.MessageType.TopLeft, "Glow test done");
            }
            finally
            {
                if (testLight) UnityEngine.Object.Destroy(testLight);
                if (Alive(p))
                {
                    foreach (var r in p.Drawn.Strokes) if (r) Look.Colour(r, p.Col, p.Emission);
                    foreach (var h in p.Drawn.Halos) if (h) Look.HaloColour(h, p.Emission);
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

        private sealed class Reading { internal Tally Strokes; }

        private static IEnumerator Shot(Paint p, string what, Light light, Reading read)
        {
            read.Strokes = null;
            yield return null; yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                read.Strokes = Measure(shot, p, what, light);
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
            // The parser's rule (claude/tools/glowparse.py): more than half the points at 250+ in a channel, or washed out
            // (saturation under the limit) once the strokes are bright (top channel 100+; dim strokes read grey at night).
            internal bool Flat(float satLimit) => n > 0 && (100.0 * maxed / n > 50.0 || (top / n * 255 >= 100.0 && sat / n < satLimit));

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
        private static Tally Measure(Texture2D shot, Paint p, string what, Light light)
        {
            var cam = Utils.GetMainCamera();
            var tex = Look.Texture(p.Rune);
            if (!cam || !tex || !Alive(p)) return null;
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
            return strokes;
        }

        // The light reaching the rune, from what the game sets each frame (EnvMan.SetEnv, read 2026-11-01): the sun or moon is
        // EnvMan.m_dirLight, shining along its forward axis with colour x intensity (the game's _SunColor), and the ambient is
        // RenderSettings.ambientLight in Flat mode (the game's _AmbientColor). Sunlit: five rays toward the sun from the
        // centre and four points 0.35 x size out, 5 cm off the surface, 1000 m long. Incoming = sun x facing x sunlit share +
        // ambient; "lit surface" = the rune's own surface colour times that (what the sun adds under the glow).
        private static string LightAt(Paint p)
        {
            try
            {
                var env = EnvMan.instance;
                var sun = env ? env.m_dirLight : null;
                if (!sun) return "light at the rune: no sun light";
                var f = p.Frame; var side = Vector3.Cross(f.Normal, f.Up);
                Vector3 toSun = -sun.transform.forward;
                float facing = Vector3.Dot(f.Normal, toSun);
                int lit = 0, rays = 0; string blocked = "nothing";
                foreach (var o in new[] { Vector2.zero, new Vector2(0.35f, 0f), new Vector2(-0.35f, 0f), new Vector2(0f, 0.35f), new Vector2(0f, -0.35f) })
                {
                    rays++;
                    if (facing <= 0f) continue;
                    var from = f.Center + (side * o.x + f.Up * o.y) * p.Size + f.Normal * 0.05f;
                    if (Physics.Raycast(from, toSun, out var hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (blocked == "nothing") blocked = $"{hit.collider.name} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}) at {hit.distance:0.0} m";
                    }
                    else lit++;
                }
                float share = facing > 0f ? lit / (float)rays : 0f;
                Color sunCol = sun.color * sun.intensity, amb = RenderSettings.ambientLight;
                Color inc = sunCol * (Mathf.Max(0f, facing) * share) + amb; inc.a = 1f;
                var est = new Color(p.Col.r * inc.r, p.Col.g * inc.g, p.Col.b * inc.b, 1f);
                return $"light at the rune: to the sun {toSun} (elevation {Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg:0} deg), facing {facing:0.00}, sunlit {(facing > 0f ? lit : 0)} of {rays} rays (first blocked by {blocked}), sun {sun.color} x {sun.intensity:0.00} (shadows {sun.shadows}, strength {sun.shadowStrength:0.00}), ambient {amb}, incoming {inc}, rune surface {p.Col}, lit surface {est} (top {est.maxColorComponent:0.000})";
            }
            catch (Exception e) { return "light at the rune: failed " + e.Message; }
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
