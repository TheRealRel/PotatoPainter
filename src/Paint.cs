// One paint: a piece with no model and no collider whose ZDO holds where and how it lies. Every player (not the
// dedicated server) traces it onto the pieces, trees, rocks or ground under it when it loads, and again whenever what lies
// under it changes (a wall that loads after it, a tree that grows back). As the placement ghost it shows a preview.
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;

namespace PotatoPainter
{
    public sealed class Paint : MonoBehaviour, IPlaced
    {
        // Set on each design's prefab; copied to every placed paint and ghost.
        public string Art;

        internal const float DefaultSize = 0.5f;
        internal static readonly List<Paint> Loaded = new List<Paint>();

        // Saved in worlds: never rename.
        private static readonly int KVersion = "PotatoPainter.Version".GetStableHashCode(), KCenter = "PotatoPainter.Center".GetStableHashCode(),
            KNormal = "PotatoPainter.Normal".GetStableHashCode(), KUp = "PotatoPainter.Up".GetStableHashCode(), KTree = "PotatoPainter.Tree".GetStableHashCode(),
            KSize = "PotatoPainter.Size".GetStableHashCode(), KColor = "PotatoPainter.Color".GetStableHashCode(), KLayer = "PotatoPainter.Layer".GetStableHashCode();

        // The placement ghost's latest aim, read by OnPlaced on the same client right after the game places the piece.
        private static Frame aim; private static bool aimValid; private static float aimTime; private static int aimLayer;
        private static string aimWhy = "";

        // Same rune on the same spot (centres closer than this share of its size, facing the same way): a repeat. Repeats
        // are left out when painting, and piles of them already in the world (test 0.1.2: 5 to 30 per spot, painted while
        // the grass hid them) are cut down to the top one when they load (the user, 2026-10-30).
        private const float RepeatShare = 0.15f;

        internal Rune Rune;
        internal Frame Frame; internal float Size; internal Color Col; internal int Layer;
        internal bool HasData;
        internal Transform CenterHost => built?.CenterHost;

        private ZNetView nview;
        private bool ghost, listed, placedWithoutAim, repeat;
        private Built built, preview;
        private float nextCheck, previewTime;
        private Vector3 lastEye = Vector3.positiveInfinity, lastDir;
        private float visibleCheck = -1f;

        private float Lift => 0.004f + 0.002f * Layer;   // layers 2 mm apart, the newest on top (probe test 11)

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            Rune = Runes.All.FirstOrDefault(r => r.Art == Art);
        }

        private void Start()
        {
            if (GUIManager.IsHeadless() || Rune == null) { enabled = false; return; }
            ghost = Player.IsPlacementGhost(gameObject) || !nview || nview.GetZDO() == null;
            if (ghost) return;
            Loaded.Add(this); listed = true;
            Load();
        }

        // On the placing client, right after the game created the piece (before Start).
        public void OnPlaced()
        {
            if (!nview || nview.GetZDO() == null) return;
            if (!aimValid || Time.time - aimTime > 1f)
            {
                placedWithoutAim = true;
                Plugin.Log.LogInfo($"Placed {Rune?.Name} without a usable preview ({(aimValid ? $"preview {Time.time - aimTime:0.0} s old" : "no preview: " + aimWhy)}): taken away again");
                return;
            }
            var same = Loaded.FirstOrDefault(p => p && p.HasData && Repeats(p, Rune, aim, DefaultSize));
            if (same)
            {
                repeat = true;
                Plugin.Log.LogInfo($"{Rune?.Name} at {aim.Center:F2}: already painted there (layer {same.Layer}), so this one is left out");
                return;
            }
            // The layer as it is now: the preview's can be stale when the aim hasn't moved since the last paint here
            // (two paints on one spot both landed on layer 0 in the first test).
            aimLayer = LayerAt(aim.Center, DefaultSize, null);
            var zdo = nview.GetZDO();
            zdo.Set(KCenter, aim.Center); zdo.Set(KNormal, aim.Normal); zdo.Set(KUp, aim.Up); zdo.Set(KTree, aim.Tree ? 1 : 0);
            zdo.Set(KSize, DefaultSize); zdo.Set(KColor, new Vector3(Look.RedOchre.r, Look.RedOchre.g, Look.RedOchre.b)); zdo.Set(KLayer, aimLayer);
            zdo.Set(KVersion, 1);
            zdo.SetPosition(aim.Center);
            transform.position = aim.Center;
            Plugin.Log.LogInfo($"Painted {Rune?.Name} at {aim.Center:F2} (layer {aimLayer}{(aim.Tree ? ", on a tree" : "")})");
        }

