// How strong an etched rune's glow may be before it goes flat on screen (0.4.4; the user picked "per colour and
// bloom-aware" on 2026-10-31). From 0.4.3's glow test (claude/potatopainter-glow-0.4.3.md): at night, with the rune light
// on, the strength at which each colour stops holding, either washing out to white (saturation below 0.5) or filling up
// to a flat block (more than half the stroke pixels at 250+ in a channel). Bloom lowers the limit, so the cap follows the
// bloom setting of whoever is looking (the game's PostProcessingBehaviour profile). By day in strong sun the limit is
// lower still (0.41 to 0.92 measured); the user accepted a little washing out there.
//
// Paints keep their glow as a share of the cap (the hue at full brightness times the share, ZDO PotatoPainter.Glow with
// PotatoPainter.GlowShare = 1), so every player sees each colour at its own best for their bloom setting. Paints etched
// before 0.4.4 keep an absolute strength; it is held to the cap.
using UnityEngine;

namespace PotatoPainter
{
    internal static class GlowCap
    {
        // Measured hues (HSV hue of the colour at full saturation): red, ember, orange, yellow (0.4.3's "yours", hue 0.162,
        // read 0.81 / 0.94, within 0.02 of yellow), green, cyan, blue, violet, magenta. Night, rune light on.
        private static readonly float[] Hue = { 0f, 0.0445f, 0.0833f, 0.1667f, 0.3333f, 0.5f, 0.6667f, 0.75f, 0.8333f };
        private static readonly float[] WithBloom = { 0.81f, 0.76f, 0.83f, 0.79f, 0.88f, 0.85f, 0.84f, 0.84f, 0.66f };
        private static readonly float[] WithoutBloom = { 0.90f, 0.89f, 0.91f, 0.92f, 1.04f, 0.98f, 0.91f, 0.92f, 0.86f };
        // White (no saturation) never washed out or filled a channel at any strength measured: at 1 its strokes read
        // 241 to 245 of 255 at night. Colours between white and full saturation are blended by saturation, a step the test
        // didn't measure; 0.4.4's glow test reads half-saturated colours to check it.
        private const float White = 1f;

        private static bool bloom; private static int frame = -1;

        // The bloom setting now (read once a frame): the main camera's post-processing profile.
        internal static bool Bloom
        {
            get
            {
                if (frame == Time.frameCount) return bloom;
                frame = Time.frameCount;
                bloom = false;
                try
                {
                    var cam = Utils.GetMainCamera();
                    var pp = cam ? cam.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>() : null;
                    bloom = pp && pp.enabled && pp.profile && pp.profile.bloom.enabled;
                }
                catch { bloom = false; }
                return bloom;
            }
        }

        // The cap for a glow colour (any brightness; only its hue and saturation count).
        internal static float Cap(Color glow, bool withBloom)
        {
            Color.RGBToHSV(glow, out float h, out float s, out _);
            var caps = withBloom ? WithBloom : WithoutBloom;
            float full;
            int n = Hue.Length;
            if (h >= Hue[n - 1]) full = Mathf.Lerp(caps[n - 1], caps[0], (h - Hue[n - 1]) / (1f - Hue[n - 1]));
            else
            {
                int i = 0;
                while (i < n - 2 && h >= Hue[i + 1]) i++;
                full = Mathf.Lerp(caps[i], caps[i + 1], (h - Hue[i]) / (Hue[i + 1] - Hue[i]));
            }
            return Mathf.Lerp(White, full, s);
        }

        // What a paint's kept glow becomes on screen: a share of the cap (0.4.4 on), or an older absolute strength held
        // to the cap.
        internal static Color Emission(Color kept, bool share) => Emission(kept, share, Bloom);

        internal static Color Emission(Color kept, bool share, bool withBloom)
        {
            float top = kept.maxColorComponent;
            if (top <= 0f) return Color.black;
            var hue = kept / top;
            float cap = Cap(hue, withBloom);
            float strength = share ? Mathf.Clamp01(top) * cap : Mathf.Min(top, cap);
            var c = hue * strength; c.a = 1f;
            return c;
        }

        // When the bloom setting changes, every etched paint loaded takes its new strength.
        private static bool? lastBloom;
        internal static void Update()
        {
            if (!Player.m_localPlayer) return;
            bool now = Bloom;
            if (lastBloom == now) return;
            bool first = lastBloom == null;
            lastBloom = now;
            if (first) return;
            int n = 0;
            foreach (var p in Paint.Loaded) if (p && p.HasData && p.Etched && p.Retint()) n++;
            // Test data (0.4.4): taken out before release.
            Plugin.Log.LogInfo($"Bloom {(now ? "on" : "off")}: glow of {n} etched runes set to the {(now ? "bloom" : "no-bloom")} caps.");
        }
    }
}
