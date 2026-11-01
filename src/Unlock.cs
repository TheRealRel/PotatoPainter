// Symbols a boss unlocks (agreed with the user 2026-10-31). A symbol is open once the world has the boss's defeat key (set
// for the world when the boss dies, Character.OnDeath -> ZoneSystem.SetGlobalKey, synced to every player) or the character
// has it (the same death adds it to the killer's own keys, Player.AddUniqueKey, saved with the character), so a new
// character on a world where the boss is down has it, and so does a character who killed it on another world. The key
// names are read from the boss prefabs (Character.m_defeatSetGlobalKey), not written here.
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace PotatoPainter
{
    internal static class Unlock
    {
        private static readonly Dictionary<string, string> keys = new Dictionary<string, string>();
        private static readonly HashSet<string> warned = new HashSet<string>();
        private static string lastLogged;

        // The boss's defeat key, or null when its prefab or key can't be found (then the symbol stays open: logged once).
        internal static string KeyFor(string boss)
        {
            if (keys.TryGetValue(boss, out var key)) return key;
            var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(boss) : null;
            if (!prefab) prefab = PrefabManager.Instance.GetPrefab(boss);
            var c = prefab ? prefab.GetComponent<Character>() : null;
            key = c && !string.IsNullOrEmpty(c.m_defeatSetGlobalKey) ? c.m_defeatSetGlobalKey : null;
            if (key != null) keys[boss] = key;
            else if (warned.Add(boss)) Plugin.Log.LogWarning($"Couldn't read {boss}'s defeat key; the symbols it unlocks are open this session.");
            return key;
        }

        internal static bool Open(Rune rune, Player player)
        {
            if (rune == null || rune.Boss == null) return true;
            var key = KeyFor(rune.Boss);
            if (key == null) return true;
            if (ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(key)) return true;
            return player && player.HaveUniqueKey(key);
        }

        internal static bool Open(Piece piece, Player player)
        {
            var paint = piece ? piece.GetComponent<Paint>() : null;
            return !paint || Open(Runes.All.FirstOrDefault(r => r.Art == paint.Art), player);
        }

        // The Rúnakefli's build menu leaves out symbols not unlocked yet: after the game works out which pieces are available
        // (Player.UpdateAvailablePiecesList -> PieceTable.UpdateAvailable, which with no-cost mode lists every piece), the
        // locked ones are taken out of its lists again.
        [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
        internal static class HideLocked
        {
            // Valheim 1.0: m_availablePieces and m_enabledPieces are public HashSet<Piece>s, used directly (0.4.2 looked
            // m_availablePieces up by name, which kept Valheim10Compatibility from adding its pre-1.0 alias of that name);
            // the per-tab lists are private, List<List<Piece>>.
            private static readonly AccessTools.FieldRef<PieceTable, List<List<Piece>>> ByCategory = AccessTools.FieldRefAccess<PieceTable, List<List<Piece>>>("m_availablePiecesByCategory");

            // __1: the player (UpdateAvailable(knownRecipes, player, hideUnavailable, noPlacementCost)).
            private static void Postfix(PieceTable __instance, Player __1)
            {
                if (!Plugin.Table || __instance != Plugin.Table) return;
                var locked = new List<Piece>(); var open = new List<string>();
                foreach (var go in __instance.m_pieces)
                {
                    var piece = go ? go.GetComponent<Piece>() : null;
                    var paint = piece ? piece.GetComponent<Paint>() : null;
                    var rune = paint ? Runes.All.FirstOrDefault(r => r.Art == paint.Art) : null;
                    if (rune == null || rune.Boss == null) continue;
                    if (Open(rune, __1)) { if (!paint.Etched) open.Add(rune.Name); }
                    else locked.Add(piece);
                }
                foreach (var piece in locked)
                {
                    __instance.m_availablePieces.Remove(piece);
                    __instance.m_enabledPieces.Remove(piece);
                    var lists = ByCategory(__instance);
                    if (lists != null) foreach (var list in lists) list?.Remove(piece);
                }
                // Test-only (0.4.0): which symbols are open, logged when it changes. Take out before release.
                var line = $"Symbols unlocked by bosses: {open.Count} ({string.Join(", ", open)}); {locked.Count / 2} still locked.";
                if (line != lastLogged) { lastLogged = line; Plugin.Log.LogInfo(line); }
            }
        }
    }
}
