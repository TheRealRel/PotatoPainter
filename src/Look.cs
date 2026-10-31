// How paint looks: a copy of the game's beech leaf material (Custom/Vegetation: cut-out, lit by the world's light and
// shadow, hidden by walls in front), with the rune as its texture and the paint's colour per paint. Found in probe
// tests 7 to 16 (claude/potatopainter-research.md).
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PotatoPainter
{
    internal static class Look
    {
        internal static readonly Color RedOchre = new Color(0.62f, 0.12f, 0.06f, 1f);
        // Glow colour and strength from probe test 22: ember holds its colour on screen up to strength 1 to 1.5 at night
        // (223,61,22 at 1); stronger turns white (ACES tonemapping). Halo levels: faint 0.4, medium 0.8, strong 1.6.
        internal static readonly Color Ember = new Color(1f, 0.267f, 0f, 1f);
        internal const float HaloFaint = 0.4f;
        private static Material halo; private static bool haloLooked;
        private static Material leaf;
        private static bool looked;
        private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        // The rune drawing as a texture: clamped, with mipmaps that keep the share of solid texels at the 0.5 cut-out,
        // so strokes don't thin out and break up at a distance (probe test 15).
        internal static Texture2D Texture(Rune rune)
        {
            if (textures.TryGetValue(rune.Art, out var t)) return t;
            var bytes = Runes.ArtBytes(rune);
            if (bytes == null) return null;
            t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "PotatoPainter " + rune.Art, wrapMode = TextureWrapMode.Clamp };
            ImageConversion.LoadImage(t, bytes);
            KeepCoverage(t, 0.5f);
            textures[rune.Art] = t;
            return t;
        }

        // Painted: the leaf look in the paint's own colour (it keeps the leaf's normal map and its snow and rain). Etched: the same, glowing ember at strength 1 like the runes on
        // the Rúnakefli (probe test 22: ember keeps its colour up to 1 to 1.5 at night, whiter above).
        internal static Material Material(Rune rune, bool etched)
        {
            var key = (etched ? "etched " : "") + rune.Art;
            if (materials.TryGetValue(key, out var m)) return m;
            var tex = Texture(rune);
            if (!Leaf() || !tex) return null;
            m = etched ? Paint(key, tex, RedOchre, Ember, 1f) : new Material(leaf) { name = "PotatoPainter " + key, mainTexture = tex };
            // Etched runes are lit evenly: no bumps from the beech leaf's own normal map (beech_leaf_n, laid over every
            // rune's square), which painted runes keep for a painted look (the user, 2026-10-30).
            if (etched && m && m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            materials[key] = m;
            return m;
        }

        // An etched rune's faint halo, in ember (the Rúnakefli's own halo level).
        internal static Material HaloFor(Rune rune)
        {
            var key = "halo " + rune.Art;
            if (materials.TryGetValue(key, out var m)) return m;
            var tex = Texture(rune);
            m = tex ? Halo(rune.Art, HaloTexture(tex), Ember * HaloFaint) : null;
            materials[key] = m;
            return m;
        }

        // The halo's texture as in probe test 22: the rune's alpha blurred (gaussian, sigma 7 px of 256, inside the
        // drawing's margin) x 1.6, with the strokes taken out so it adds nothing on top of them; white under the alpha.
        private static Texture2D HaloTexture(Texture2D rune)
        {
            int w = rune.width, h = rune.height;
            var src = rune.GetPixels32(0);
            var a = new float[w * h]; for (int i = 0; i < a.Length; i++) a[i] = src[i].a / 255f;
            float sigma = 7f * w / 256f; int rad = Mathf.CeilToInt(sigma * 3f);
            var k = new float[rad * 2 + 1]; float sum = 0f;
            for (int i = -rad; i <= rad; i++) { k[i + rad] = Mathf.Exp(-i * i / (2f * sigma * sigma)); sum += k[i + rad]; }
            for (int i = 0; i < k.Length; i++) k[i] /= sum;
            var tmp = new float[w * h]; var blur = new float[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { float v = 0f; for (int i = -rad; i <= rad; i++) { int xx = x + i; if (xx >= 0 && xx < w) v += a[y * w + xx] * k[i + rad]; } tmp[y * w + x] = v; }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { float v = 0f; for (int i = -rad; i <= rad; i++) { int yy = y + i; if (yy >= 0 && yy < h) v += tmp[yy * w + x] * k[i + rad]; } blur[y * w + x] = v; }
            var outp = new Color32[w * h];
            for (int i = 0; i < outp.Length; i++) outp[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(blur[i] * 1.6f) * (1f - a[i]) * 255f));
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PotatoPainter halo " + rune.name };
            t.SetPixels32(outp); t.Apply(true, false);
            return t;
        }

        // The paint look with any drawing and a fixed colour (the Rúnakefli's own runes), glowing when glow > 0: emission
        // like the Mistlands mushrooms, the drawing (_EmissiveTex) times _EmissionColor (probe tests 18 to 22).
        internal static Material Paint(string name, Texture2D tex, Color col, Color glowCol, float glow)
        {
            if (!Leaf() || !tex) return null;
            var m = new Material(leaf) { name = "PotatoPainter " + name, mainTexture = tex };
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (glow > 0f && m.HasProperty("_EmissionColor"))
            {
                if (m.HasProperty("_EmissiveTex")) m.SetTexture("_EmissiveTex", tex);
                m.SetColor("_EmissionColor", glowCol * glow);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return m;
        }

        // The halo around glowing strokes: a copy of the game's own additive "embers" particle material (shader Particles/
        // Standard Unlit2) with a blurred outline of the strokes as its texture, drawn on the same sheet. A material built
        // from that shader by hand drew opaque squares (probe test 21); the copy works (test 22). Colour = glow x level.
        internal static Material Halo(string name, Texture2D tex, Color col)
        {
            if (!haloLooked)
            {
                haloLooked = true;
                const string shader = "Particles/Standard Unlit2";
                bool Additive(Material x) => x && x.shader && x.shader.name == shader && x.HasProperty("_DstBlend") && x.GetFloat("_DstBlend") == 1f && x.HasProperty("_SrcBlend") && x.GetFloat("_SrcBlend") == 5f;
                var found = Resources.FindObjectsOfTypeAll<Material>().Where(Additive).ToList();
                halo = found.FirstOrDefault(x => x.name == "embers")
                    ?? found.FirstOrDefault(x => x.shaderKeywords.Length == 1 && x.shaderKeywords[0] == "_ALPHABLEND_ON" && x.GetFloat("_SoftParticlesEnabled") == 0f);
                if (!halo) Plugin.Log.LogWarning($"Glow halo off this session: none of the game's additive {shader} materials is loaded ({found.Count} found).");
            }
            if (!halo || !tex) return null;
            var m = new Material(halo) { name = "PotatoPainter halo " + name, mainTexture = tex };
            var c = col; c.a = 1f;
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        private static Material Leaf()
        {
            if (looked) return leaf;
            looked = true;
            var beech = Jotunn.Managers.PrefabManager.Instance.GetPrefab("Beech1");
            var source = beech ? beech.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(x => x && x.shader && x.shader.name == "Custom/Vegetation") : null;
            if (!source) { Plugin.Log.LogError("The beech's leaf material wasn't found; paint can't be drawn this session."); return null; }
            leaf = new Material(source) { name = "PotatoPainter paint" };
            if (leaf.HasProperty("_Color")) leaf.SetColor("_Color", RedOchre);
            if (leaf.HasProperty("_Cutoff")) leaf.SetFloat("_Cutoff", 0.5f);
            // No wind of its own: paint on a tree follows the bark (bent on the CPU, Bend.cs).
            foreach (var p in new[] { "_SwaySpeed", "_SwayDistance", "_RippleDistance", "_RippleSpeed", "_PushDistance" }) if (leaf.HasProperty(p)) leaf.SetFloat(p, 0f);
            leaf.DisableKeyword("_EMISSION");
            leaf.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            return leaf;
        }

        // One paint's colour (and an etched one's glow), per renderer, so each paint keeps its own.
        internal static void Colour(Renderer r, Color col, Color glow)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", new Color(col.r, col.g, col.b, 1f));
            if (glow.maxColorComponent > 0f) block.SetColor("_EmissionColor", new Color(glow.r, glow.g, glow.b, 1f));
            r.SetPropertyBlock(block);
        }

        // An etched rune's halo: its glow at the faint halo level.
        internal static void HaloColour(Renderer r, Color glow)
        {
            var block = new MaterialPropertyBlock();
            var c = glow * HaloFaint; c.a = 1f;
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }

        // Mipmaps made by averaging lose alpha coverage: each smaller level's alpha is scaled so the share of texels at or
        // above the cut-out matches the full-size image.
        internal static void KeepCoverage(Texture2D t, float cutoff)
        {
            var top = t.GetPixels(0);
            float Cover(Color[] px, float k) { int n = 0; foreach (var c in px) if (c.a * k >= cutoff) n++; return (float)n / px.Length; }
            float target = Cover(top, 1f);
            for (int mip = 1; mip < t.mipmapCount; mip++)
            {
                var px = t.GetPixels(mip);
                float lo = 0.25f, hi = 8f;
                for (int it = 0; it < 24; it++) { float mid = (lo + hi) * 0.5f; if (Cover(px, mid) < target) lo = mid; else hi = mid; }
                for (int i = 0; i < px.Length; i++) px[i].a = Mathf.Clamp01(px[i].a * hi);
                t.SetPixels(px, mip);
            }
            t.Apply(false);
        }

        // The build menu icon: the rune in red ochre (brightened to read on the dark menu), 64 px. Etched: in ember, with
        // a faint glow around the strokes, so the two tabs tell apart at a glance.
        internal static Sprite Icon(Rune rune, bool etched)
        {
            var bytes = Runes.ArtBytes(rune);
            if (bytes == null) return null;
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(src, bytes);
            const int size = 64;
            var icon = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "PotatoPainter icon " + rune.Art };
            var tint = etched ? new Color(1f, 0.55f, 0.2f) : new Color(0.86f, 0.34f, 0.18f);
            var a = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    a[y * size + x] = src.GetPixelBilinear((x + 0.5f) / size, (y + 0.5f) / size).a;
            // Etched glow: the strokes' alpha spread 2 px, at a third, under the strokes.
            var glow = new float[size * size];
            if (etched)
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float v = 0f, n = 0f;
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                int xx = x + dx, yy = y + dy; float wgt = 1f / (1f + dx * dx + dy * dy); n += wgt;
                                if (xx >= 0 && xx < size && yy >= 0 && yy < size) v += a[yy * size + xx] * wgt;
                            }
                        glow[y * size + x] = Mathf.Clamp01(v / n * 1.5f) * 0.35f;
                    }
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(tint.r, tint.g, tint.b, Mathf.Max(a[i], glow[i]));
            icon.SetPixels(px); icon.Apply(false);
            Object.Destroy(src);
            return Sprite.Create(icon, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