        private void Load()
        {
            var zdo = nview.GetZDO();
            if (zdo.GetInt(KVersion) == 0)
            {
                // Nothing to paint (placed where nothing could be traced): its owner takes it away again.
                if (nview.IsOwner())
                {
                    if (repeat) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$msg_potato_paint_repeat");
                    else if (placedWithoutAim) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$msg_potato_paint_nosurface");
                    ZNetScene.instance.Destroy(gameObject);
                }
                return;
            }
            Frame = new Frame { Center = zdo.GetVec3(KCenter, transform.position), Normal = zdo.GetVec3(KNormal, Vector3.up), Up = zdo.GetVec3(KUp, Vector3.forward), Tree = zdo.GetInt(KTree) != 0 };
            Size = zdo.GetFloat(KSize, DefaultSize);
            var c = zdo.GetVec3(KColor, new Vector3(Look.RedOchre.r, Look.RedOchre.g, Look.RedOchre.b));
            Col = new Color(c.x, c.y, c.z, 1f);
            Layer = zdo.GetInt(KLayer);
            HasData = true;
            if (ClearPile()) return;
            Rebuild("loaded");
        }

        private static bool Repeats(Paint p, Rune rune, Frame f, float size) =>
            p.Rune == rune && Vector3.Distance(p.Frame.Center, f.Center) < Mathf.Min(p.Size, size) * RepeatShare && Vector3.Dot(p.Frame.Normal, f.Normal) > 0.9f;

        // A pile of the same rune on one spot: the top one stays, the rest are taken away for everyone, and the one kept is
        // lowered to just above whatever else lies under it. True when this paint was taken away.
        private bool ClearPile()
        {
            var pile = Loaded.Where(p => p && p.HasData && Repeats(p, Rune, Frame, Size)).ToList();
            if (pile.Count < 2) return false;
            var keep = pile.OrderByDescending(p => p.Layer).First();
            var gone = new HashSet<Paint>(pile.Where(p => p != keep));
            // Lowered to just above the other paints under it, so paints above it stay above it.
            int layer = 0;
            foreach (var p in Loaded)
                if (p && p.HasData && !pile.Contains(p) && p.Layer < keep.Layer && Vector3.Distance(p.Frame.Center, keep.Frame.Center) < (p.Size + keep.Size) * 0.5f * 0.9f) layer = Mathf.Max(layer, p.Layer + 1);
            Plugin.Log.LogInfo($"{Rune.Name} at {Frame.Center:F2}: {pile.Count} of the same rune on this spot; kept the top one (layer {keep.Layer}, now {layer}), took away {gone.Count}");
            foreach (var p in gone) { p.HasData = false; p.Delete(); }
            if (keep.Layer != layer) keep.SetLayer(layer, "a pile on its spot was cleared");
            return gone.Contains(this);
        }

        private void SetLayer(int layer, string why)
        {
            if (!nview || !nview.IsValid()) return;
            nview.ClaimOwnership();
            nview.GetZDO().Set(KLayer, layer);
            Layer = layer;
            if (built != null) Rebuild(why);
        }

        private void Rebuild(string why)
        {
            built?.Clear();
            var sw = Stopwatch.StartNew();
            built = Trace.Build(Frame, Size, Lift, Look.Material(Rune), Col, false, "paint " + Rune.Art);
            nextCheck = Time.time + 2f + Random.value;
            visibleCheck = Time.time + 1.5f;
            Plugin.Log.LogInfo($"{Rune.Name} at {Frame.Center:F2}: {built.Sheets} sheets, {built.Hosts.Count} things under it, centre on {(built.CenterHost ? built.CenterHost.name : "nothing")} ({why}, {sw.ElapsedMilliseconds} ms); "
                + $"{built.Points - built.Missed} of {built.Points} points found a surface; close up: {(built.Report.Count > 0 ? string.Join("; ", built.Report) : "nothing")}{(built.OnGround ? "; grass left out under it" : "")}");
            Grass.Set(this, built.OnGround, Frame, Size);
        }

