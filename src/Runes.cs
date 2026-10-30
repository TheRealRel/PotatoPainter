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
        internal string Row, Aett, Name, Sound, Letter, Art, Meaning;
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
        // Meanings: the reconstructed Proto-Germanic names' meanings as Wikipedia's Elder Futhark table gives them (read
        // 2026-10-30 through a copy of that table), shortened.
        private static readonly (string aett, string name, string sound, string letter, string art, string meaning)[] ElderRunes =
        {
            ("f", "Fehu", "f", "ᚠ", "elder_fehu", "Cattle, wealth"), ("f", "Ūruz", "u", "ᚢ", "elder_uruz", "Aurochs"), ("f", "Þurisaz", "þ", "ᚦ", "elder_thurisaz", "Giant"),
            ("f", "Ansuz", "a", "ᚨ", "elder_ansuz", "God"), ("f", "Raidō", "r", "ᚱ", "elder_raido", "Ride, journey"), ("f", "Kauną", "k", "ᚲ", "elder_kauna", "Ulcer, or torch"),
            ("f", "Gebō", "g", "ᚷ", "elder_gebo", "Gift"), ("f", "Wunjō", "w", "ᚹ", "elder_wunjo", "Joy"),
            ("h", "Hagalaz", "h", "ᚺ", "elder_hagalaz", "Hail"), ("h", "Naudiz", "n", "ᚾ", "elder_naudiz", "Need"), ("h", "Īsaz", "i", "ᛁ", "elder_isaz", "Ice"),
            ("h", "Jēra", "j", "ᛃ", "elder_jera", "Year, harvest"), ("h", "Ī(h)waz", "ï", "ᛇ", "elder_ihwaz", "Yew tree"), ("h", "Perþō", "p", "ᛈ", "elder_pertho", "Meaning unknown, perhaps pear tree"),
            ("h", "Algiz", "z", "ᛉ", "elder_algiz", "Elk, or protection"), ("h", "Sōwilō", "s", "ᛊ", "elder_sowilo", "Sun"),
            ("t", "Tīwaz", "t", "ᛏ", "elder_tiwaz", "The god Tīwaz"), ("t", "Berkanan", "b", "ᛒ", "elder_berkanan", "Birch"), ("t", "Ehwaz", "e", "ᛖ", "elder_ehwaz", "Horse"),
            ("t", "Mannaz", "m", "ᛗ", "elder_mannaz", "Man"), ("t", "Laguz", "l", "ᛚ", "elder_laguz", "Water, lake"), ("t", "Ingwaz", "ŋ", "ᛜ", "elder_ingwaz", "The god Ingwaz"),
            ("t", "Dagaz", "d", "ᛞ", "elder_dagaz", "Day"), ("t", "Ōþala", "o", "ᛟ", "elder_othala", "Heritage, estate"),
        };

        internal static readonly List<Rune> All = new List<Rune>();

        static Runes()
        {
            for (int i = 0; i < ElderRunes.Length; i++)
            {
                var r = ElderRunes[i];
                All.Add(new Rune { Row = Elder, Aett = r.aett, Position = i + 1, Name = r.name, Sound = r.sound, Letter = r.letter, Art = r.art, Meaning = r.meaning });
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

        // The build menu's line under the name: the rune's meaning, nothing else (the user, 2026-10-30).
        internal static string Describe(Rune rune) => rune.Meaning;
    }
}
