// Grass, flowers and other ground clutter are left out under paint on the ground, so a rune on a meadow can be seen (test
// 0.1.1: the ground paints were there, hidden in the grass). Each clump is left out when any of it could reach over the
// rune, not only when it is rooted under it: a meadow grass clump is the mesh "default" (0.91 m across) drawn at the
// InstanceRenderer's m_scale (1.5, 2, 1.5) times the clutter's random scale (InstanceRenderer.AddInstance), so its blades
// reach about 0.7 m or more from its root (0.1.2 only left out clumps rooted under the rune: no visible change). Nothing is changed in the world: the game's own "clear
// vegetation" ground paint was not used, because it also turns dirt, paths and paving under it back to plain ground
// (Heightmap.PaintCleared lerps the whole paint mask colour to (0, 0, 0, 0) for ClearVegetation) and stays when the rune
// is removed. Here the clutter comes back as soon as the paint is gone.
// How the game places clutter (assembly_valheim, read 2026-10-30): ClutterSystem.GenerateVegPatch picks points in a patch,
// asks GetGroundInfo(point, out ground point, out normal, out heightmap, out biome) and skips the point when that returns
// false. Right after that test this mod adds its own: skip the point when that clutter, rooted there, could reach over a
// ground paint. ResetGrass(centre, radius) marks the patches there to be made again.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace PotatoPainter
{
    internal static class Grass
    {
        // Added to each clutter's reach (wind sway), and how far above or below the paint's surface a root counts.
        internal const float Margin = 0.05f, Depth = 0.75f;
        // At most this far past the rune's square, so a 0.5 m rune gets a bare square of about 1.5 m; bigger clumps rooted
        // farther out can still lean over its edges (the user, 2026-10-30: "Blades at the edges is fine"; at 0.75 m, test
        // 0.1.6: "can we reduce the grass removal to be slightly smaller?").
        // Measured reach without the cap: meadow grass 2.14 m, short meadow grass 1.42 m, vass 3.19 m (test 0.1.3).
        internal const float ReachCap = 0.5f;
        // Patches marked to be made again reach this far past the rune's square (more than any clutter's reach so far).
        private const float ResetReach = 2.5f;
        private static readonly Dictionary<ClutterSystem.Clutter, float> reach = new Dictionary<ClutterSystem.Clutter, float>();

        private struct Area { internal Vector3 Center, Side, Up, Normal; internal float Half; }

        private static readonly Dictionary<object, Area> areas = new Dictionary<object, Area>();
        private static Area[] list = new Area[0];
        internal static bool Hooked;

        // Leaves clutter out under this paint (key: the paint or the ghost), or brings it back with on = false.
        internal static void Set(object key, bool on, Frame f, float size)
        {
            if (areas.TryGetValue(key, out var old))
            {
                if (on && (old.Center - f.Center).sqrMagnitude < 1e-4f && Mathf.Abs(old.Half - size * 0.5f) < 1e-3f) return;
                areas.Remove(key); Reset(old);
            }
            if (on)
            {
                var a = new Area { Center = f.Center, Normal = f.Normal, Up = f.Up, Side = Vector3.Cross(f.Normal, f.Up), Half = size * 0.5f };
                areas[key] = a; Reset(a);
            }
            list = areas.Values.ToArray();
        }

        internal static void Clear(object key)
        {
            if (!areas.TryGetValue(key, out var old)) return;
            areas.Remove(key); Reset(old);
            list = areas.Values.ToArray();
        }

        private static void Reset(Area a)
        {
            if (!ClutterSystem.instance) return;   // leaving the world: nothing to remake
            ClutterSystem.instance.ResetGrass(a.Center, a.Half + ResetReach);
        }

        // Called by the game's clutter placement for every candidate root (patched in below).
        public static bool Hidden(Vector3 p, ClutterSystem.Clutter clutter)
        {
            var l = list;
            if (l.Length == 0) return false;
            float r = Reach(clutter);
            for (int i = 0; i < l.Length; i++)
            {
                var d = p - l[i].Center; float h = l[i].Half + r;
                if (d.sqrMagnitude > h * h * 2f + Depth * Depth) continue;
                if (Mathf.Abs(Vector3.Dot(d, l[i].Side)) <= h && Mathf.Abs(Vector3.Dot(d, l[i].Up)) <= h && Mathf.Abs(Vector3.Dot(d, l[i].Normal)) <= Depth)
                    return true;
            }
            return false;
        }

        // How far from its root a clutter can reach sideways at its largest: its drawn size (the instanced mesh at the
        // InstanceRenderer's scale, or the prefab's renderers) times its largest random scale, leaning by its largest tilt.
        private static float Reach(ClutterSystem.Clutter c)
        {
            if (c == null) return 0f;
            if (reach.TryGetValue(c, out var r)) return r;
            float side = 0f, height = 0f;
            var prefab = c.m_prefab;
            var ir = prefab ? prefab.GetComponent<InstanceRenderer>() : null;
            if (c.m_instanced && ir && ir.m_mesh)
            {
                var b = ir.m_mesh.bounds;
                side = Mathf.Max((Mathf.Abs(b.center.x) + b.extents.x) * Mathf.Abs(ir.m_scale.x), (Mathf.Abs(b.center.z) + b.extents.z) * Mathf.Abs(ir.m_scale.z));
                height = (b.center.y + b.extents.y) * Mathf.Abs(ir.m_scale.y);
            }
            else if (prefab)
            {
                foreach (var rr in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    var b = rr.bounds; var o = prefab.transform.position;
                    side = Mathf.Max(side, Mathf.Abs(b.min.x - o.x), Mathf.Abs(b.max.x - o.x), Mathf.Abs(b.min.z - o.z), Mathf.Abs(b.max.z - o.z));
                    height = Mathf.Max(height, b.max.y - o.y);
                }
            }
            float scale = Mathf.Max(c.m_scaleMin, c.m_scaleMax, 0f);
            float tilt = Mathf.Clamp(Mathf.Max(Mathf.Abs(c.m_maxTilt), Mathf.Abs(c.m_minTilt)), 0f, 90f) * Mathf.Deg2Rad;
            r = Mathf.Min((side + height * Mathf.Sin(tilt)) * scale + Margin, ReachCap);
            reach[c] = r;
            return r;
        }

        [HarmonyPatch(typeof(ClutterSystem), "GenerateVegPatch")]
        private static class Patch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList();
                var info = AccessTools.Method(typeof(ClutterSystem), "GetGroundInfo");
                var hidden = AccessTools.Method(typeof(Grass), nameof(Hidden));
                var ps = info?.GetParameters();
                int at = info == null ? -1 : code.FindIndex(c => c.Calls(info));
                // Expected: ldarg.0, ldloc candidate, ldloca ground point, ldloca normal, ldloca heightmap, ldloca biome, call,
                // brfalse skip, then ldloc clutter, ldfld Clutter.m_biome.
                bool ok = at >= 5 && ps != null && ps.Length == 5 && ps[1].ParameterType == typeof(Vector3).MakeByRefType()
                    && (code[at - 4].opcode == OpCodes.Ldloca_S || code[at - 4].opcode == OpCodes.Ldloca)
                    && at + 3 < code.Count && (code[at + 1].opcode == OpCodes.Brfalse || code[at + 1].opcode == OpCodes.Brfalse_S) && code[at + 1].operand is Label
                    && (code[at + 2].opcode == OpCodes.Ldloc_S || code[at + 2].opcode == OpCodes.Ldloc)
                    && code[at + 3].opcode == OpCodes.Ldfld && code[at + 3].operand is FieldInfo fi && fi.DeclaringType == typeof(ClutterSystem.Clutter);
                if (!ok)
                {
                    Plugin.Log.LogWarning("Grass stays under ground paint this session: the game's clutter placement isn't laid out as expected (ClutterSystem.GenerateVegPatch).");
                    return code;
                }
                var skip = (Label)code[at + 1].operand;
                var first = new CodeInstruction(OpCodes.Ldloc, code[at - 4].operand);
                first.labels.AddRange(code[at + 2].labels); code[at + 2].labels.Clear();
                code.InsertRange(at + 2, new[] { first, new CodeInstruction(OpCodes.Ldloc, code[at + 2].operand), new CodeInstruction(OpCodes.Call, hidden), new CodeInstruction(OpCodes.Brtrue, skip) });
                Hooked = true;
                return code;
            }
        }
    }
}
