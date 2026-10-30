// The Rúnakefli's own look on the cloned hammer: its model (from the user's Meshy model, tools/kefli.py) in place of the
// hammer's, held at the leather grip with the antler tip up where the hammer's head is, and the word ᚱᚢᚾᚨᚲᛖᚠᛚᛁ painted
// along the shaft on two sides as its own thin sheet (the stick's 128 px texture would make 3 cm runes mush).
// The hammer prefab (bundle c4210710, read 2026-10-30): root (ItemDrop, Rigidbody) with "collider" (BoxCollider, size
// 0.24 x 0.1 x 0.59, scaled 1.075 x 1 x 0.739) and "attach" at (0, 0.02, -0.12), the part shown in the hand and on the
// ground, holding "hammer" (MeshFilter, MeshRenderer with weapons1, shader Custom/Creature, scaled 0.3, turned 90° on X).
using System.IO;
using System.Reflection;
using Jotunn.Managers;
using UnityEngine;

namespace PotatoPainter
{
    internal static class Tool
    {
        internal static void Dress(GameObject prefab)
        {
            var attach = prefab.transform.Find("attach");
            var model = attach ? attach.Find("hammer") : null;
            var stick = LoadMesh("runakefli.mesh");
            if (!model || !stick) { Plugin.Log.LogWarning("The Rúnakefli keeps the hammer's look this session (its model or the hammer's parts weren't found)."); return; }
            model.name = "runakefli";
            model.localPosition = Vector3.zero; model.localRotation = Quaternion.identity; model.localScale = Vector3.one;
            model.GetComponent<MeshFilter>().sharedMesh = stick;
            var renderer = model.GetComponent<MeshRenderer>();
            var headless = GUIManager.IsHeadless();
            if (!headless && renderer)
            {
                // The hammer's own material (the game's item shader) with the stick's texture: sharp pixels like the game's
                // items, no metal (bone, wood, leather and antler), no normal map of the hammer's.
                var mat = new Material(renderer.sharedMaterial) { name = "PotatoPainter runakefli" };
                var tex = LoadTexture("runakefli_d.png", false);
                if (tex) { tex.filterMode = FilterMode.Point; mat.SetTexture("_MainTex", tex); }
                if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", null);
                if (mat.HasProperty("_MetallicGlossMap")) mat.SetTexture("_MetallicGlossMap", null);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
                if (mat.HasProperty("_MetalGloss")) mat.SetFloat("_MetalGloss", 0f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.15f);
                renderer.sharedMaterial = mat;
            }

            var runes = LoadMesh("runakefli_runes.mesh");
            if (runes)
            {
                var go = new GameObject("runes");
                go.layer = model.gameObject.layer;
                go.transform.SetParent(attach, false);
                go.AddComponent<MeshFilter>().sharedMesh = runes;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // Its own material in red ochre, glowing ember at strength 1 with a faint halo: a per-renderer colour isn't
                // copied when the game copies the tool into the hand.
                if (!headless)
                {
                    var tex = LoadTexture("runakefli_runes.png", false);
                    if (tex) { tex.wrapMode = TextureWrapMode.Clamp; Look.KeepCoverage(tex, 0.5f); }
                    r.sharedMaterial = Look.Paint("runakefli word", tex, Look.RedOchre, Look.Ember, 1f);
                    var haloTex = LoadTexture("runakefli_halo.png", false);
                    if (haloTex) { haloTex.wrapMode = TextureWrapMode.Clamp; haloTex.filterMode = FilterMode.Bilinear; }
                    var haloMat = Look.Halo("runakefli word", haloTex, Look.Ember * Look.HaloFaint);
                    if (haloMat)
                    {
                        var h = new GameObject("runes halo");
                        h.layer = go.layer;
                        h.transform.SetParent(attach, false);
                        h.AddComponent<MeshFilter>().sharedMesh = runes;
                        var hr = h.AddComponent<MeshRenderer>();
                        hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        hr.receiveShadows = false;
                        hr.sharedMaterial = haloMat;
                    }
                }
            }

            // Dropped on the ground: a box around the stick (65 cm, the hand at z 0, from z -0.155 to 0.495).
            var col = prefab.transform.Find("collider");
            var box = col ? col.GetComponent<BoxCollider>() : null;
            if (box)
            {
                col.localPosition = Vector3.zero; col.localRotation = Quaternion.identity; col.localScale = Vector3.one;
                box.size = new Vector3(0.06f, 0.06f, 0.65f);
                box.center = attach.localPosition + new Vector3(0f, 0f, 0.17f);
            }
        }

        // The icon: the stick drawn by the game's own renderer, lying corner to corner with the tip up and to the right.
        internal static void Icon(ItemDrop item)
        {
            if (GUIManager.IsHeadless() || !item) return;
            var sprite = RenderManager.Instance.Render(item.gameObject, Quaternion.Euler(0f, 0f, 45f) * Quaternion.Euler(0f, 90f, 0f));
            if (sprite) item.m_itemData.m_shared.m_icons = new[] { sprite };
        }

        private static byte[] Resource(string file)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("runakefli." + file))
            {
                if (s == null) { Plugin.Log.LogError("Missing resource runakefli." + file); return null; }
                var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
            }
        }

        private static Texture2D LoadTexture(string file, bool linear)
        {
            var bytes = Resource(file);
            if (bytes == null) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = "PotatoPainter " + file };
            return ImageConversion.LoadImage(t, bytes) ? t : null;
        }

        // The model format of PotatoFarmer's convert.py ("PTM1": counts, then positions, normals, UVs, triangles).
        private static Mesh LoadMesh(string file)
        {
            var bytes = Resource(file);
            if (bytes == null) return null;
            using (var reader = new BinaryReader(new MemoryStream(bytes)))
            {
                if (new string(reader.ReadChars(4)) != "PTM1") { Plugin.Log.LogError(file + " isn't a model file."); return null; }
                int vertices = reader.ReadInt32(), indices = reader.ReadInt32();
                var positions = new Vector3[vertices]; var normals = new Vector3[vertices]; var uvs = new Vector2[vertices]; var triangles = new int[indices];
                for (int i = 0; i < vertices; i++) positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                for (int i = 0; i < vertices; i++) normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                for (int i = 0; i < vertices; i++) uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                for (int i = 0; i < indices; i++) triangles[i] = reader.ReadInt32();
                var mesh = new Mesh { name = "PotatoPainter " + Path.GetFileNameWithoutExtension(file) };
                mesh.vertices = positions; mesh.normals = normals; mesh.uv = uvs; mesh.triangles = triangles;
                // White vertex colours: the paint material needs them (probe finding). Filled before assigning, since
                // Mesh.colors hands back a copy.
                var white = new Color[vertices];
                for (int i = 0; i < vertices; i++) white[i] = Color.white;
                mesh.colors = white;
                mesh.RecalculateTangents(); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
