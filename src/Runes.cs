// The Rúnakefli's designs, organized as in claude/potatopainter-runes.md: by row, in the row's own order, by ætt; then the
// symbols.
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
        // Saved in worlds (two piece prefabs per design: painted, and etched, which glows): never rename.
        internal string Prefab => "potato_paint_" + Art;
        internal string EtchedPrefab => "potato_etch_" + Art;
        internal string NameToken => "piece_" + Prefab;
        internal string DescriptionToken => "piece_" + Prefab + "_description";
        // The build menu tabs: "Runes: Painted" and "Runes: Etched" (Painted and Etched apart, the user 2026-10-30; one set of
        // runes for every rune row, the user 2026-10-31).
        // Symbols get their own tabs, "Symbols: Painted" and "Symbols: Etched" (the user, 2026-10-31).
        internal string Tab(bool etched) => (Row == Runes.Symbols ? "Symbols" : "Runes") + (etched ? ": Etched" : ": Painted");
        // A symbol a boss unlocks: the boss's prefab name (its Character.m_defeatSetGlobalKey is the key); null when open
        // from the start.
        internal string Boss;
        // Where the same shape is a rune of the other rows too: their names, shown under the note.
        internal string Also;
    }

    internal static class Runes
    {
        internal const string Elder = "Elder Futhark", Younger = "Younger Futhark", ShortTwig = "Younger Futhark, short-twig", Futhorc = "Anglo-Saxon futhorc", Symbols = "Symbols";

        // One set of runes (the user, 2026-10-31: the rows' duplicates were redundant): every shape once, 51 in all. First the
        // Elder Futhark, then the shapes the Younger Futhark adds (long-branch, then short-twig), then the futhorc's. A shape
        // the other rows share is named by its first row and lists the other names under its note (ᛉ algiz is maðr in the
        // Younger Futhark and eolh in the futhorc). Sources for the shared shapes: claude/potatopainter-runes.md, Wikipedia's
        // Algiz page (maðr has algiz's shape, calc has yr's), Noto Sans Runic (ġēar and ior drawn like hagall).
        private static readonly Dictionary<string, string> AlsoNames = new Dictionary<string, string>
        {
            { "elder_fehu", "Also fé in the Younger Futhark and feoh in the futhorc." }, { "elder_uruz", "Also úr in the Younger Futhark and ūr in the futhorc." },
            { "elder_thurisaz", "Also þurs in the Younger Futhark and þorn in the futhorc." }, { "elder_raido", "Also reið in the Younger Futhark and rād in the futhorc." },
            { "elder_gebo", "Also ġyfu in the futhorc." }, { "elder_wunjo", "Also wynn in the futhorc." },
            { "elder_naudiz", "Also nauðr in the Younger Futhark and nēod in the futhorc." }, { "elder_isaz", "Also íss in the Younger Futhark and īs in the futhorc." },
            { "elder_ihwaz", "Also īw in the futhorc." }, { "elder_pertho", "Also peorþ in the futhorc." },
            { "elder_algiz", "Also maðr (man) in the Younger Futhark and eolh (elk-sedge) in the futhorc." },
            { "elder_tiwaz", "Also Týr in the Younger Futhark and Tīw in the futhorc." }, { "elder_berkanan", "Also bjǫrk in the Younger Futhark and beorc in the futhorc." },
            { "elder_ehwaz", "Also eoh in the futhorc." }, { "elder_mannaz", "Also mann in the futhorc." },
            { "elder_laguz", "Also lǫgr in the Younger Futhark and lagu in the futhorc." }, { "elder_dagaz", "Also dæġ in the futhorc." },
            { "elder_othala", "Also œ̄þel in the futhorc." },
            { "younger_hagall", "Also ġēar (year) and ior (a river creature) in the futhorc." }, { "younger_sol", "Also siġel in the futhorc." },
            { "younger_yr", "Also calc in the futhorc." },
        };

        // The shapes the Younger Futhark adds, long-branch, in its order. Meanings: the names' glosses in Bruce Dickins' 1915
        // translation of the Icelandic and Norwegian rune poems (read 2026-10-31 on ragweedforge.com, rpie.html and
        // RunNRPe.html; áss is Óðinn in the Icelandic poem, estuary in the Norwegian). Notes: what the poems say, reworded.
        private static readonly (string name, string sound, string letter, string art, string meaning, string note)[] YoungerRunes =
        {
            ("Áss", "ą", "ᚬ", "younger_oss", "A god, or estuary", "Stands for Óðinn, prince of Ásgarðr and lord of Valhalla."),
            ("Kaun", "k", "ᚴ", "younger_kaun", "Ulcer", "Stands for sickness, deadly to children."),
            ("Hagall", "h", "ᚼ", "younger_hagall", "Hail", "Stands for cold and ruin, the coldest of grain."),
            ("Ár", "a", "ᛅ", "younger_ar", "Plenty, a good year", "Stands for a good summer and thriving crops."),
            ("Sól", "s", "ᛋ", "younger_sol", "Sun", "Stands for light, the shield of the clouds and bane of ice."),
            ("Ýr", "ʀ", "ᛦ", "younger_yr", "Yew, the bow", "Stands for the bow, from the yew that stays green in winter."),
        };

        // The short-twig forms (Swedish and Norwegian; the other seven short-twig runes have the long-branch shapes), in the
        // row's order, named after their long-branch rune.
        private static readonly (string name, string sound, string letter, string art, string meaning, string note)[] ShortTwigRunes =
        {
            ("Áss, short-twig", "ą", "ᚭ", "short_oss", "A god, or estuary", "The short-twig áss, carved in Sweden and Norway."),
            ("Hagall, short-twig", "h", "ᚽ", "short_hagall", "Hail", "The short-twig hagall, carved in Sweden and Norway."),
            ("Nauðr, short-twig", "n", "ᚿ", "short_naud", "Need, constraint", "The short-twig nauðr, carved in Sweden and Norway."),
            ("Ár, short-twig", "a", "ᛆ", "short_ar", "Plenty, a good year", "The short-twig ár, carved in Sweden and Norway."),
            ("Sól, short-twig", "s", "ᛌ", "short_sol", "Sun", "The short-twig sól, carved in Sweden and Norway."),
            ("Týr, short-twig", "t", "ᛐ", "short_tyr", "The god Týr", "The short-twig Týr, carved in Sweden and Norway."),
            ("Bjǫrk, short-twig", "b", "ᛓ", "short_bjarkan", "Birch", "The short-twig bjǫrk, carved in Sweden and Norway."),
            ("Maðr, short-twig", "m", "ᛙ", "short_madr", "Man", "The short-twig maðr, carved in Sweden and Norway."),
            ("Ýr, short-twig", "ʀ", "ᛧ", "short_yr", "Yew, the bow", "The short-twig ýr, carved in Sweden and Norway."),
        };

        // The shapes the Anglo-Saxon futhorc adds, in Codex Vindobonensis 795 order, then its later additions. Meanings and
        // notes: the Old English rune poem in Bruce Dickins' 1915 translation (read 2026-10-31 on ragweedforge.com,
        // rpae.html), reworded; gār, cweorð, stān and the Ruthwell rune have no stanza, so their notes say where they are
        // found (claude/potatopainter-runes.md, Wikipedia's Anglo-Saxon runes page).
        private static readonly (string name, string sound, string letter, string art, string meaning, string note)[] FuthorcRunes =
        {
            ("Ōs", "o", "ᚩ", "futhorc_os", "Mouth", "Stands for speech, the source of wisdom."),
            ("Ċēn", "c", "ᚳ", "futhorc_cen", "Torch", "Stands for light, a bright flame where princes sit."),
            ("Hæġl", "h", "ᚻ", "futhorc_haegl", "Hail", "Stands for change, white grain from heaven that turns to water."),
            ("Ing", "ŋ", "ᛝ", "futhorc_ing", "The hero Ing", "Stands for the hero seen among the East-Danes, who left over the waves."),
            ("Āc", "a", "ᚪ", "futhorc_ac", "Oak", "Stands for worth, feeding swine on land and proving itself at sea."),
            ("Æsc", "æ", "ᚫ", "futhorc_aesc", "Ash", "Stands for standing firm against many attacks."),
            ("Ēar", "ea", "ᛠ", "futhorc_ear", "Earth, the grave", "Stands for the end, when joys and pledges pass away."),
            ("Ȳr", "y", "ᚣ", "futhorc_yr", "Bow", "Stands for trusty war gear, an honour to princes."),
            ("Gār", "ḡ", "ᚸ", "futhorc_gar", "Spear", "A later futhorc rune for a hard g."),
            ("Cweorð", "q", "ᛢ", "futhorc_cweorth", "Meaning unknown", "A later futhorc rune, found only in manuscripts, perhaps for writing Latin q."),
            ("Stān", "st", "ᛥ", "futhorc_stan", "Stone", "A later futhorc rune, found only in manuscripts, for st."),
            ("Ruthwell rune", "k", "ᛤ", "futhorc_ruthwell", "Name unknown", "Seen only on the Ruthwell Cross, for a k sound."),
        };

        // Elder Futhark, Kylver Stone order: three ættir of eight, each named after its first rune.
        // Meanings: the reconstructed Proto-Germanic names' meanings as Wikipedia's Elder Futhark table gives them (read
        // 2026-10-30 through a copy of that table), shortened. Notes: what each rune stood for, from the medieval rune poems as
        // norse-mythology.org's "The Meanings of the Runes" sums them up (read 2026-10-30), reworded. Īsaz and Perþō, which
        // that page leaves open, take the most common reading (the user asked for our best interpretation): Īsaz, ice the
        // poems call cold, slippery and "the broad bridge", read as stillness; Perþō, which the Old English poem calls
        // play and laughter in the beer hall, read as a lot or dice cup, so chance and play (Wikipedia's Isaz and Peorð
        // pages and modern rune books, 2026-10-30). Notes that only repeated their meaning (the user, 2026-10-30: Algiz
        // "protection" twice) were rewritten from the rune poems themselves, Bruce Dickins' 1915 translation (read
        // 2026-10-30 on sacred-texts.com and Wikisource copies): Feoh must be shared, and wealth is strife among kinsmen
        // (Norwegian, Icelandic); Rad, easy for a warrior indoors, hard on a stout horse on the long roads; Gyfu, an
        // ornament, praise and support to the giver and the destitute; Wynn, joy for one who knows few woes; Nyd,
        // oppressive to the heart yet a help to those who hearken to it; Ger, the earth's fruit to wealthy and needy;
        // Eolhx, the marsh sedge that wounds whoever grasps it; Ethel, a home with justice and prosperity.
        private static readonly (string aett, string name, string sound, string letter, string art, string meaning, string note)[] ElderRunes =
        {
            ("f", "Fehu", "f", "ᚠ", "elder_fehu", "Cattle, wealth", "Stands for sharing, since hoarded wealth set kin at odds."), ("f", "Ūruz", "u", "ᚢ", "elder_uruz", "Aurochs", "Stands for strength of will."), ("f", "Þurisaz", "þ", "ᚦ", "elder_thurisaz", "Giant", "Stands for danger and hardship."),
            ("f", "Ansuz", "a", "ᚨ", "elder_ansuz", "God", "Stands for vitality."), ("f", "Raidō", "r", "ᚱ", "elder_raido", "Ride, journey", "Stands for the long road, easier boasted of than ridden."), ("f", "Kauną", "k", "ᚲ", "elder_kauna", "Ulcer, or torch", "Stands for pain."),
            ("f", "Gebō", "g", "ᚷ", "elder_gebo", "Gift", "Stands for the honour won by giving."), ("f", "Wunjō", "w", "ᚹ", "elder_wunjo", "Joy", "Stands for a life with little pain or sorrow."),
            ("h", "Hagalaz", "h", "ᚺ", "elder_hagalaz", "Hail", "Stands for ruin and disorder."), ("h", "Naudiz", "n", "ᚾ", "elder_naudiz", "Need", "Stands for hardship that helps those who heed it."), ("h", "Īsaz", "i", "ᛁ", "elder_isaz", "Ice", "Stands for stillness."),
            ("h", "Jēra", "j", "ᛃ", "elder_jera", "Year, harvest", "Stands for the earth's yield, for rich and poor alike."), ("h", "Ī(h)waz", "ï", "ᛇ", "elder_ihwaz", "Yew tree", "Stands for strength and steadiness."), ("h", "Perþō", "p", "ᛈ", "elder_pertho", "Meaning unknown, perhaps pear tree", "Stands for chance and play."),
            ("h", "Algiz", "z", "ᛉ", "elder_algiz", "Elk, or protection", "Stands for a guard, like the sedge that cuts a grasping hand."), ("h", "Sōwilō", "s", "ᛊ", "elder_sowilo", "Sun", "Stands for success and comfort."),
            ("t", "Tīwaz", "t", "ᛏ", "elder_tiwaz", "The god Tīwaz", "Stands for victory and honour."), ("t", "Berkanan", "b", "ᛒ", "elder_berkanan", "Birch", "Stands for growth and fertility."), ("t", "Ehwaz", "e", "ᛖ", "elder_ehwaz", "Horse", "Stands for trust and companionship."),
            ("t", "Mannaz", "m", "ᛗ", "elder_mannaz", "Man", "Stands for support."), ("t", "Laguz", "l", "ᛚ", "elder_laguz", "Water, lake", "Stands for the unknown."), ("t", "Ingwaz", "ŋ", "ᛜ", "elder_ingwaz", "The god Ingwaz", "Stands for new beginnings."),
            ("t", "Dagaz", "d", "ᛞ", "elder_dagaz", "Day", "Stands for hope and happiness."), ("t", "Ōþala", "o", "ᛟ", "elder_othala", "Heritage, estate", "Stands for a home with justice and plenty."),
        };

        // The symbols (agreed with the user 2026-10-31; claude/potatopainter-plan.md, "Symbols"): the left and right hand open
        // from the start, the rest four per boss, Eikthyr, The Elder, Bonemass (the user: "split it evenly amongst the first
        // three bosses"). Drawings: _work-2026-10-29\painter-lods\symbols and \hands. Boss: prefab name, or null.
        private static readonly (string name, string art, string boss, string meaning, string note)[] SymbolList =
        {
            ("Hand, left", "symbol_hand_left", null, "A hand print", "A left hand pressed in paint."),
            ("Hand, right", "symbol_hand_right", null, "A hand print", "A right hand pressed in paint."),
            ("Vegvísir", "symbol_vegvisir", "Eikthyr", "Wayfinder", "From an Icelandic book of magic staves (the Huld manuscript, about 1860): whoever carries it won't lose the way in storms or bad weather."),
            ("Huginn and Muninn", "symbol_ravens", "Eikthyr", "Thought and Memory", "Óðinn's two ravens, who fly over the world each day and bring him news. Drawn after the raven on a York penny of about 940."),
            ("Sleipnir", "symbol_sleipnir", "Eikthyr", "Óðinn's eight-legged horse", "The best of all horses. Drawn after the Ardre VIII picture stone from Gotland."),
            ("Gungnir", "symbol_gungnir", "Eikthyr", "Óðinn's spear", "Made by the sons of Ívaldi. The sign is modern."),
            ("Yggdrasil", "symbol_yggdrasil", "gd_king", "The World Tree", "The great ash whose roots and branches reach through the nine worlds."),
            ("Web of Wyrd", "symbol_wyrd", "gd_king", "Fate", "Nine staves that hold every rune. A modern sign (Jan Fries, 1993)."),
            ("Troll cross", "symbol_troll_cross", "gd_king", "Protection", "Worn against trolls and evil. A modern sign, first made by a smith in the late 1990s."),
            ("Mjölnir, knotwork", "symbol_mjolnir_knot", "gd_king", "Þórr's hammer", "With a braid down the handle and a triquetra on the head, like many modern pendants."),
            ("Valknut", "symbol_valknut", "Bonemass", "Knot of the slain", "Three linked triangles, carved on Gotland picture stones beside Óðinn and the dead. The name is modern."),
            ("Mjölnir", "symbol_mjolnir", "Bonemass", "Þórr's hammer", "Drawn after a silver hammer pendant from Skåne, Sweden."),
            ("Triple Horn", "symbol_triple_horn", "Bonemass", "Three drinking horns", "Linked horns, carved on the Snoldelev Stone in Denmark in the 800s."),
            ("Hand, bloody", "symbol_hand_bloody", "Bonemass", "A bloody hand print", "Left by a hand fresh from battle."),
        };

        internal static readonly List<Rune> All = new List<Rune>();

        static Runes()
        {
            for (int i = 0; i < ElderRunes.Length; i++)
            {
                var r = ElderRunes[i];
                All.Add(new Rune { Row = Elder, Aett = r.aett, Position = i + 1, Name = r.name, Sound = r.sound, Letter = r.letter, Art = r.art, Meaning = r.meaning, Note = r.note });
            }
            void Add(string row, (string name, string sound, string letter, string art, string meaning, string note)[] runes)
            {
                for (int i = 0; i < runes.Length; i++)
                {
                    var r = runes[i];
                    All.Add(new Rune { Row = row, Aett = "", Position = i + 1, Name = r.name, Sound = r.sound, Letter = r.letter, Art = r.art, Meaning = r.meaning, Note = r.note });
                }
            }
            Add(Younger, YoungerRunes); Add(ShortTwig, ShortTwigRunes); Add(Futhorc, FuthorcRunes);
            for (int i = 0; i < SymbolList.Length; i++)
            {
                var r = SymbolList[i];
                All.Add(new Rune { Row = Symbols, Aett = "", Position = i + 1, Name = r.name, Sound = "", Letter = "", Art = r.art, Boss = r.boss, Meaning = r.meaning, Note = r.note });
            }
            foreach (var rune in All) if (AlsoNames.TryGetValue(rune.Art, out var also)) rune.Also = also;
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

        // The name as the game's menus can show it. Valheim's UI fonts (AveriaSerifLibre, AveriaSansLibre; checked in
        // resources.assets 2026-10-30 and 2026-10-31) have no ā ē ī ō ū ą ę ǫ ċ ġ ȳ ḡ, so those marks are dropped (the
        // 0.1.1 video showed "Isaz" for Īsaz), as is the macron over œ; é á í ó ú ý ð þ æ œ are in the font and stay.
        // Ī(h)waz is shown as Ihwaz.
        internal static string Shown(Rune rune) => rune.Name == "Ī(h)waz" ? "Ihwaz" : Plain(rune.Name);

        internal static string Plain(string text)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in text)
                switch (c)
                {
                    case 'Ā': sb.Append('A'); break; case 'ā': case 'ą': sb.Append('a'); break;
                    case 'Ē': sb.Append('E'); break; case 'ē': case 'ę': sb.Append('e'); break;
                    case 'Ī': sb.Append('I'); break; case 'ī': sb.Append('i'); break;
                    case 'Ō': sb.Append('O'); break; case 'ō': sb.Append('o'); break;
                    case 'Ū': sb.Append('U'); break; case 'ū': sb.Append('u'); break;
                    case 'Ǫ': sb.Append('O'); break; case 'ǫ': sb.Append('o'); break;
                    case 'Ċ': sb.Append('C'); break; case 'ċ': sb.Append('c'); break;
                    case 'Ġ': sb.Append('G'); break; case 'ġ': sb.Append('g'); break;
                    case 'Ȳ': sb.Append('Y'); break; case 'ȳ': sb.Append('y'); break;
                    case 'ḡ': sb.Append('g'); break;
                    case '\u0304': break;   // combining macron (œ̄þel)
                    default: sb.Append(c); break;
                }
            return sb.ToString();
        }

        // The build menu's text under the name: the rune's meaning, then a short line on what it stood for (the user,
        // 2026-10-30).
        // A shape the other rows share adds their names on a third line (the user, 2026-10-31: one set of runes).
        internal static string Describe(Rune rune) => Plain(rune.Meaning + "\n" + rune.Note + (rune.Also != null ? "\n" + rune.Also : ""));
    }
}
