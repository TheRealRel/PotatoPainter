// Grass, flowers and other ground clutter are left out under paint on the ground, so a rune on a meadow can be seen (test
// 0.1.1: the ground paints were there, hidden in the grass). Nothing is changed in the world: the game's own "clear
// vegetation" ground paint was not used, because it also turns dirt, paths and paving under it back to plain ground
// (Heightmap.PaintCleared lerps the whole paint mask colour to (0, 0, 0, 0) for ClearVegetation) and stays when the rune
// is removed. Here the clutter comes back as soon as the paint is gone.
// How the game places clutter (assembly_valheim, read 2026-10-30): ClutterSystem.GenerateVegPatch picks points in a patch,
// asks GetGroundInfo(point, out ground point, out normal, out heightmap, out biome) and skips the point when that returns
// false. Right after that test this mod adds its own: skip the point when it lies under a ground paint. ResetGrass(centre,
// radius) marks the patches there to be made again.
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
        // How far past the rune's square the clutter is left out, and how far above or below its surface a point counts.
        internal const float Margin = 0.1f, Depth = 0.75f;

        private struct Area { internal Vector3 Center, Side, Up, Normal; internal float Half; }

        private static readonly Dictionary<object, Area> areas = new Dictionary<object, Area>();
        private static Area[] list = new Area[0];
        internal static bool Hooked;

        // Leaves clutter out under this paint (key: the paint or the ghost), or brings it back with on = false.
        internal static void Set(object key, bool on, Frame f, float size)
        {
            if (areas.TryGetValue(key, out var old))
            {
                if (on && (old.Center - f.Center).sqrMagnitude < 1e-4f && Mathf.Abs(old.Half - (size * 0.5f + Margin)) < 1e-3f) return;
                areas.Remove(key); Reset(old);
            }
            if (on)
            {
                var a = new Area { Center = f.Center, Normal = f.Normal, Up = f.Up, Side = Vector3.Cross(f.Normal, f.Up), Half = size * 0.5f + Margin };
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
            if (ClutterSystem.instance) ClutterSystem.instance.ResetGrass(a.Center, a.Half * 1.5f);
        }

        // Called by the game's clutter placement for every candidate point (patched in below).
        public static bool Hidden(Vector3 p)
        {
            var l = list;
            for (int i = 0; i < l.Length; i++)
            {
                var d = p - l[i].Center;
                if (d.sqrMagnitude > l[i].Half * l[i].Half * 2f + Depth * Depth) continue;
                if (Mathf.Abs(Vector3.Dot(d, l[i].Side)) <= l[i].Half && Mathf.Abs(Vector3.Dot(d, l[i].Up)) <= l[i].Half && Mathf.Abs(Vector3.Dot(d, l[i].Normal)) <= Depth) return true;
            }
            return false;
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
                // Expected: ldarg.0, ldloc candidate, ldloca ground point, ldloca normal, ldloca heightmap, ldloca biome, call, brfalse skip.
                bool ok = at >= 5 && ps != null && ps.Length == 5 && ps[1].ParameterType == typeof(Vector3).MakeByRefType()
                    && (code[at - 4].opcode == OpCodes.Ldloca_S || code[at - 4].opcode == OpCodes.Ldloca)
                    && at + 2 < code.Count && (code[at + 1].opcode == OpCodes.Brfalse || code[at + 1].opcode == OpCodes.Brfalse_S) && code[at + 1].operand is Label;
                if (!ok)
                {
                    Plugin.Log.LogWarning("Grass stays under ground paint this session: the game's clutter placement isn't laid out as expected (ClutterSystem.GenerateVegPatch).");
                    return code;
                }
                var skip = (Label)code[at + 1].operand;
                var first = new CodeInstruction(OpCodes.Ldloc, code[at - 4].operand);
                first.labels.AddRange(code[at + 2].labels); code[at + 2].labels.Clear();
                code.InsertRange(at + 2, new[] { first, new CodeInstruction(OpCodes.Call, hidden), new CodeInstruction(OpCodes.Brtrue, skip) });
                Hooked = true;
                Plugin.Log.LogInfo("Grass is left out under ground paint (ClutterSystem.GenerateVegPatch patched).");
                return code;
            }
        }
    }
}
