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
        public const string Id = "potato.painter", Version = "0.1.5";
        // Saved in worlds and inventories: never rename.
        public const string ToolPrefab = "potato_runakefli";
        public const string TablePrefab = "_PotatoRunakefliPieceTable";

        // The Rúnakefli's recipe (the user's, 2026-10-30), at the workbench.
        private static readonly (string item, int amount)[] Recipe =
            { ("HardAntler", 1), ("ElderBark", 5), ("Raspberry", 10), ("Blueberries", 10), ("WitheredBone", 3) };

        internal static ManualLogSource Log;
        internal static PieceTable Table;
        internal static readonly HashSet<Paint> Doomed = new HashSet<Paint>();
        private static readonly AccessTools.FieldRef<Player, PieceTable> BuildPieces = AccessTools.FieldRefAccess<Player, PieceTable>("m_buildPieces");
        private static readonly AccessTools.FieldRef<Player, GameObject> PlacementGhost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private static readonly System.Reflection.FieldInfo PlacementStatus = AccessTools.Field(typeof(Player), "m_placementStatus");
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
                { "item_potato_runakefli_description", "A carved rune stick with pigment of berries, bark and bone. Paints runes onto walls, floors, rocks, trees and the ground. Middle mouse takes a paint away again." },
                { "msg_potato_paint_nosurface", "Nothing to paint on there" },
                { "msg_potato_paint_removed", "Paint removed" },
                { "msg_potato_paint_repeat", "Already painted there" },
            };
            foreach (var rune in Runes.All)
            {
                words[rune.NameToken] = rune.Name;
                words[rune.DescriptionToken] = Runes.Describe(rune);
            }
            LocalizationManager.Instance.GetLocalization().AddTranslation("English", words);
            try { AddTable(); AddTool(); AddRunes(); }
            catch (Exception e) { Log.LogError("Couldn't add the Rúnakefli and its runes: " + e); }
        }

        // The Rúnakefli's own build menu. Its tabs come from each piece's Category (one per rune row; piece tables don't
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

        // One piece per drawn design, free to paint for now. The piece has no model and no collider: Jötunn's empty prefab
        // is a cube (box collider, mesh), so those go.
        private static void AddRunes()
        {
            int added = 0;
            foreach (var rune in Runes.All)
            {
                if (Runes.ArtBytes(rune) == null) continue;
                var config = new PieceConfig
                {
                    Name = "$" + rune.NameToken,
                    Description = "$" + rune.DescriptionToken,
                    PieceTable = TablePrefab,
                    Category = rune.Row,
                    AllowedInDungeons = false,
                };
                if (!GUIManager.IsHeadless()) config.Icon = Look.Icon(rune);
                var piece = new CustomPiece(rune.Prefab, true, config);
                var prefab = piece.PiecePrefab;
                foreach (var c in prefab.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true)) DestroyImmediate(r);
                foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true)) DestroyImmediate(f);
                prefab.AddComponent<Paint>().Art = rune.Art;
                PieceManager.Instance.AddPiece(piece);
                added++;
            }
            Log.LogInfo($"Rúnakefli ready: {added} of {Runes.All.Count} runes drawn so far.");
        }

        private void Update()
        {
            if (Doomed.Count > 0)
            {
                foreach (var p in Doomed.ToList()) if (p) { Log.LogInfo($"{p.Rune?.Name} at {p.Frame.Center:F2}: what it was painted on was destroyed, so it goes too"); p.Delete(); }
                Doomed.Clear();
            }
            var player = Player.m_localPlayer;
            if (!player || !Table || !player.InPlaceMode() || BuildPieces(player) != Table || !TakeInput(player)) return;
            if (ZInput.GetButtonDown("Remove") || ZInput.GetButtonDown("JoyRemove")) RemoveAimed(player);
            // Test data (0.1.2: "clicks did nothing"): what the game thought of the spot on each click with the Rúnakefli.
            if (ZInput.GetButtonDown("Attack") || ZInput.GetButtonDown("JoyPlace"))
            {
                var ghost = PlacementGhost(player);
                Log.LogInfo($"Click with the Rúnakefli: the game's placement is {PlacementStatus.GetValue(player)}, ghost {(ghost ? (ghost.activeSelf ? "shown" : "hidden") + " at " + ghost.transform.position.ToString("F2") : "none")}");
            }
        }

        private void LateUpdate() => Bend.Update();

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
            Log.LogInfo($"{target.Rune?.Name} at {at:F2}: removed with the Rúnakefli");
            target.Delete();
            player.Message(MessageHud.MessageType.TopLeft, "$msg_potato_paint_removed");
        }

        // No placement marker (the game's yellow outline and arrow) with the Rúnakefli: the paint's own preview shows where
        // it goes. UpdatePlacementGhost turns the marker on every frame it's valid, so it's hidden after it, each frame.
        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        internal static class NoPlacementMarker
        {
            private static void Postfix(Player __instance, GameObject ___m_placementMarkerInstance)
            {
                if (___m_placementMarkerInstance && Table && BuildPieces(__instance) == Table) ___m_placementMarkerInstance.SetActive(false);
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
