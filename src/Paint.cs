// One paint: a piece with no model and no collider whose ZDO holds where and how it lies. Every player (not the
// dedicated server) traces it onto the pieces, trees, rocks or ground under it when it loads, and again whenever what lies
// under it changes (a wall that loads after it, a tree that grows back). As the placement ghost it shows a preview.
using System.Collections.Generic;
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
        private Vector3 lastEye = Vector3.positiveInfinity, lastDir; private float lastTurn;
        private static readonly HarmonyLib.AccessTools.FieldRef<Player, int> PlaceRotation = HarmonyLib.AccessTools.FieldRefAccess<Player, int>("m_placeRotation");
        private static readonly HarmonyLib.AccessTools.FieldRef<Player, float> PlaceRotationDegrees = HarmonyLib.AccessTools.FieldRefAccess<Player, float>("m_placeRotationDegrees");
        private Vector3? ghostGrassAt;

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
                return;
            }
            if (Loaded.Any(p => p && p.HasData && Repeats(p, Rune, aim, DefaultSize)))
            {
                repeat = true;
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
            Rebuild();
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
            foreach (var p in gone) { p.HasData = false; p.Delete(); }
            if (keep.Layer != layer) keep.SetLayer(layer);
            return gone.Contains(this);
        }

        private void SetLayer(int layer)
        {
            if (!nview || !nview.IsValid()) return;
            nview.ClaimOwnership();
            nview.GetZDO().Set(KLayer, layer);
            Layer = layer;
            if (built != null) Rebuild();
        }

        private void Rebuild()
        {
            built?.Clear();
            built = Trace.Build(Frame, Size, Lift, Look.Material(Rune), Col, false, "paint " + Rune.Art);
            nextCheck = Time.time + 2f + Random.value;
            Grass.Set(this, built.OnGround, Frame, Size);
        }

        private void Update()
        {
            if (ghost) { Preview(); return; }
            if (!HasData || Time.time < nextCheck) return;
            nextCheck = Time.time + 2f;
            // Rebuild when what lies under it changed: something loaded after it, or unloaded, or was replaced.
            if (!Trace.HostIds(Frame.Center, Size).SetEquals(built.Hosts)) Rebuild();
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
            var player = Player.m_localPlayer;
            // The game's own placement rotation (mouse wheel), as it turns the ghost: m_placeRotation x m_placeRotationDegrees.
            float turn = player ? Mathf.Repeat(PlaceRotation(player) * PlaceRotationDegrees(player), 360f) : 0f;
            // Retraced when the aim moves (at most ten times a second), and twice a second when it doesn't, so new paint
            // nearby (layers) and pieces that load show up in the preview too.
            bool moved = (eye - lastEye).sqrMagnitude >= 1e-4f || Vector3.Angle(dir, lastDir) > 0.05f || turn != lastTurn;
            if (Time.time - previewTime < (moved ? 0.1f : 0.5f)) return;
            lastEye = eye; lastDir = dir; lastTurn = turn; previewTime = Time.time;
            preview?.Clear();
            // Grass is left out under the preview once the aim rests, and given back only when the aim leaves that spot
            // (test 0.1.7: the camera's own small movements gave it back and took it again twice a second, remaking four
            // grass patches each time).
            aimValid = Trace.Aim(eye, dir, 50f, DefaultSize, cam.transform.right, turn, out aim)
                && (!player || Vector3.Distance(player.GetEyePoint(), aim.Center) <= player.m_maxPlaceDistance);
            aimTime = Time.time;
            if (!aimValid) { Grass.Clear(this); ghostGrassAt = null; return; }
            if (ghostGrassAt.HasValue && Vector3.Distance(ghostGrassAt.Value, aim.Center) > 0.25f) { Grass.Clear(this); ghostGrassAt = null; }
            aimLayer = LayerAt(aim.Center, DefaultSize, null);
            preview = Trace.Build(aim, DefaultSize, 0.004f + 0.002f * aimLayer, Look.Material(Rune), Look.RedOchre, true, "preview");
            if (!moved && !ghostGrassAt.HasValue && preview.OnGround) { Grass.Set(this, true, aim, DefaultSize); ghostGrassAt = aim.Center; }
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
            Grass.Clear(this); ghostGrassAt = null;
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
