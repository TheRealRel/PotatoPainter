// PotatoPainter - runes and Norse symbols painted with the Rúnakefli onto walls, floors, rocks, trees and the ground.
// Paint lies on the surface as the game draws it (traced on every detail level), takes the world's light and shadow, and
// bends with the bark on trees. Findings behind it: claude/potatopainter-research.md and -research-2.md; plan:
// claude/potatopainter-plan.md.
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace PotatoPainter
{
    // Paints are pieces: the server and every player need the mod (no process filter, so dedicated servers load it and
    // know the pieces; a server deletes pieces it doesn't know).
    [BepInPlugin(Id, "PotatoPainter", Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "potato.painter", Version = "0.4.2";
        // Saved in worlds and inventories: never rename.
        public const string ToolPrefab = "potato_runakefli";
        public const string TablePrefab = "_PotatoRunakefliPieceTable";

        // The Rúnakefli's recipe (the user's, 2026-10-30), at the workbench.
        private static readonly (string item, int amount)[] Recipe =
            { ("HardAntler", 1), ("ElderBark", 5), ("Raspberry", 10), ("Blueberries", 10), ("WitheredBone", 3) };
        // What a rune costs (the user, 2026-10-30): Coal for the paint (soot and charcoal were real rune paints), and Resin
        // as well for an etched one, which glows. Given back when the Rúnakefli takes the rune away again.
        private static readonly (string item, int amount)[] PaintedCost = { ("Coal", 1) };
        private static readonly (string item, int amount)[] EtchedCost = { ("Coal", 1), ("Resin", 1) };
        // Set by the placement check when the spot was refused: the message to show for it.
        private static string refused;

        internal static ManualLogSource Log;
        internal static PieceTable Table;
        internal static readonly HashSet<Paint> Doomed = new HashSet<Paint>();
        internal static readonly AccessTools.FieldRef<Player, PieceTable> BuildPieces = AccessTools.FieldRefAccess<Player, PieceTable>("m_buildPieces");
        private static readonly Func<Player, bool> TakeInput = AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));

        private void Awake()
        {
            Log = Logger;
            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            PrefabManager.OnVanillaPrefabsAvailable += AddContent;
        }

        private static void AddContent()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddContent;
            var words = new Dictionary<string, string>
            {
                { "item_potato_runakefli", "Rúnakefli" },
                { "item_potato_runakefli_description", "A carved rune stick with pigment of berries, bark and bone. Paints runes onto walls, floors, rocks, trees and the ground, or etches them to glow. Middle mouse takes a rune away again." },
                { "msg_potato_paint_nosurface", "Nothing to paint on there" },
                { "msg_potato_paint_removed", "Paint removed" },
                { "msg_potato_paint_repeat", "Already painted there" },
                { "msg_potato_etch_nosurface", "Nothing to etch there" },
                { "msg_potato_etch_repeat", "Already etched there" },
                { "msg_potato_symbol_locked", "You haven't unlocked this symbol yet" },
            };
            foreach (var rune in Runes.All)
            {
                words[rune.NameToken] = Runes.Shown(rune);
                words[rune.DescriptionToken] = Runes.Describe(rune);
            }
            LocalizationManager.Instance.GetLocalization().AddTranslation("English", words);
            try { AddTable(); AddTool(); AddRunes(); }
            catch (Exception e) { Log.LogError("Couldn't add the Rúnakefli and its runes: " + e); }
        }

        // The Rúnakefli's own build menu. Its tabs come from each piece's Category (Runes and Symbols; piece tables don't
        // own categories since Valheim 1.0, per Jötunn). The game's remove is off for it: it removes the piece whose
        // collider the aim hits, and a paint has none, so it would take the wall under the paint (Player.RemovePiece).
        private static void AddTable()
        {
            var table = new CustomPieceTable(TablePrefab, new PieceTableConfig { CanRemovePieces = false });
            PieceManager.Instance.AddPieceTable(table);
            Table = table.PieceTable;
        }

        private static void AddTool()
        {
            var config = new ItemConfig
            {
                Name = "$item_potato_runakefli",
                Description = "$item_potato_runakefli_description",
                CraftingStation = "piece_workbench",
                PieceTable = TablePrefab,
            };
            foreach (var (item, amount) in Recipe)
            {
                if (PrefabManager.Instance.GetPrefab(item)) config.AddRequirement(new RequirementConfig(item, amount, 0, true));
                else Log.LogWarning($"The Rúnakefli's recipe item {item} wasn't found; it's left out of the recipe this session.");
            }
            var tool = new CustomItem(ToolPrefab, "Hammer", config);
            try { Tool.Dress(tool.ItemPrefab); Tool.Icon(tool.ItemDrop); }
            catch (Exception e) { Log.LogWarning("The Rúnakefli keeps the hammer's look this session: " + e); }
            ItemManager.Instance.AddItem(tool);
        }

        // Two pieces per drawn design, each kind in its own tab: painted, and etched (glows). The piece has no model and no
        // collider: Jötunn's empty prefab is a cube (box collider, mesh), so those go.
        private static void AddRunes()
        {
            int added = 0;
            foreach (var cost in PaintedCost.Concat(EtchedCost).Select(c => c.item).Distinct())
                if (!PrefabManager.Instance.GetPrefab(cost)) Log.LogWarning($"The runes' cost item {cost} wasn't found; it's left out of their cost this session.");
            foreach (var rune in Runes.All)
            {
                if (Runes.ArtBytes(rune) == null) continue;
                foreach (bool etched in new[] { false, true })
                {
                    var config = new PieceConfig
                    {
                        Name = "$" + rune.NameToken,
                        Description = "$" + rune.DescriptionToken,
                        PieceTable = TablePrefab,
                        Category = rune.Tab(etched),
                        AllowedInDungeons = false,
                    };
                    foreach (var (item, amount) in etched ? EtchedCost : PaintedCost)
                        if (PrefabManager.Instance.GetPrefab(item)) config.AddRequirement(new RequirementConfig(item, amount, 0, true));
                    if (!GUIManager.IsHeadless()) config.Icon = Look.Icon(rune, etched);
                    var piece = new CustomPiece(etched ? rune.EtchedPrefab : rune.Prefab, true, config);
                    var prefab = piece.PiecePrefab;
                    foreach (var c in prefab.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
                    foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true)) DestroyImmediate(r);
                    foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true)) DestroyImmediate(f);
                    var paint = prefab.AddComponent<Paint>();
                    paint.Art = rune.Art; paint.Etched = etched;
                    PieceManager.Instance.AddPiece(piece);
                }
                added++;
            }
            Log.LogInfo($"Rúnakefli ready: {added} of {Runes.All.Count} designs ({Runes.All.Count(r => r.Row != Runes.Symbols)} runes, {Runes.All.Count(r => r.Row == Runes.Symbols)} symbols), painted and etched.");
        }

        private void Update()
        {
            if (Doomed.Count > 0)
            {
                foreach (var p in Doomed.ToList()) if (p) p.Delete();
                Doomed.Clear();
            }
            var player = Player.m_localPlayer;
            if (!player || !Table || !player.InPlaceMode() || BuildPieces(player) != Table || !TakeInput(player)) return;
            if (ZInput.GetButtonDown("Remove") || ZInput.GetButtonDown("JoyRemove")) RemoveAimed(player);
        }

        private void LateUpdate()
        {
            var sw = Perf.Start(); Bend.Update(); Perf.Stop("bend", sw);
            sw = Perf.Start(); Ground.Update(); Perf.Stop("ground follow", sw, Ground.FrameVerts, Ground.FrameSheets);
            Panel.Update();
            Perf.Frame();
        }

        // The Rúnakefli's remove: the topmost paint where you aim, with the game's own no-build and ward checks.
        private static void RemoveAimed(Player player)
        {
            var cam = GameCamera.instance;
            if (!cam) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 50f, Trace.Mask, QueryTriggerInteraction.Ignore)) return;
            if (Vector3.Distance(hit.point, player.GetEyePoint()) > player.m_maxPlaceDistance) return;
            var p = hit.point;
            var target = Paint.Loaded
                .Where(x => x && x.HasData && (x.Frame.Center - p).magnitude <= x.Size * 0.6f && Mathf.Abs(Vector3.Dot(p - x.Frame.Center, x.Frame.Normal)) < 0.3f)
                .OrderByDescending(x => x.Layer).ThenBy(x => (x.Frame.Center - p).sqrMagnitude).FirstOrDefault();
            if (!target) return;
            var at = target.Frame.Center;
            if (Location.IsInsideNoBuildLocation(at)) { player.Message(MessageHud.MessageType.Center, "$msg_nobuildzone"); return; }
            if (!PrivateArea.CheckAccess(at, 0f, true, false)) { player.Message(MessageHud.MessageType.Center, "$msg_privatezone"); return; }
            var piece = target.GetComponent<Piece>();
            if (piece && !piece.CanBeRemoved()) { player.Message(MessageHud.MessageType.Center, "$msg_cantremovenow"); return; }
            // Its Coal and Resin back, as the hammer gives back a piece's (Player.RemovePiece: Piece.DropResources).
            if (piece) piece.DropResources();
            target.Delete();
            player.Message(MessageHud.MessageType.TopLeft, "$msg_potato_paint_removed");
        }

        // With the Rúnakefli: no placement marker (the game's yellow outline and arrow; the paint's own preview shows where
        // it goes), and the spot counts as invalid when nothing can be painted there or the same rune is already there, so
        // the game doesn't place it and takes nothing. UpdatePlacementGhost turns the marker on every frame it's valid and
        // sets m_placementStatus; TryPlacePiece calls it, then reads the status before placing (the cost is taken after).
        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        internal static class PlacementCheck
        {
            private static void Postfix(Player __instance, GameObject ___m_placementMarkerInstance, ref Player.PlacementStatus ___m_placementStatus)
            {
                if (!Table || BuildPieces(__instance) != Table) return;
                if (___m_placementMarkerInstance) ___m_placementMarkerInstance.SetActive(false);
                refused = null;
                if (___m_placementStatus != Player.PlacementStatus.Valid) return;
                // A symbol not unlocked yet can't be painted (the menu leaves it out; this covers a selection kept from before).
                if (!Unlock.Open(__instance.GetSelectedPiece(), __instance)) refused = "$msg_potato_symbol_locked";
                else refused = Paint.Refusal();
                if (refused != null) ___m_placementStatus = Player.PlacementStatus.Invalid;
            }
        }

        // No support colours with the Rúnakefli: in build mode the game tints the piece under the crosshair by how well it is
        // supported (Player.UpdateWearNTearHover: raycast, then WearNTear.Highlight), which means nothing for paint (the
        // user, 2026-10-30). Skipped while the Rúnakefli's table is active, leaving m_hoveringPiece null as the game does
        // outside build mode.
        [HarmonyPatch(typeof(Player), "UpdateWearNTearHover")]
        internal static class NoSupportColours
        {
            private static bool Prefix(Player __instance, ref Piece ___m_hoveringPiece)
            {
                if (!Table || BuildPieces(__instance) != Table) return true;
                ___m_hoveringPiece = null;
                return false;
            }
        }

        // A refused click says why, in place of the game's "Invalid placement".
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        internal static class RefusedMessage
        {
            private static void Postfix(Player __instance, bool __result)
            {
                if (!__result && refused != null && Table && BuildPieces(__instance) == Table) __instance.Message(MessageHud.MessageType.Center, refused);
            }
        }

        // A piece or tree being destroyed (removed, broken, cut; not just unloaded, which never goes through here): every
        // paint whose centre lies on it goes too, next frame.
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
        internal static class HostDestroyed
        {
            private static void Prefix(GameObject go)
            {
                if (!go || Paint.Loaded.Count == 0) return;
                var t = go.transform;
                foreach (var p in Paint.Loaded)
                    if (p && p.CenterHost && (p.CenterHost == t || p.CenterHost.IsChildOf(t))) Doomed.Add(p);
            }
        }
    }
}
