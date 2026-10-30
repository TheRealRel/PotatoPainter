// The Rúnakefli's designs, organized as in claude/potatopainter-runes.md: by row, in the row's own order, by ætt.
// A design is only offered once its drawing exists (assets/runes/<Art>.png, embedded in the DLL).
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace PotatoPainter
{
    internal sealed class Rune
    {
        internal string Row, Aett, Name, Sound, Letter, Art;
        internal int Position;
        // Saved in worlds (one piece prefab per design): never rename.
        internal string Prefab => "potato_paint_" + Art;
        internal string NameToken => "piece_" + Prefab;
        internal string DescriptionToken => "piece_" + Prefab + "_description";
    }

    internal static class Runes
    {
        internal const string Elder = "Elder Futhark";

        // Elder Futhark, Kylver Stone order: three ættir of eight, each named after its first rune.
        private static readonly (string aett, string name, string sound, string letter, string art)[] ElderRunes =
        {
            ("f", "Fehu", "f", "ᚠ", "elder_fehu"), ("f", "Ūruz", "u", "ᚢ", "elder_uruz"), ("f", "Þurisaz", "þ", "ᚦ", "elder_thurisaz"),
            ("f", "Ansuz", "a", "ᚨ", "elder_ansuz"), ("f", "Raidō", "r", "ᚱ", "elder_raido"), ("f", "Kauną", "k", "ᚲ", "elder_kauna"),
            ("f", "Gebō", "g", "ᚷ", "elder_gebo"), ("f", "Wunjō", "w", "ᚹ", "elder_wunjo"),
            ("h", "Hagalaz", "h", "ᚺ", "elder_hagalaz"), ("h", "Naudiz", "n", "ᚾ", "elder_naudiz"), ("h", "Īsaz", "i", "ᛁ", "elder_isaz"),
            ("h", "Jēra", "j", "ᛃ", "elder_jera"), ("h", "Ī(h)waz", "ï", "ᛇ", "elder_ihwaz"), ("h", "Perþō", "p", "ᛈ", "elder_pertho"),
            ("h", "Algiz", "z", "ᛉ", "elder_algiz"), ("h", "Sōwilō", "s", "ᛊ", "elder_sowilo"),
            ("t", "Tīwaz", "t", "ᛏ", "elder_tiwaz"), ("t", "Berkanan", "b", "ᛒ", "elder_berkanan"), ("t", "Ehwaz", "e", "ᛖ", "elder_ehwaz"),
            ("t", "Mannaz", "m", "ᛗ", "elder_mannaz"), ("t", "Laguz", "l", "ᛚ", "elder_laguz"), ("t", "Ingwaz", "ŋ", "ᛜ", "elder_ingwaz"),
            ("t", "Dagaz", "d", "ᛞ", "elder_dagaz"), ("t", "Ōþala", "o", "ᛟ", "elder_othala"),
        };

        internal static readonly List<Rune> All = new List<Rune>();

        static Runes()
        {
            for (int i = 0; i < ElderRunes.Length; i++)
            {
                var r = ElderRunes[i];
                All.Add(new Rune { Row = Elder, Aett = r.aett, Position = i + 1, Name = r.name, Sound = r.sound, Letter = r.letter, Art = r.art });
            }
        }

        // The drawing's PNG bytes, or null while it isn't drawn yet.
        internal static byte[] ArtBytes(Rune rune)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("runes." + rune.Art + ".png"))
            {
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        internal static string Describe(Rune rune) =>
            $"{rune.Row}, {rune.Aett} ætt, rune {rune.Position}. Sound: {rune.Sound}. Painted where you aim, on walls, floors, rocks, trees and the ground.";
    }
}
