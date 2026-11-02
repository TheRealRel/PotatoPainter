// An etched rune's light (0.4.3, the user: "the glow is not as good as the original glow we had many versions ago"): the
// original look was probe test 22's "rune light bright", a steady point light per glowing rune that lights the surface
// around it, so the rune reads as glowing with bloom on or off. Switched in the Etched panel ("Light", on by default) and
// kept per paint. Torches for comparison (the game's prefabs): range 15 m (wall torch 12), intensity 1.5, no shadows.
using UnityEngine;

namespace PotatoPainter
{
    internal static class RuneLight
    {
        // Probe test 22, "bright": range 7 x the rune's size (3.5 m for 0.5 m), intensity 1.2 at glow strength 1, 15 cm off
        // the surface. The game's LightLod fades it in and out over a second and keeps only the nearest lights up to the
        // Point lights graphics setting, ranking a light by its distance minus a quarter of its light distance (30 m here,
        // torches 80 m), so a rune light gives way to a torch at the same distance.
        internal const float RangePerSize = 7f, IntensityPerGlow = 1.2f, Off = 0.15f, LodDistance = 30f;

        // Rune lights with shadows: the player's setting "Rune light shadows" (off by default; tested in 0.4.7 to 0.4.9 with
        // a test key). The game's LightLod then gives shadows only to lights ranked under the Point light shadows graphics
        // setting (m_shadowLimit) and within its m_shadowDistance, fading them in and out over about a second
        // (assembly_valheim LightLod.Awake and UpdateLoop, read 2026-11-01; Awake drops the shadow LOD when the light has no
        // shadows, so lights are remade when the setting changes). 20 m is the torches' m_shadowDistance (research-2.md).
        internal static bool Shadows;
        internal const float ShadowDistance = 20f;

        // The light's colour: the glow's hue at full brightness. Its strength: the glow's strength (the brightest channel).
        internal static Color Hue(Color glow)
        {
            float top = glow.maxColorComponent;
            var c = top > 0f ? glow / top : Color.black; c.a = 1f;
            return c;
        }

        // Darker colours get a stronger light (1.0.0; the user, 2026-11-01: blue and red runes seemed to have no glow at full
        // strength, "Let's try and compensate for this"). First try: raised to pure green's luminance (0.2126 R + 0.7152 G +
        // 0.0722 B), at most 4x. The user's night video (20-19-30): "too much": the violet and magenta lights (4x, 2.5x)
        // lit the ground about five times as bright as the cyan runes' (1x), washing whole rocks purple even by day, while
        // red at 3.4x lit about as much as cyan at night. Saturated blue and violet look brighter on screen than their
        // luminance says, so the weights are dialled from that video: blue's raised until violet needs 1.25x and red's
        // until red needs 2.2x (in the game's linear colour space, as Unity lights use their colour). Boosts at full
        // saturation: red 2.2x, ember 1.95x, orange 1.5x, blue 1.4x, violet 1.25x; magenta, yellow, green, cyan and the
        // paler colours keep their light as it was. At most 2.5x.
        internal const float MaxBoost = 2.5f, WeightR = 0.325f, WeightG = 0.7152f, WeightB = 0.503f;

        private static float Brightness(Color c) { var l = c.linear; return WeightR * l.r + WeightG * l.g + WeightB * l.b; }

        internal static float Boost(Color hue) => Mathf.Clamp(WeightG / Mathf.Max(Brightness(hue), 1e-4f), 1f, MaxBoost);

        internal static float Intensity(Color glow) => IntensityPerGlow * glow.maxColorComponent * Boost(Hue(glow));

        // A placed paint's light, with the game's LightLod (added while inactive, as the probe did, so LightLod starts with
        // the light set up).
        internal static GameObject Make(Frame f, float size, Color glow, Transform parent, string name)
        {
            if (glow.maxColorComponent <= 0f) return null;
            var go = new GameObject(Trace.Prefix + name + " light");
            go.SetActive(false);
            if (parent) go.transform.SetParent(parent, false);
            go.transform.position = f.Center + f.Normal * Off;
            var l = go.AddComponent<Light>();
            Setup(l, size, glow);
            if (Shadows) { l.shadows = LightShadows.Soft; l.shadowStrength = 1f; }
            var lod = go.AddComponent<LightLod>();
            lod.m_lightLod = true; lod.m_lightDistance = LodDistance; lod.m_shadowLod = Shadows; lod.m_shadowDistance = ShadowDistance;
            go.SetActive(true);
            return go;
        }

        // A new glow strength for a light already made (the bloom setting changed): colour and intensity only; LightLod
        // fades a light by its range, which it read when the light was made (LightLod.m_baseRange).
        internal static void Tint(Light l, Color glow)
        {
            if (!l) return;
            l.color = Hue(glow);
            l.intensity = Intensity(glow);
        }

        internal static void Setup(Light l, float size, Color glow)
        {
            l.type = LightType.Point;
            l.color = Hue(glow);
            l.range = size * RangePerSize;
            l.intensity = Intensity(glow);
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
        }
    }
}
