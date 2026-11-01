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
        // Set on each design's prefab; copied to every placed paint and ghost. Etched: the glowing kind (its own prefab).
        public string Art;
        public bool Etched;

        internal const float DefaultSize = 0.5f;
        internal static readonly List<Paint> Loaded = new List<Paint>();

        // Saved in worlds: never rename.
        private static readonly int KVersion = "PotatoPainter.Version".GetStableHashCode(), KCenter = "PotatoPainter.Center".GetStableHashCode(),
            KNormal = "PotatoPainter.Normal".GetStableHashCode(), KUp = "PotatoPainter.Up".GetStableHashCode(), KTree = "PotatoPainter.Tree".GetStableHashCode(),
            KSize = "PotatoPainter.Size".GetStableHashCode(), KColor = "PotatoPainter.Color".GetStableHashCode(), KLayer = "PotatoPainter.Layer".GetStableHashCode(),
            KGlow = "PotatoPainter.Glow".GetStableHashCode(), KLight = "PotatoPainter.Light".GetStableHashCode(),
            KGlowShare = "PotatoPainter.GlowShare".GetStableHashCode();

        // The placement ghost's latest aim, read by OnPlaced on the same client right after the game places the piece.
        private static Frame aim; private static bool aimValid; private static float aimTime; private static int aimLayer;
        private static readonly HashSet<string> logged = new HashSet<string>();
        private static float previewCost;   // seconds the last preview trace took
        private static Rune aimRune; private static bool aimEtched; private static float aimSize = DefaultSize; private static Color aimCol, aimGlow; private static bool aimLit;

        // Same rune on the same spot (centres closer than this share of its size, facing the same way): a repeat. Repeats
        // are left out when painting, and piles of them already in the world (test 0.1.2: 5 to 30 per spot, painted while
        // the grass hid them) are cut down to the top one when they load (the user, 2026-10-30).
        private const float RepeatShare = 0.15f;

        internal Rune Rune;
        internal Frame Frame; internal float Size; internal Color Col, Glow; internal int Layer;
        internal bool Lit;   // an etched paint's own light (RuneLight); etched paints from before 0.4.3 have it on
        // Glow: the kept value is a share of the colour's cap (0.4.4 on) or an absolute strength (before).
        internal bool GlowShare;
        internal Color Emission => Etched ? GlowCap.Emission(Glow, GlowShare) : Color.black;
        internal bool HasData;
        internal Transform CenterHost => built?.CenterHost;

        private ZNetView nview;
        private bool ghost, listed, placedWithoutAim, repeat, placedNow;
        private Built built, preview;
        internal Built Drawn => built;
        private Light previewLight;   // the ghost's one light, moved with the preview (no LightLod: it would fade in on every retrace)
        // Test-only (GlowTest): the ghost's preview stays off while the glow test reads the screen.
        internal static bool HidePreview;
        private float nextCheck, previewTime;
        private Vector3 lastEye = Vector3.positiveInfinity, lastDir; private float lastTurn; private int lastSettings = -1;
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
            if (Loaded.Any(p => p && p.HasData && Repeats(p, Rune, Etched, aim, aimSize, aimCol, aimGlow, aimLit, true)))
            {
                repeat = true;
                return;
            }
            // The layer as it is now: the preview's can be stale when the aim hasn't moved since the last paint here
            // (two paints on one spot both landed on layer 0 in the first test).
            aimLayer = LayerAt(aim.Center, aimSize, null);
            var zdo = nview.GetZDO();
            zdo.Set(KCenter, aim.Center); zdo.Set(KNormal, aim.Normal); zdo.Set(KUp, aim.Up); zdo.Set(KTree, aim.Tree ? 1 : 0);
            zdo.Set(KSize, aimSize); zdo.Set(KColor, new Vector3(aimCol.r, aimCol.g, aimCol.b)); zdo.Set(KLayer, aimLayer);
            if (Etched) { zdo.Set(KGlow, new Vector3(aimGlow.r, aimGlow.g, aimGlow.b)); zdo.Set(KGlowShare, 1); zdo.Set(KLight, aimLit ? 1 : 2); }
            zdo.Set(KVersion, 1);
            placedNow = true;
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
                    if (repeat) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, Etched ? "$msg_potato_etch_repeat" : "$msg_potato_paint_repeat");
                    else if (placedWithoutAim) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, Etched ? "$msg_potato_etch_nosurface" : "$msg_potato_paint_nosurface");
                    ZNetScene.instance.Destroy(gameObject);
                }
                return;
            }
            Frame = new Frame { Center = zdo.GetVec3(KCenter, transform.position), Normal = zdo.GetVec3(KNormal, Vector3.up), Up = zdo.GetVec3(KUp, Vector3.forward), Tree = zdo.GetInt(KTree) != 0 };
            Size = zdo.GetFloat(KSize, DefaultSize);
            var c = zdo.GetVec3(KColor, new Vector3(Look.RedOchre.r, Look.RedOchre.g, Look.RedOchre.b));
            Col = new Color(c.x, c.y, c.z, 1f);
            // Etched runes from before the panel (0.2.0) glow ember at strength 1.
            var g = Etched ? zdo.GetVec3(KGlow, new Vector3(Look.Ember.r, Look.Ember.g, Look.Ember.b)) : Vector3.zero;
            Glow = new Color(g.x, g.y, g.z, 1f);
            // 1 on, 2 off; not set (before 0.4.3): on.
            Lit = Etched && zdo.GetInt(KLight, 1) != 2;
            GlowShare = Etched && zdo.GetInt(KGlowShare) == 1;
            Layer = zdo.GetInt(KLayer);
            HasData = true;
            if (ClearPile()) return;
            Rebuild();
            // Test-only (0.4.3): every etched rune placed here starts the glow test on it.
            if (placedNow && Etched) GlowTest.Run(this);
        }

        // Why the game must not place the selected rune where the ghost's preview is right now, or null when it may (read
        // by Plugin's placement check, so a click that can't paint takes nothing).
        internal static string Refusal()
        {
            if (!aimValid || Time.time - aimTime > 1f) return aimEtched ? "$msg_potato_etch_nosurface" : "$msg_potato_paint_nosurface";
            if (aimRune != null && Loaded.Any(p => p && p.HasData && Repeats(p, aimRune, aimEtched, aim, aimSize, aimCol, aimGlow, aimLit, true))) return aimEtched ? "$msg_potato_etch_repeat" : "$msg_potato_paint_repeat";
            return null;
        }

        // The same rune of the same kind, colour, glow, light and size on the same spot. A new colour, size or light over an
        // old one is allowed: it goes on top.
        private static bool Repeats(Paint p, Rune rune, bool etched, Frame f, float size, Color col, Color glow, bool lit, bool share) =>
            p.Rune == rune && p.Etched == etched && Mathf.Abs(p.Size - size) < 0.01f && Near(p.Col, col) && Near(p.Glow, glow) && p.Lit == lit && (!etched || p.GlowShare == share)
            && Vector3.Distance(p.Frame.Center, f.Center) < Mathf.Min(p.Size, size) * RepeatShare && Vector3.Dot(p.Frame.Normal, f.Normal) > 0.9f;

        private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        // A pile of the same rune on one spot: the top one stays, the rest are taken away for everyone, and the one kept is
        // lowered to just above whatever else lies under it. True when this paint was taken away.
        private bool ClearPile()
        {
            var pile = Loaded.Where(p => p && p.HasData && Repeats(p, Rune, Etched, Frame, Size, Col, Glow, Lit, GlowShare)).ToList();
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
            var sw = Perf.Start();
            built = Trace.Build(Frame, Size, Lift, Look.Material(Rune, Etched), Etched ? Look.HaloFor(Rune) : null, Col, Emission, false, (Etched ? "etch " : "paint ") + Rune.Art);
            Perf.Stop("paint trace size " + Size.ToString("0.0"), sw, built.Sheets);
            if (Etched && Lit && built.Sheets > 0)
            {
                built.Light = RuneLight.Make(Frame, Size, Emission, built.CenterHost, "etch " + Rune.Art);
                if (built.Light) built.Objects.Add(built.Light);
            }
            nextCheck = Time.time + 2f + Random.value;
            Grass.Set(this, built.OnGround, Frame, Size);
        }

        // Test-only (ShadowTest, 0.4.7): the rune light made again, with or without shadows. True when one was made.
        internal bool RemakeLight()
        {
            if (built == null || !Etched || !Lit || built.Sheets == 0) return false;
            if (built.Light) { built.Objects.Remove(built.Light); Destroy(built.Light); }
            built.Light = RuneLight.Make(Frame, Size, Emission, built.CenterHost, "etch " + Rune.Art);
            if (built.Light) built.Objects.Add(built.Light);
            return built.Light;
        }

        // The glow drawn again at its cap for the bloom setting now (GlowCap.Update). True when something was redrawn.
        internal bool Retint()
        {
            if (built == null || !Etched) return false;
            var glow = Emission;
            foreach (var r in built.Strokes) if (r) Look.Colour(r, Col, glow);
            foreach (var h in built.Halos) if (h) Look.HaloColour(h, glow);
            if (built.Light) RuneLight.Tint(built.Light.GetComponent<Light>(), glow);
            return true;
        }

        private void Update()
        {
            if (ghost) { Preview(); return; }
            if (!HasData || Time.time < nextCheck) return;
            nextCheck = Time.time + 2f;
            // Rebuild when what lies under it changed: something loaded after it, or unloaded, or was replaced.
            var hc = Perf.Start(); bool changed = !Trace.HostIds(Frame.Center, Size).SetEquals(built.Hosts); Perf.Stop("host check", hc);
            if (changed) Rebuild();
        }

        // The ghost: trace the paint where the crosshair points, at most ten times a second. The aim is the game's own
        // placement ray (Player.PieceRayTest: from the camera along its forward, 50 m, then within m_maxPlaceDistance of
        // the player's eye), not the line to the ghost: with snapping the game moves the ghost to a snap point, and a
        // line through it can miss everything (test 0.1.3: "the aim ray hit nothing" on the user's stone build).
        private void Preview()
        {
            if (HidePreview)
            {
                if (preview != null) { preview.Clear(); preview = null; }
                if (previewLight) previewLight.enabled = false;
                lastSettings = -1;
                return;
            }
            var cam = Utils.GetMainCamera();
            if (!cam) return;
            var eye = cam.transform.position; var dir = cam.transform.forward;
            var player = Player.m_localPlayer;
            // The game's own placement rotation (mouse wheel), as it turns the ghost: m_placeRotation x m_placeRotationDegrees.
            float turn = player ? Mathf.Repeat(PlaceRotation(player) * PlaceRotationDegrees(player), 360f) : 0f;
            // Retraced when the aim moves (at most ten times a second), and twice a second when it doesn't, so new paint
            // nearby (layers) and pieces that load show up in the preview too.
            bool moved = (eye - lastEye).sqrMagnitude >= 1e-4f || Vector3.Angle(dir, lastDir) > 0.05f || turn != lastTurn || Settings.Version != lastSettings;
            // A slow trace (a large paint on detailed surfaces) waits at least four times as long as it took before the next one
            // (0.4.2), so the preview can't eat most of the frames.
            if (Time.time - previewTime < Mathf.Max(moved ? 0.1f : 0.5f, previewCost * 4f)) return;
            lastEye = eye; lastDir = dir; lastTurn = turn; lastSettings = Settings.Version; previewTime = Time.time;
            // The panel's settings for this kind (kept until changed).
            float size = Settings.Size; var col = Settings.Colour(Etched); var kept = Settings.KeptFor(Etched); var glow = Settings.GlowFor(Etched);
            preview?.Clear();
            // Grass is left out under the preview once the aim rests, and given back only when the aim leaves that spot
            // (test 0.1.7: the camera's own small movements gave it back and took it again twice a second, remaking four
            // grass patches each time).
            aimValid = Trace.Aim(eye, dir, 50f, size, cam.transform.right, turn, out aim)
                && (!player || Vector3.Distance(player.GetEyePoint(), aim.Center) <= player.m_maxPlaceDistance);
            aimTime = Time.time; aimRune = Rune; aimEtched = Etched; aimSize = size; aimCol = col; aimGlow = kept; aimLit = Etched && Settings.Light;
            // Test data (0.2.2: "some trees aren't registering"): each object and reason once. Taken out before release.
            if (!aimValid && Trace.WhyHit)
            {
                var key = Trace.RootOf(Trace.WhyHit).name + "|" + (Trace.Why ?? "farther than you can build");
                if (logged.Add(key)) Plugin.Log.LogInfo($"Nothing to paint where you aim: {Trace.Why ?? "farther than you can build"}; {Trace.Describe(Trace.WhyHit)}");
            }
            if (!aimValid) { Grass.Clear(this); ghostGrassAt = null; if (previewLight) previewLight.enabled = false; return; }
            if (ghostGrassAt.HasValue && Vector3.Distance(ghostGrassAt.Value, aim.Center) > 0.25f) { Grass.Clear(this); ghostGrassAt = null; }
            aimLayer = LayerAt(aim.Center, size, null);
            var sw = Perf.Start();
            preview = Trace.Build(aim, size, 0.004f + 0.002f * aimLayer, Look.Material(Rune, Etched), Etched ? Look.HaloFor(Rune) : null, col, glow, true, "preview");
            previewCost = (float)sw.Elapsed.TotalSeconds;
            Perf.Stop("preview trace size " + size.ToString("0.0"), sw, preview.Sheets);
            // The rune's light in the preview too, so it shows what will be placed.
            // Not when the same lit rune already lies right there (just placed, the aim unmoved): its own light shows it.
            bool lit = aimLit && preview.Sheets > 0 && glow.maxColorComponent > 0f
                && !Loaded.Any(p => p && p.HasData && Repeats(p, Rune, Etched, aim, size, col, kept, true, true));
            if (lit && !previewLight)
            {
                var lg = new GameObject(Trace.Prefix + "preview light");
                previewLight = lg.AddComponent<Light>();
            }
            if (previewLight)
            {
                previewLight.enabled = lit;
                if (lit) { RuneLight.Setup(previewLight, size, glow); previewLight.transform.position = aim.Center + aim.Normal * RuneLight.Off; }
            }
            if (!moved && !ghostGrassAt.HasValue && preview.OnGround) { Grass.Set(this, true, aim, size); ghostGrassAt = aim.Center; }
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
            aimValid = false;
            preview?.Clear(); preview = null;
            if (previewLight) previewLight.enabled = false;
            Grass.Clear(this); ghostGrassAt = null;
            lastEye = Vector3.positiveInfinity;
        }

        private void OnDestroy()
        {
            if (listed) Loaded.Remove(this);
            Grass.Clear(this);
            built?.Clear();
            preview?.Clear();
            if (previewLight) Destroy(previewLight.gameObject);
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
