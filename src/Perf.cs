// Test-only (0.4.1, the user: "placing large decals, lag got really bad"): how long each part of PotatoPainter takes, summed
// over 5 seconds and logged as one line when anything ran. Take out before release.
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace PotatoPainter
{
    internal static class Perf
    {
        private sealed class Stat { internal int N; internal double Total, Max; internal long A, B; }
        private static readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        private static float windowStart = -1f; private static int frames; private static float frameMax, frameSum;

        internal static Stopwatch Start() => Stopwatch.StartNew();

        // a, b: counters to sum with it (vertices, sheets, points).
        internal static void Stop(string key, Stopwatch sw, long a = 0, long b = 0)
        {
            sw.Stop(); double ms = sw.Elapsed.TotalMilliseconds;
            if (!stats.TryGetValue(key, out var s)) stats[key] = s = new Stat();
            s.N++; s.Total += ms; if (ms > s.Max) s.Max = ms; s.A += a; s.B += b;
        }

        // A time measured elsewhere (a trace's math on another thread), in ms.
        internal static void Add(string key, double ms, long a = 0, long b = 0)
        {
            if (!stats.TryGetValue(key, out var s)) stats[key] = s = new Stat();
            s.N++; s.Total += ms; if (ms > s.Max) s.Max = ms; s.A += a; s.B += b;
        }

        // Once a frame (Plugin.LateUpdate).
        internal static void Frame()
        {
            if (windowStart < 0f) windowStart = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime * 1000f; frames++; frameSum += dt; if (dt > frameMax) frameMax = dt;
            if (Time.unscaledTime - windowStart < 5f) return;
            if (stats.Count > 0)
            {
                var parts = stats.OrderByDescending(kv => kv.Value.Total).Select(kv =>
                    $"{kv.Key} {kv.Value.N}x total {kv.Value.Total:0} ms max {kv.Value.Max:0.0} ms" + (kv.Value.A != 0 ? $" [{kv.Value.A}" + (kv.Value.B != 0 ? $"/{kv.Value.B}" : "") + "]" : ""));
                Plugin.Log.LogInfo($"Perf 5 s: {frames} frames, frame avg {frameSum / Mathf.Max(1, frames):0.0} ms max {frameMax:0.0} ms; paints loaded {Paint.Loaded.Count}, ground sheets {Ground.All.Count}{(RuneLight.Shadows ? "; rune light shadows on" : "")}; " + string.Join("; ", parts));
            }
            stats.Clear(); frames = 0; frameSum = 0f; frameMax = 0f; windowStart = Time.unscaledTime;
        }

        // How long the game's own grass patches take to make (ResetGrass marks them; they're made in GenerateVegPatch).
        [HarmonyPatch(typeof(ClutterSystem), "GenerateVegPatch")]
        private static class GrassPatchTime
        {
            private static void Prefix(out Stopwatch __state) { __state = Stopwatch.StartNew(); }
            private static void Postfix(Stopwatch __state) { Stop("grass patch made", __state); }
        }
    }
}
