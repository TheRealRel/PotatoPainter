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

        internal static Material Material(Rune rune)
        {
            if (materials.TryGetValue(rune.Art, out var m)) return m;
            var tex = Texture(rune);
            if (!Leaf() || !tex) return null;
            m = new Material(leaf) { name = "PotatoPainter " + rune.Art, mainTexture = tex };
            materials[rune.Art] = m;
            return m;
        }

        // The paint look with any drawing and a fixed colour (the Rúnakefli's own runes).
        internal static Material Paint(string name, Texture2D tex, Color col)
        {
            if (!Leaf() || !tex) return null;
            var m = new Material(leaf) { name = "PotatoPainter " + name, mainTexture = tex };
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
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

        // One paint's colour, per renderer, so each paint keeps its own.
        internal static void Colour(Renderer r, Color col)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", new Color(col.r, col.g, col.b, 1f));
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

        // The build menu icon: the rune in red ochre (brightened to read on the dark menu), 64 px.
        internal static Sprite Icon(Rune rune)
        {
            var bytes = Runes.ArtBytes(rune);
            if (bytes == null) return null;
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(src, bytes);
            const int size = 64;
            var icon = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "PotatoPainter icon " + rune.Art };
            var tint = new Color(0.86f, 0.34f, 0.18f);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = src.GetPixelBilinear((x + 0.5f) / size, (y + 0.5f) / size).a;
                    px[y * size + x] = new Color(tint.r, tint.g, tint.b, a);
                }
            icon.SetPixels(px); icon.Apply(false);
            Object.Destroy(src);
            return Sprite.Create(icon, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
