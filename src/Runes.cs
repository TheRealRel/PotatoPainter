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
        internal string Row, Aett, Name, Sound, Letter, Art, Meaning, Note;
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
        // 2026-10-30 through a copy of that table), shortened. Notes: what each rune stood for, from the medieval rune poems as
        // norse-mythology.org's "The Meanings of the Runes" sums them up (read 2026-10-30), reworded. Īsaz and Perþō, which
        // that page leaves open, take the most common reading (the user asked for our best interpretation): Īsaz, ice the
        // poems call cold, slippery and "the broad bridge", read as stillness; Perþō, which the Old English poem calls
        // play and laughter in the beer hall, read as a lot or dice cup, so chance and play (Wikipedia's Isaz and Peorð
        // pages and modern rune books, 2026-10-30).
        private static readonly (string aett, string name, string sound, string letter, string art, string meaning, string note)[] ElderRunes =
        {
            ("f", "Fehu", "f", "ᚠ", "elder_fehu", "Cattle, wealth", "Stands for riches."), ("f", "Ūruz", "u", "ᚢ", "elder_uruz", "Aurochs", "Stands for strength of will."), ("f", "Þurisaz", "þ", "ᚦ", "elder_thurisaz", "Giant", "Stands for danger and hardship."),
            ("f", "Ansuz", "a", "ᚨ", "elder_ansuz", "God", "Stands for vitality."), ("f", "Raidō", "r", "ᚱ", "elder_raido", "Ride, journey", "Stands for travel and effort."), ("f", "Kauną", "k", "ᚲ", "elder_kauna", "Ulcer, or torch", "Stands for pain."),
            ("f", "Gebō", "g", "ᚷ", "elder_gebo", "Gift", "Stands for generosity."), ("f", "Wunjō", "w", "ᚹ", "elder_wunjo", "Joy", "Stands for delight."),
            ("h", "Hagalaz", "h", "ᚺ", "elder_hagalaz", "Hail", "Stands for ruin and disorder."), ("h", "Naudiz", "n", "ᚾ", "elder_naudiz", "Need", "Stands for want."), ("h", "Īsaz", "i", "ᛁ", "elder_isaz", "Ice", "Stands for stillness."),
            ("h", "Jēra", "j", "ᛃ", "elder_jera", "Year, harvest", "Stands for harvest and reward."), ("h", "Ī(h)waz", "ï", "ᛇ", "elder_ihwaz", "Yew tree", "Stands for strength and steadiness."), ("h", "Perþō", "p", "ᛈ", "elder_pertho", "Meaning unknown, perhaps pear tree", "Stands for chance and play."),
            ("h", "Algiz", "z", "ᛉ", "elder_algiz", "Elk, or protection", "Stands for protection."), ("h", "Sōwilō", "s", "ᛊ", "elder_sowilo", "Sun", "Stands for success and comfort."),
            ("t", "Tīwaz", "t", "ᛏ", "elder_tiwaz", "The god Tīwaz", "Stands for victory and honour."), ("t", "Berkanan", "b", "ᛒ", "elder_berkanan", "Birch", "Stands for growth and fertility."), ("t", "Ehwaz", "e", "ᛖ", "elder_ehwaz", "Horse", "Stands for trust and companionship."),
            ("t", "Mannaz", "m", "ᛗ", "elder_mannaz", "Man", "Stands for support."), ("t", "Laguz", "l", "ᛚ", "elder_laguz", "Water, lake", "Stands for the unknown."), ("t", "Ingwaz", "ŋ", "ᛜ", "elder_ingwaz", "The god Ingwaz", "Stands for new beginnings."),
            ("t", "Dagaz", "d", "ᛞ", "elder_dagaz", "Day", "Stands for hope and happiness."), ("t", "Ōþala", "o", "ᛟ", "elder_othala", "Heritage, estate", "Stands for heritage and tradition."),
        };

        internal static readonly List<Rune> All = new List<Rune>();

        static Runes()
        {
            for (int i = 0; i < ElderRunes.Length; i++)
            {
                var r = ElderRunes[i];
                All.Add(new Rune { Row = Elder, Aett = r.aett, Position = i + 1, Name = r.name, Sound = r.sound, Letter = r.letter, Art = r.art, Meaning = r.meaning, Note = r.note });
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

        // The build menu's text under the name: the rune's meaning, then a short line on what it stood for (the user,
        // 2026-10-30).
        internal static string Describe(Rune rune) => rune.Meaning + "\n" + rune.Note;
    }
}