        private void Update()
        {
            if (ghost) { Preview(); return; }
            if (visibleCheck > 0f && Time.time >= visibleCheck && built != null)
            {
                // Test data (0.1.3: ground runes not seen): is each sheet drawn, and what is between the camera and it.
                visibleCheck = -1f;
                var cam = Utils.GetMainCamera();
                foreach (var o in built.Objects)
                {
                    var r = o ? o.GetComponent<Renderer>() : null;
                    if (!r) continue;
                    string between = "";
                    if (cam)
                    {
                        var c = r.bounds.center; var from = cam.transform.position; var d = c - from;
                        between = Physics.Raycast(from, d.normalized, out var hit, d.magnitude - 0.02f, Trace.Mask, QueryTriggerInteraction.Ignore)
                            ? $"; {hit.collider.name} is in front of it, {d.magnitude - hit.distance:0.00} m before it" : "; nothing solid in front of it";
                    }
                    Plugin.Log.LogInfo($"{Rune.Name} at {Frame.Center:F2}: sheet {o.name} on layer {LayerMask.LayerToName(o.layer)} under {(o.transform.parent ? o.transform.parent.name : "nothing")}, drawn {(r.isVisible ? "on screen" : "nowhere on screen")}, enabled {r.enabled}, active {o.activeInHierarchy}, bounds centre {r.bounds.center:F2} size {r.bounds.size:F2}, {(cam ? Vector3.Distance(cam.transform.position, r.bounds.center).ToString("0.0") + " m from the camera" : "no camera")}{between}");
                }
            }
            if (!HasData || Time.time < nextCheck) return;
            nextCheck = Time.time + 2f;
            // Rebuild when what lies under it changed: something loaded after it, or unloaded, or was replaced.
            if (!Trace.HostIds(Frame.Center, Size).SetEquals(built.Hosts)) Rebuild("what lies under it changed");
        }

        // The ghost: trace the paint where the crosshair points, at most ten times a second. The aim is the game's own
        // placement ray (Player.PieceRayTest: from the camera along its forward, 50 m, then within m_maxPlaceDistance of
        // the player's eye), not the line to the ghost: with snapping the game moves the ghost to a snap point, and a
        // line through it can miss everything (test 0.1.3: "the aim ray hit nothing" on the user's stone build).
        private void Preview()
        {
            var cam = Utils.GetMainCamera();
            if (!cam) return;
            var eye = cam.transform.position; var dir = cam.transform.forward;
            // Retraced when the aim moves (at most ten times a second), and twice a second when it doesn't, so new paint
            // nearby (layers) and pieces that load show up in the preview too.
            bool moved = (eye - lastEye).sqrMagnitude >= 1e-4f || Vector3.Angle(dir, lastDir) > 0.05f;
            if (Time.time - previewTime < (moved ? 0.1f : 0.5f)) return;
            lastEye = eye; lastDir = dir; previewTime = Time.time;
            preview?.Clear();
            // Grass is left out under the preview once the aim rests (a moving aim would remake the grass ten times a second).
            if (moved) Grass.Clear(this);
            bool was = aimValid;
            aimValid = Trace.Aim(eye, dir, 50f, DefaultSize, cam.transform.right, out aim, out var why);
            var player = Player.m_localPlayer;
            if (aimValid && player && Vector3.Distance(player.GetEyePoint(), aim.Center) > player.m_maxPlaceDistance)
            { aimValid = false; why = $"{Vector3.Distance(player.GetEyePoint(), aim.Center):0.0} m away, farther than you can build ({player.m_maxPlaceDistance:0.0} m)"; }
            aimTime = Time.time;
            // Logged when it changes only (test data: "no preview where I aimed", 0.1.2).
            if (!aimValid && (was || why != aimWhy)) Plugin.Log.LogInfo($"Preview of {Rune?.Name}: nothing to paint where you aim ({why})");
            else if (aimValid && !was) Plugin.Log.LogInfo($"Preview of {Rune?.Name}: on {aim.Center:F2}");
            aimWhy = why ?? "";
            if (!aimValid) { Grass.Clear(this); return; }
            aimLayer = LayerAt(aim.Center, DefaultSize, null);
            preview = Trace.Build(aim, DefaultSize, 0.004f + 0.002f * aimLayer, Look.Material(Rune), Look.RedOchre, true, "preview");
            if (!moved) Grass.Set(this, preview.OnGround, aim, DefaultSize);
        }

        // On top of every loaded paint it overlaps (centres closer than 0.9 x their mean size, probe test 13).
        private static int LayerAt(Vector3 center, float size, HashSet<Paint> leaveOut)
        {
            int layer = 0;
            foreach (var p in Loaded)
                if (p && p.HasData && (leaveOut == null || !leaveOut.Contains(p)) && Vector3.Distance(p.Frame.Center, center) < (p.Size + size) * 0.5f * 0.9f) layer = Mathf.Max(layer, p.Layer + 1);
            return layer;
        }

        private void OnDisable()
        {
            if (!ghost) return;
            preview?.Clear(); preview = null;
            Grass.Clear(this);
            lastEye = Vector3.positiveInfinity;
        }

        private void OnDestroy()
        {
            if (listed) Loaded.Remove(this);
            Grass.Clear(this);
            built?.Clear();
            preview?.Clear();
        }

        // Takes the paint away for everyone (the Rúnakefli's remove, or the piece or tree under it was destroyed).
        internal void Delete()
        {
            if (!nview || !nview.IsValid()) return;
            nview.ClaimOwnership();
            ZNetScene.instance.Destroy(gameObject);
        }
    }
}
