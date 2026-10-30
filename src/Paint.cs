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

        internal Rune Rune;
        internal Frame Frame; internal float Size; internal Color Col; internal int Layer;
        internal bool HasData;
        internal Transform CenterHost => built?.CenterHost;

        private ZNetView nview;
        private bool ghost, listed, placedWithoutAim;
        private Built built, preview;
        private float nextCheck, previewTime;
        private Vector3 previewAt = Vector3.positiveInfinity;

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
            if (!aimValid || Time.time - aimTime > 1f) { placedWithoutAim = true; return; }
            // The layer as it is now: the preview's can be stale when the aim hasn't moved since the last paint here
            // (two paints on one spot both landed on layer 0 in the first test).
            aimLayer = LayerAt(aim.Center, DefaultSize);
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
                    if (placedWithoutAim) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$msg_potato_paint_nosurface");
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
            Rebuild("loaded");
        }

        private void Rebuild(string why)
        {
            built?.Clear();
            var sw = Stopwatch.StartNew();
            built = Trace.Build(Frame, Size, Lift, Look.Material(Rune), Col, false, "paint " + Rune.Art);
            nextCheck = Time.time + 2f + Random.value;
            Plugin.Log.LogInfo($"{Rune.Name} at {Frame.Center:F2}: {built.Sheets} sheets, {built.Hosts.Count} things under it, centre on {(built.CenterHost ? built.CenterHost.name : "nothing")} ({why}, {sw.ElapsedMilliseconds} ms)");
        }

        private void Update()
        {
            if (ghost) { Preview(); return; }
            if (!HasData || Time.time < nextCheck) return;
            nextCheck = Time.time + 2f;
            // Rebuild when what lies under it changed: something loaded after it, or unloaded, or was replaced.
            if (!Trace.HostIds(Frame.Center, Size).SetEquals(built.Hosts)) Rebuild("what lies under it changed");
        }

        // The ghost: trace the paint where the game put the ghost (its own aim ray hit), at most ten times a second.
        private void Preview()
        {
            var cam = Utils.GetMainCamera();
            if (!cam) return;
            var target = transform.position;
            // Retraced when the aim moves (at most ten times a second), and twice a second when it doesn't, so new paint
            // nearby (layers) and pieces that load show up in the preview too.
            bool moved = (target - previewAt).sqrMagnitude >= 1e-4f;
            if (Time.time - previewTime < (moved ? 0.1f : 0.5f)) return;
            previewAt = target; previewTime = Time.time;
            preview?.Clear();
            var eye = cam.transform.position; var dir = target - eye; float dist = dir.magnitude;
            if (dist < 0.01f) return;
            aimValid = Trace.Aim(eye, dir / dist, dist + 1.5f, DefaultSize, cam.transform.right, out aim);
            aimTime = Time.time;
            if (!aimValid) return;
            aimLayer = LayerAt(aim.Center, DefaultSize);
            preview = Trace.Build(aim, DefaultSize, 0.004f + 0.002f * aimLayer, Look.Material(Rune), Look.RedOchre, true, "preview");
        }

        // On top of every loaded paint it overlaps (centres closer than 0.9 x their mean size, probe test 13).
        private static int LayerAt(Vector3 center, float size)
        {
            int layer = 0;
            foreach (var p in Loaded)
                if (p && p.HasData && Vector3.Distance(p.Frame.Center, center) < (p.Size + size) * 0.5f * 0.9f) layer = Mathf.Max(layer, p.Layer + 1);
            return layer;
        }

        private void OnDisable()
        {
            if (!ghost) return;
            preview?.Clear(); preview = null;
            previewAt = Vector3.positiveInfinity;
        }

        private void OnDestroy()
        {
            if (listed) Loaded.Remove(this);
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
