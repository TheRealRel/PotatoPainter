// Etched runes dim in sunlight (0.4.9; the user, 2026-10-31: "Surface and glow dim in sun", and 2026-11-01: "Glow-limit
// model + check"). 0.4.5 showed the sunlit surface colour of an etched rune washes out by itself in direct sun, so the glow
// cap alone can't keep it from going flat; 0.4.8's test measured, at midday (sun x1.70), the most the surface colour and
// the glow can both be multiplied by and still hold (claude/potatopainter-glow-0.4.8-run.md): with the glow at its cap and
// with none, sunlit and in shade, bloom on and off, every hue at full and half saturation (the lowest reading of each).
// The angle to the sun hardly mattered once sunlit; what counts is whether the sun reaches the rune and how strong it is.
//
// Between the measured points: 1/f (the multiplier's inverse, which grows with light added on top of a fixed limit) is
// blended linearly: by the glow's share of its cap between no glow and full glow, by the day's ambient light between
// night (1, no dimming: the caps are night limits) and shade at midday, and by the sun reaching the rune (sunlit share of
// five rays x sun strength / 1.70) between shade and sunlit. Reproduces every midday reading; 0.4.9's morning check held
// (and dimmed a little more than needed). Moonlight isn't counted (no moonlit rune was measured; the caps are night limits).
// The glow's multiplier is f (its cap carries the 15% margin). The surface colour isn't dimmed (0.4.13; the user, 2026-11-01:
// "our daylight dimming is going overboard", chose "Paint true, glow dims"): it looked dull in sunlight, and the game
// dims nothing of its own in daylight.
using UnityEngine;

namespace PotatoPainter
{
    internal static class SunDim
    {
        private static readonly float[] Hue = { 0f, 0.0445f, 0.0833f, 0.1667f, 0.3333f, 0.5f, 0.6667f, 0.75f, 0.8333f };
        // Red, ember, orange, yellow, green, cyan, blue, violet, magenta. [bloom on, bloom off][full, half].
        private static readonly float[][][] SunGlow =
        {
            new[] { new[] { 0.62f, 0.64f, 0.67f, 0.64f, 0.69f, 0.66f, 0.86f, 0.89f, 0.62f }, new[] { 0.67f, 0.75f, 0.73f, 0.70f, 0.70f, 0.61f, 0.69f, 0.69f, 0.67f } },
            new[] { new[] { 0.69f, 0.70f, 0.72f, 0.73f, 0.73f, 0.73f, 0.88f, 0.89f, 0.67f }, new[] { 0.73f, 0.83f, 0.83f, 0.77f, 0.73f, 0.66f, 0.77f, 0.70f, 0.70f } },
        };
        private static readonly float[][][] SunNoGlow =
        {
            new[] { new[] { 0.80f, 0.80f, 0.84f, 0.91f, 1f, 0.98f, 1f, 1f, 0.78f }, new[] { 0.86f, 0.91f, 1f, 1f, 1f, 0.83f, 1f, 1f, 0.84f } },
            new[] { new[] { 0.92f, 0.91f, 0.94f, 1f, 1f, 1f, 1f, 1f, 0.88f }, new[] { 0.97f, 1f, 1f, 1f, 1f, 0.97f, 1f, 1f, 0.91f } },
        };
        private static readonly float[][][] ShadeGlow =
        {
            new[] { new[] { 1f, 1f, 1f, 0.98f, 1f, 1f, 1f, 1f, 1f }, new[] { 1f, 0.97f, 0.97f, 0.95f, 1f, 1f, 1f, 1f, 1f } },
            new[] { new[] { 0.94f, 0.98f, 0.98f, 0.92f, 0.89f, 0.94f, 0.95f, 0.95f, 0.97f }, new[] { 1f, 0.92f, 0.92f, 0.89f, 0.91f, 0.89f, 1f, 0.88f, 0.98f } },
        };
        internal const float MeasuredSun = 1.70f, MinFactor = 0.15f, MaxSun = 1.5f;
        // The game's ambient (RenderSettings.ambientLight, Flat) at night and at midday in the runs, as luminance.
        private static readonly float NightAmbient = Lum(new Color(0.357f, 0.368f, 0.485f)), DayAmbient = Lum(new Color(0.463f, 0.574f, 0.706f));

        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        internal static float Factor(Color glow, float share, float day, float sun, bool bloom)
        {
            if (day <= 0f && sun <= 0f) return 1f;
            Color.RGBToHSV(glow, out float h, out float sat, out _);
            int b = bloom ? 0 : 1;
            float At(float[][][] t)
            {
                float full = AtHue(t[b][0], h), half = AtHue(t[b][1], h);
                // Below half saturation: the half readings (lighter colours weren't measured).
                return sat >= 0.5f ? Mathf.Lerp(half, full, (sat - 0.5f) * 2f) : half;
            }
            share = Mathf.Clamp01(share);
            float inv0 = 1f / At(SunNoGlow), inv1 = 1f / At(SunGlow), invShade1 = 1f / At(ShadeGlow);
            float invSun = inv0 + share * (inv1 - inv0), invShade = 1f + share * (invShade1 - 1f);
            float inv = 1f + day * (invShade - 1f) + sun * (invSun - invShade);
            return Mathf.Clamp(1f / Mathf.Max(inv, 1e-3f), MinFactor, 1f);
        }

        // 0 at night, 1 at the midday ambient measured (weather and time of day move it).
        internal static float Day()
        {
            float a = Lum(RenderSettings.ambientLight);
            return Mathf.Clamp01((a - NightAmbient) / (DayAmbient - NightAmbient));
        }

        // How much sun reaches the rune: share of five rays toward the sun that aren't blocked (the centre and four points
        // 0.35 x size out, 5 cm off the surface) x sun strength / 1.70. Nothing at night or when it faces away.
        internal static float Sun(Frame f, float size)
        {
            var env = EnvMan.instance;
            var light = env ? env.m_dirLight : null;
            if (!light || !light.isActiveAndEnabled || !EnvMan.IsDaylight()) return 0f;
            Vector3 toSun = -light.transform.forward;
            if (Vector3.Dot(f.Normal, toSun) <= 0f) return 0f;
            var side = Vector3.Cross(f.Normal, f.Up);
            int lit = 0;
            foreach (var o in Offsets)
            {
                var from = f.Center + (side * o.x + f.Up * o.y) * size + f.Normal * 0.05f;
                if (!Physics.Raycast(from, toSun, 1000f, ~0, QueryTriggerInteraction.Ignore)) lit++;
            }
            return Mathf.Min(MaxSun, lit / (float)Offsets.Length * light.intensity / MeasuredSun);
        }

        private static readonly Vector2[] Offsets = { Vector2.zero, new Vector2(0.35f, 0f), new Vector2(-0.35f, 0f), new Vector2(0f, 0.35f), new Vector2(0f, -0.35f) };

        private static float AtHue(float[] t, float h)
        {
            int n = Hue.Length;
            if (h >= Hue[n - 1]) return Mathf.Lerp(t[n - 1], t[0], (h - Hue[n - 1]) / (1f - Hue[n - 1]));
            int i = 0;
            while (i < n - 2 && h >= Hue[i + 1]) i++;
            return Mathf.Lerp(t[i], t[i + 1], (h - Hue[i]) / (Hue[i + 1] - Hue[i]));
        }
    }
}
