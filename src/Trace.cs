// Tracing a paint onto whatever it lies on (ported from PotatoPainter probe test 22; findings in
// claude/potatopainter-research.md). A square grid around the paint's centre is cast onto the surfaces under it, on every
// detail level (LOD) of every version (new, worn, broken) of each piece, so each simpler far-away model carries its own
// copy and the game swaps them with the model. Stone is traced as the game draws it (with its vertex "ripple"), a seam
// between two pieces is traced on the neighbour, leaves are never painted, and on a tree the bark nearest the aimed
// plane is taken so branches in front are skipped.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PotatoPainter
{
    // Where and how a paint lies: its centre on the surface, the surface's outward normal, the rune's up direction.
    internal struct Frame
    {
        internal Vector3 Center, Normal, Up;
        internal bool Tree;
    }

    // Everything one paint added to the world, so it can be taken away again.
    internal sealed class Built
    {
        internal readonly List<GameObject> Objects = new List<GameObject>();
        internal readonly List<(LODGroup group, int level, Renderer r)> Registered = new List<(LODGroup, int, Renderer)>();
        internal readonly List<Bend.Bender> Benders = new List<Bend.Bender>();
        internal readonly List<Ground.Sheet> GroundSheets = new List<Ground.Sheet>();
        internal Transform CenterHost;
        internal readonly HashSet<int> Hosts = new HashSet<int>();
        internal int Sheets;
        internal bool OnGround;                                   // some of it lies on the ground (Grass.cs)

        internal void Clear()
        {
            foreach (var (group, level, r) in Registered)
            {
                if (!group) continue;
                var lods = group.GetLODs();
                if (level >= lods.Length) continue;
                lods[level].renderers = lods[level].renderers.Where(x => x && x != r).ToArray();
                group.SetLODs(lods);   // switch distances unchanged: the group's size is not recalculated
            }
            foreach (var o in Objects) if (o) UnityEngine.Object.Destroy(o);
            foreach (var b in Benders) Bend.All.Remove(b);
            foreach (var g in GroundSheets) Ground.Remove(g);
            GroundSheets.Clear();
            Objects.Clear(); Registered.Clear(); Benders.Clear(); Hosts.Clear(); Sheets = 0; CenterHost = null; OnGround = false;
        }
    }

    internal static class Trace
    {
        internal const string Prefix = "PotatoPainter ";
        internal static readonly int Mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle", "piece_nonsolid");

        internal sealed class Surface { internal Vector3[] V; internal int[] T; internal Renderer R; internal Material[] TriMat; internal Vector2[] UV2; }

        // One drawable version of a host: a detail level of one of its LOD groups (new, worn, broken), or its plain renderers.
        private sealed class Level
        {
            internal LODGroup Group; internal int Index; internal Transform Parent; internal bool Active;
            internal readonly List<Surface> Surfaces = new List<Surface>();
        }

        private sealed class Host
        {
            internal Transform Root;
            internal readonly List<Level> Levels = new List<Level>();
            internal readonly List<Surface> Reference = new List<Surface>();   // what is drawn up close right now
        }

        // A tree's bark nearest the aimed plane (within Band) rather than the first surface hit, so branches are skipped.
        private struct Plane { internal bool On; internal Vector3 Point, Normal; internal float Band; }

        // ------------------------------------------------------------------ aiming
        // Where a paint of this size would go along the ray, as the game draws the surfaces there.
        internal static bool Aim(Vector3 eye, Vector3 dir, float max, float size, Vector3 cameraRight, float turn, out Frame frame)
        {
            frame = default;
            if (!Physics.Raycast(eye, dir, out var hit, max, Mask, QueryTriggerInteraction.Ignore)) return false;
            var hosts = Hosts(hit.point, size, hit.collider);
            if (hosts.Count == 0) return false;
            var reference = hosts.SelectMany(h => h.Reference).ToList();
            if (!Cast(reference, eye, dir, hit.distance + 1.5f, default, out var center, out var n, out var aimed, out var aimedTri, out _)) return false;
            var mat = aimed.TriMat != null ? aimed.TriMat[aimedTri] : null;
            frame.Center = center; frame.Normal = n;
            frame.Tree = mat && mat.shader && mat.shader.name == "Custom/Vegetation";
            // Upright: on a wall the rune's top points up; on a floor, away from you. Cross(side, n) (probe test 18; the
            // other order drew every rune upside down). The image's top row is v = 1.
            var side = Vector3.Cross(Vector3.up, n);
            if (side.sqrMagnitude < 0.01f) side = cameraRight;
            side = Vector3.ProjectOnPlane(side, n).normalized;
            if (Vector3.Dot(side, cameraRight) < 0f) side = -side;
            frame.Up = Vector3.Cross(side, n);
            // The mouse wheel turns it about the surface's normal, the way it turns a piece (turn = the game's own placement
            // rotation in degrees; seen from the front, clockwise as a piece turns seen from above).
            if (turn != 0f) frame.Up = Quaternion.AngleAxis(turn, n) * frame.Up;
            return true;
        }

        // ------------------------------------------------------------------ building
        // Traces the paint on everything under it. preview: only what is drawn up close right now, in world space, not
        // added to the game's model switching (the placement ghost). halo: drawn again on the same sheet with it (etched
        // runes, the way probe test 22 drew it), or null.
        internal static Built Build(Frame f, float size, float lift, Material material, Material halo, Color col, Color glow, bool preview, string name)
        {
            var built = new Built();
            var hosts = Hosts(f.Center, size, null);
            // The same set the paint compares against later (HostIds), so it only rebuilds when it really changed.
            built.Hosts.UnionWith(HostIds(f.Center, size));
            if (hosts.Count == 0 || !material) return built;
            var n = f.Normal; var up = f.Up; var side = Vector3.Cross(n, up);
            var plane = f.Tree ? new Plane { On = true, Point = f.Center, Normal = n, Band = size * 0.35f + 0.05f } : default;
            int grid = Mathf.Clamp(Mathf.RoundToInt(size / 0.03f), 16, 96);
            float reach = 0.25f + size * 0.25f, radius = size * 0.75f + reach;
            int count = (grid + 1) * (grid + 1);
            var starts = new Vector3[count]; var uvs = new Vector2[count];
            for (int y = 0; y <= grid; y++)
                for (int x = 0; x <= grid; x++)
                {
                    float u = (float)x / grid, v = (float)y / grid; int i = y * (grid + 1) + x;
                    // u runs along side = Cross(n, up), which is the probe's side (pointing to the painter's right): for
                    // side s perpendicular to n, Cross(n, Cross(s, n)) = s. Same layout as probe test 18 (not mirrored).
                    starts[i] = f.Center + side * ((u - 0.5f) * size) + up * ((v - 0.5f) * size) + n * reach;
                    uvs[i] = new Vector2(u, v);
                }

            // Which host owns each point: the one whose close-up surface is on top there.
            var owner = new int[count]; for (int i = 0; i < count; i++) owner[i] = -1;
            var nearRef = hosts.Select(h => Near(h.Reference, f.Center, radius)).ToList();
            for (int i = 0; i < count; i++)
            {
                float best = float.MaxValue;
                for (int k = 0; k < hosts.Count; k++)
                    if (Cast(nearRef[k], starts[i], -n, reach * 2f, plane, out var at, out _, out _, out _, out _))
                    {
                        float d = Vector3.Distance(starts[i], at);
                        if (d < best) { best = d; owner[i] = k; }
                    }
            }
            // The host the centre is painted on: the paint goes when it's destroyed.
            {
                float best = float.MaxValue;
                for (int k = 0; k < hosts.Count; k++)
                    if (Cast(nearRef[k], f.Center + n * reach, -n, reach * 2f, plane, out var at, out _, out _, out _, out _))
                    {
                        float d = Vector3.Distance(f.Center + n * reach, at);
                        if (d < best) { best = d; built.CenterHost = hosts[k].Root; }
                    }
            }

            var otherCache = new Dictionary<(int, int, bool), List<Surface>>();
            List<Surface> Other(int j, Level like)
            {
                var key = (j, like.Index, like.Active);
                if (otherCache.TryGetValue(key, out var got)) return got;
                var match = hosts[j].Levels.FirstOrDefault(l => l.Index == like.Index && l.Active == like.Active)
                    ?? hosts[j].Levels.Where(l => l.Active == like.Active).OrderByDescending(l => l.Index).FirstOrDefault();
                got = Near(match != null ? match.Surfaces : hosts[j].Reference, f.Center, radius);
                otherCache[key] = got; return got;
            }

            for (int k = 0; k < hosts.Count; k++)
            {
                if (!owner.Contains(k)) continue;
                foreach (var lv in hosts[k].Levels)
                {
                    if (preview && !(lv.Active && lv.Index == 0)) continue;
                    var surfaces = Near(lv.Surfaces, f.Center, radius);
                    var verts = new Vector3[count]; var normals = new Vector3[count]; var ok = new bool[count]; var uv2s = new Vector2[count];
                    var corners = new int[count * 3]; var bary = new Vector3[count]; var hitR = new Renderer[count];
                    // The surface material where the centre lands on this model: trees bend in the wind.
                    Material hostMat = null; Renderer hostR = null;
                    if (Cast(surfaces, f.Center + n * reach, -n, reach * 2f, plane, out _, out _, out var cs, out var ct, out _) && cs.R && cs.TriMat != null) { hostMat = cs.TriMat[ct]; hostR = cs.R; }
                    for (int i = 0; i < count; i++)
                    {
                        if (owner[i] < 0) continue;
                        // A corner on another piece (a seam) is traced on that piece, at the same detail level.
                        var on = owner[i] == k ? surfaces : Other(owner[i], lv);
                        if (Cast(on, starts[i], -n, reach * 2f, plane, out var at, out var nn, out var hs, out var ht, out var u2))
                        {
                            verts[i] = at + nn * lift; normals[i] = nn; uv2s[i] = u2; ok[i] = true;
                            hitR[i] = hs.R;
                            int i0 = hs.T[3 * ht], i1 = hs.T[3 * ht + 1], i2 = hs.T[3 * ht + 2];
                            corners[3 * i] = i0; corners[3 * i + 1] = i1; corners[3 * i + 2] = i2;
                            bary[i] = Barycentric(at, hs.V[i0], hs.V[i1], hs.V[i2]);
                        }
                    }
                    var tri = new List<int>(); float cell = size / grid * 2.5f;
                    for (int y = 0; y < grid; y++)
                        for (int x = 0; x < grid; x++)
                        {
                            int a = y * (grid + 1) + x, b = a + 1, c = a + grid + 1, d = c + 1;
                            if (owner[a] != k || !(ok[a] && ok[b] && ok[c] && ok[d])) continue;
                            if ((verts[a] - verts[b]).magnitude > cell || (verts[a] - verts[c]).magnitude > cell || (verts[d] - verts[b]).magnitude > cell || (verts[d] - verts[c]).magnitude > cell) continue;
                            tri.AddRange(new[] { a, c, b, b, c, d, a, b, c, b, d, c });   // both faces
                        }
                    if (tri.Count == 0) continue;

                    // Trees: bent on the CPU exactly like the bark's triangles, when the bark has no flutter or push and every
                    // point landed on that one bark renderer (probe test 14). Otherwise drawn still.
                    bool sway = Bend.Sways(hostMat);
                    bool cpu = sway && !preview && hostR && hostMat.GetFloat("_RippleDistance") == 0f && hostMat.GetFloat("_PushDistance") == 0f
                        && Enumerable.Range(0, count).All(i => !ok[i] || hitR[i] == hostR);
                    // Built in the host's own space, so it moves with it and the game shows it with that detail level.
                    var parent = preview ? null : (cpu ? hostR.transform : lv.Parent);
                    if (parent)
                    {
                        var toLocal = parent.worldToLocalMatrix; var normalToLocal = parent.localToWorldMatrix.transpose;
                        for (int i = 0; i < count; i++) if (ok[i]) { verts[i] = toLocal.MultiplyPoint3x4(verts[i]); normals[i] = normalToLocal.MultiplyVector(normals[i]).normalized; }
                    }
                    var mesh = new Mesh { name = Prefix + "sheet" };
                    mesh.vertices = verts; mesh.normals = normals; mesh.uv = uvs; mesh.colors = Enumerable.Repeat(Color.white, count).ToArray(); mesh.uv2 = uv2s; mesh.triangles = tri.ToArray();
                    mesh.RecalculateTangents(); mesh.RecalculateBounds();
                    var go = new GameObject(Prefix + name + " LOD" + lv.Index);
                    go.layer = parent ? parent.gameObject.layer : hosts[k].Root.gameObject.layer;
                    if (parent) go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = material;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = true;
                    Look.Colour(r, col, glow);
                    built.Objects.Add(go); built.Sheets++;
                    MeshRenderer hr = null;
                    if (halo)
                    {
                        var hg = new GameObject(Prefix + name + " halo LOD" + lv.Index);
                        hg.layer = go.layer;
                        if (parent) hg.transform.SetParent(parent, false);
                        hg.AddComponent<MeshFilter>().sharedMesh = mesh;
                        hr = hg.AddComponent<MeshRenderer>();
                        hr.sharedMaterial = halo;
                        hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        hr.receiveShadows = false;
                        Look.HaloColour(hr, glow);
                        built.Objects.Add(hg);
                    }
                    var groundHm = hosts[k].Root.GetComponent<Heightmap>();
                    if (groundHm)
                    {
                        built.OnGround = true;
                        // On the ground: laid on the ground as it is drawn (Ground.cs), refreshed as the camera moves.
                        var on = new List<(int, int, int, int, Vector3, Vector3, Vector3)>();
                        for (int i = 0; i < count; i++)
                            if (ok[i] && owner[i] == k)
                                on.Add((i, corners[3 * i], corners[3 * i + 1], corners[3 * i + 2], bary[i], verts[i] - normals[i] * lift, normals[i]));
                        mesh.MarkDynamic();
                        var gs = Ground.Make(groundHm, mesh, r, parent, lift, on);
                        if (gs != null) built.GroundSheets.Add(gs);
                    }
                    if (cpu)
                    {
                        var hm = hostR.GetComponent<MeshFilter>().sharedMesh;
                        var hu2 = hm.uv2; if (hu2 == null || hu2.Length != hm.vertexCount) hu2 = null;
                        var s3 = hostR.transform.lossyScale; float scale = (Mathf.Abs(s3.x) + Mathf.Abs(s3.y) + Mathf.Abs(s3.z)) / 3f;
                        mesh.MarkDynamic();
                        var bender = new Bend.Bender
                        {
                            Mesh = mesh, Host = hostR, Paint = r, HostMat = hostMat, HostLocal = hm.vertices, HostUV2 = hu2, Corners = corners, Bary = bary, Ok = ok,
                            LiftLocal = lift / Mathf.Max(scale, 1e-4f), Buf = (Vector3[])verts.Clone(), Nrm = (Vector3[])normals.Clone(),
                            Bent = new Vector3[hm.vertexCount], BentFrame = Enumerable.Repeat(-1, hm.vertexCount).ToArray()
                        };
                        Bend.All.Add(bender); built.Benders.Add(bender);
                    }
                    if (!preview && lv.Group)
                    {
                        var lods = lv.Group.GetLODs();
                        lods[lv.Index].renderers = lods[lv.Index].renderers.Concat(hr ? new Renderer[] { r, hr } : new Renderer[] { r }).ToArray();
                        lv.Group.SetLODs(lods);
                        built.Registered.Add((lv.Group, lv.Index, r));
                        if (hr) built.Registered.Add((lv.Group, lv.Index, hr));
                    }
                }
            }
            return built;
        }

        // The roots of everything a paint of this size at this spot could lie on (pieces, trees, rocks, the ground).
        internal static HashSet<int> HostIds(Vector3 center, float size)
        {
            var ids = new HashSet<int>();
            foreach (var c in Physics.OverlapSphere(center, size * 0.8f + 0.1f, Mask, QueryTriggerInteraction.Ignore))
            {
                if (c.GetComponentInParent<Character>()) continue;
                var root = RootOf(c);
                if (root) ids.Add(root.GetInstanceID());
            }
            return ids;
        }

        private static List<Host> Hosts(Vector3 center, float size, Collider first)
        {
            var hosts = new List<Host>(); var seen = new HashSet<Transform>();
            void Add(Collider c)
            {
                if (!c || c.GetComponentInParent<Character>()) return;
                var root = RootOf(c);
                if (!root || !seen.Add(root)) return;
                var h = Read(c);
                if (h.Reference.Count > 0) hosts.Add(h);
            }
            Add(first);
            foreach (var c in Physics.OverlapSphere(center, size * 0.8f + 0.1f, Mask, QueryTriggerInteraction.Ignore)) Add(c);
            return hosts;
        }

        // ------------------------------------------------------------------ reading surfaces
        private static Surface FromMesh(Mesh mesh, Transform at)
        {
            var local = mesh.vertices; var world = new Vector3[local.Length]; var m = at.localToWorldMatrix;
            for (int i = 0; i < local.Length; i++) world[i] = m.MultiplyPoint3x4(local[i]);
            return new Surface { V = world, T = mesh.triangles };
        }

        // Valheim's vertex "ripple", as in the DX11 vertex programs of Custom/Piece and Custom/StaticRock: with W the vertex's
        // world position, a = W.y + W.z, b = W.z + W.x, c = W.y + W.x, f = _RippleFreq, the vertex moves by _RippleDistance *
        // (sin(f a) sin(0.568562 f a), k sin(f b) sin(0.457431 f b), sin(f c) sin(0.642344 f a)), k = 0.4 for Custom/Piece
        // and 1 for Custom/StaticRock. Triangles are drawn flat between the moved vertices.
        private static bool Ripple(Material m, out float dist, out float freq, out float k)
        {
            dist = freq = 0f; k = 1f;
            if (!m || !m.shader) return false;
            var n = m.shader.name;
            if (n == "Custom/Piece") k = 0.4f; else if (n != "Custom/StaticRock") return false;
            if (!m.HasProperty("_RippleDistance") || !m.HasProperty("_RippleFreq")) return false;
            dist = m.GetFloat("_RippleDistance"); freq = m.GetFloat("_RippleFreq");
            return dist != 0f;
        }

        private static Vector3 Rippled(Vector3 w, float dist, float f, float k)
        {
            double a = (w.y + w.z) * (double)f, b = (w.z + w.x) * (double)f, c = (w.y + w.x) * (double)f;
            return w + dist * new Vector3((float)(Math.Sin(a) * Math.Sin(0.568562 * a)), k * (float)(Math.Sin(b) * Math.Sin(0.457431 * b)), (float)(Math.Sin(c) * Math.Sin(0.642344 * a)));
        }

        private static Surface FromRenderer(Renderer r)
        {
            if (!(r is MeshRenderer)) return null;
            var mf = r.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) return null;
            var mesh = mf.sharedMesh; var mats = r.sharedMaterials;
            var plain = FromMesh(mesh, mf.transform);
            var uv2 = mesh.uv2; if (uv2 == null || uv2.Length != plain.V.Length) uv2 = null;
            if (!mats.Any(m => Ripple(m, out _, out _, out _)))
            {
                var triMat = new List<Material>(); var tris = new List<int>();
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var mat = mats.Length == 0 ? null : mats[Mathf.Min(sub, mats.Length - 1)];
                    if (IsFoliage(mat)) continue;   // never painted
                    var st = mesh.GetTriangles(sub); tris.AddRange(st);
                    for (int i = 0; i < st.Length / 3; i++) triMat.Add(mat);
                }
                return new Surface { V = plain.V, T = tris.ToArray(), R = r, TriMat = triMat.ToArray(), UV2 = uv2 };
            }
            // Each submesh with its own material's ripple (a vertex shared by two materials is moved once per material).
            var V = new List<Vector3>(); var T = new List<int>(); var TM = new List<Material>(); var U2 = new List<Vector2>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var mat = mats.Length == 0 ? null : mats[Mathf.Min(sub, mats.Length - 1)];
                int at = V.Count;
                if (uv2 != null) U2.AddRange(uv2);
                if (Ripple(mat, out var dist, out var f, out var k)) foreach (var w in plain.V) V.Add(Rippled(w, dist, f, k));
                else V.AddRange(plain.V);
                var st = mesh.GetTriangles(sub);
                foreach (var t in st) T.Add(t + at);
                for (int i = 0; i < st.Length / 3; i++) TM.Add(mat);
            }
            return new Surface { V = V.ToArray(), T = T.ToArray(), R = r, TriMat = TM.ToArray(), UV2 = uv2 != null ? U2.ToArray() : null };
        }

        // A collider's surface in world space: a box as its 12 triangles, a mesh collider when its mesh can be read.
        private static Surface FromCollider(Collider col)
        {
            if (col is BoxCollider box)
            {
                var m = box.transform.localToWorldMatrix; var c = box.center; var e = box.size * 0.5f;
                var v = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    v[i] = m.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                var t = new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
                return new Surface { V = v, T = t, TriMat = new Material[12] };
            }
            if (col is MeshCollider mc && mc.sharedMesh && mc.sharedMesh.isReadable)
            {
                var s = FromMesh(mc.sharedMesh, mc.transform);
                s.TriMat = new Material[s.T.Length / 3];
                return s;
            }
            return null;
        }

        // Our own sheets are never read as part of what they lie on (probe test 8: that made each paint slower and land
        // on the paint before it).
        internal static bool Ours(Renderer r) => r && r.name.StartsWith(Prefix, StringComparison.Ordinal);

        // Leaves and other cards: Custom/Vegetation drawn two-sided (_Cull 0) or fluttering (_RippleDistance > 0).
        private static bool IsFoliage(Material m)
        {
            if (!m || !m.shader || m.shader.name != "Custom/Vegetation") return false;
            return (m.HasProperty("_Cull") && m.GetFloat("_Cull") == 0f) || (m.HasProperty("_RippleDistance") && m.GetFloat("_RippleDistance") > 0f);
        }

        private static bool Snow(Renderer r) => r.sharedMaterials.Any(m => m && m.shader && m.shader.name.IndexOf("Snow", StringComparison.OrdinalIgnoreCase) >= 0);

        internal static Transform RootOf(Collider c)
        {
            if (c.GetComponent<Heightmap>()) return c.transform;
            var piece = c.GetComponentInParent<Piece>();
            return piece ? piece.transform : c.attachedRigidbody ? c.attachedRigidbody.transform : c.transform.root;
        }

        private static Host Read(Collider c)
        {
            var root = RootOf(c);
            var h = new Host { Root = root };
            if (c is MeshCollider ground && c.GetComponent<Heightmap>())
            {
                if (!ground.sharedMesh || !ground.sharedMesh.isReadable) return h;
                var s = FromMesh(ground.sharedMesh, ground.transform);
                var lv = new Level { Index = 0, Parent = ground.transform, Active = true };
                lv.Surfaces.Add(s); h.Levels.Add(lv); h.Reference.Add(s);
                return h;
            }
            var groups = root.GetComponentsInChildren<LODGroup>(true).Where(g => !g.GetLODs().SelectMany(l => l.renderers).Any(r => r && Snow(r))).ToList();
            foreach (var g in groups)
            {
                var lods = g.GetLODs();
                bool active = g.gameObject.activeInHierarchy;
                for (int i = 0; i < lods.Length; i++)
                {
                    var lv = new Level { Group = g, Index = i, Parent = g.transform, Active = active };
                    foreach (var r in lods[i].renderers) { if (!r || Ours(r)) continue; var s = FromRenderer(r); if (s != null) lv.Surfaces.Add(s); }
                    if (lv.Surfaces.Count > 0) h.Levels.Add(lv);
                    if (active && i == 0) h.Reference.AddRange(lv.Surfaces);
                }
            }
            if (groups.Count == 0)
            {
                var lv = new Level { Index = 0, Parent = root, Active = true };
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false)) { if (Snow(r) || Ours(r)) continue; var s = FromRenderer(r); if (s != null) lv.Surfaces.Add(s); }
                if (lv.Surfaces.Count > 0) { h.Levels.Add(lv); h.Reference.AddRange(lv.Surfaces); }
            }
            // Drawn models that can't be read (meshes not marked readable, as in some mods' pieces; test 0.1.3: "hit
            // collider (layer piece), which has no surface that can be read" on the user's stone build): its colliders
            // are traced instead, boxes and readable mesh colliders, as one level drawn with the piece.
            if (h.Reference.Count == 0)
            {
                var lv = new Level { Index = 0, Parent = root, Active = true };
                foreach (var col in root.GetComponentsInChildren<Collider>(false))
                {
                    if (col.isTrigger || ((1 << col.gameObject.layer) & Mask) == 0 || RootOf(col) != root) continue;
                    var s = FromCollider(col);
                    if (s != null) lv.Surfaces.Add(s);
                }
                if (lv.Surfaces.Count > 0) { h.Levels.Add(lv); h.Reference.AddRange(lv.Surfaces); }
            }
            return h;
        }

        private static List<Surface> Near(List<Surface> surfaces, Vector3 center, float radius)
        {
            float r2 = radius * radius; var list = new List<Surface>();
            foreach (var s in surfaces)
            {
                var keep = new List<int>(); var keepMat = new List<Material>();
                for (int i = 0; i + 2 < s.T.Length; i += 3)
                {
                    Vector3 a = s.V[s.T[i]], b = s.V[s.T[i + 1]], c = s.V[s.T[i + 2]];
                    if ((Closest(center, a, b, c) - center).sqrMagnitude <= r2) { keep.Add(s.T[i]); keep.Add(s.T[i + 1]); keep.Add(s.T[i + 2]); if (s.TriMat != null) keepMat.Add(s.TriMat[i / 3]); }
                }
                if (keep.Count > 0) list.Add(new Surface { V = s.V, T = keep.ToArray(), R = s.R, TriMat = s.TriMat != null ? keepMat.ToArray() : null, UV2 = s.UV2 });
            }
            return list;
        }

        private static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        private static Vector3 Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1), d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denom) < 1e-12f) return new Vector3(1f, 0f, 0f);
            float v = (d11 * d20 - d01 * d21) / denom, w = (d00 * d21 - d01 * d20) / denom;
            return new Vector3(1f - v - w, v, w);
        }

        // The ray's hit on these surfaces: which surface and triangle, and the hit's UV2 (interpolated) when the surface has
        // one. With a plane (trees): of all hits, the one nearest the plane within its band. Otherwise the first hit.
        private static bool Cast(List<Surface> surfaces, Vector3 from, Vector3 dir, float max, Plane plane, out Vector3 point, out Vector3 normal, out Surface hitSurface, out int hitTri, out Vector2 hitUV2)
        {
            point = normal = Vector3.zero; float best = max; hitSurface = null; hitTri = -1; hitUV2 = Vector2.zero;
            float bestScore = float.MaxValue;
            foreach (var s in surfaces)
            {
                var v = s.V; var t = s.T;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(dir, e2);
                    float det = Vector3.Dot(e1, p);
                    if (Mathf.Abs(det) < 1e-8f) continue;
                    float inv = 1f / det; Vector3 o = from - a;
                    float u = Vector3.Dot(o, p) * inv; if (u < 0f || u > 1f) continue;
                    Vector3 q = Vector3.Cross(o, e1);
                    float w = Vector3.Dot(dir, q) * inv; if (w < 0f || u + w > 1f) continue;
                    float d = Vector3.Dot(e2, q) * inv;
                    if (plane.On)
                    {
                        if (d <= 0f || d >= max) continue;
                        float score = Mathf.Abs(Vector3.Dot(from + dir * d - plane.Point, plane.Normal));
                        if (score > plane.Band || score >= bestScore) continue;
                        bestScore = score; best = d;
                    }
                    else { if (d <= 0f || d >= best) continue; best = d; }
                    point = from + dir * d;
                    hitSurface = s; hitTri = i / 3;
                    if (s.UV2 != null) hitUV2 = s.UV2[t[i]] * (1f - u - w) + s.UV2[t[i + 1]] * u + s.UV2[t[i + 2]] * w;
                    normal = Vector3.Cross(e1, e2).normalized;
                    if (Vector3.Dot(normal, dir) > 0f) normal = -normal;
                }
            }
            return hitSurface != null;
        }
    }
}
