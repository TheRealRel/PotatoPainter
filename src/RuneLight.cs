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

        // The light's colour: the glow's hue at full brightness. Its strength: the glow's strength (the brightest channel).
        internal static Color Hue(Color glow)
        {
            float top = glow.maxColorComponent;
            var c = top > 0f ? glow / top : Color.black; c.a = 1f;
            return c;
        }

        internal static float Intensity(Color glow) => IntensityPerGlow * glow.maxColorComponent;

        // A placed paint's light, with the game's LightLod (added while inactive, as the probe did, so LightLod starts with
        // the light set up).
        internal static GameObject Make(Frame f, float size, Color glow, Transform parent, string name)
        {
            if (glow.maxColorComponent <= 0f) return null;
            var go = new GameObject(Trace.Prefix + name + " light");
            go.SetActive(false);
            if (parent) go.transform.SetParent(parent, false);
            go.transform.position = f.Center + f.Normal * Off;
            Setup(go.AddComponent<Light>(), size, glow);
            var lod = go.AddComponent<LightLod>();
            lod.m_lightLod = true; lod.m_lightDistance = LodDistance; lod.m_shadowLod = false;
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
