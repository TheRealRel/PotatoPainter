// The Rúnakefli's designs, organized as in claude/potatopainter-runes.md: by row, in the row's own order, by ætt.
// A design is only offered once its drawing exists (assets/runes/<Art>.png, embedded in the DLL).
// Rows share drawings where the shape is the same: Art names the drawing, Id the design (its pieces).
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace PotatoPainter
{
    internal sealed class Rune
    {
        internal string Row, Aett, Name, Sound, Letter, Id, Art, Meaning, Note;
        // The Younger Futhark's short-twig drawing, where it differs from the long-branch one (the Rúnakefli's panel
        // switches between them; each paint keeps the style it was painted in). Null everywhere else.
        internal string ShortArt;
        internal int Position;
        // Saved in worlds (two piece prefabs per design: painted, and etched, which glows): never rename. Elder Futhark ids
        // are the drawing names they had before rows shared drawings (elder_fehu).
        internal string Prefab => "potato_paint_" + Id;
        internal string EtchedPrefab => "potato_etch_" + Id;
        internal string NameToken => "piece_" + Prefab;
        internal string DescriptionToken => "piece_" + Prefab + "_description";
        // The build menu tabs, one pair per row (the user, 2026-10-30): "Elder Futhark: Painted", "Elder Futhark: Etched".
        internal string Tab(bool etched) => Row + (etched ? ": Etched" : ": Painted");
        // The drawing for a style: short-twig only means something for runes that have a short-twig drawing.
        internal string ArtFor(bool shortTwig) => shortTwig && ShortArt != null ? ShortArt : Art;
        // The Younger Futhark's runes come in both styles (seven look the same in each).
        internal bool HasStyles => Row == Runes.Younger;
    }

    internal static class Runes
    {
        internal const string Elder = "Elder Futhark", Younger = "Younger Futhark", Futhorc = "Futhorc";

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

        // Younger Futhark (claude/potatopainter-runes.md): three ættir of 6, 5 and 5, long-branch drawing, then short-twig
        // where it differs. Shapes shared with the Elder Futhark use its drawings (ᚠ is fehu there and fé here); long-branch
        // maðr ᛘ is algiz's shape (Wikipedia's Algiz page). Meanings: the names' glosses in Bruce Dickins' 1915 translation
        // of the Icelandic and Norwegian rune poems (read 2026-10-31 on ragweedforge.com, rpie.html and RunNRPe.html; úr is
        // shower in the Icelandic poem and dross in the Norwegian, áss Óðinn in the Icelandic and estuary in the
        // Norwegian). Notes: what the poems say of each, reworded.
        private static readonly (string aett, string name, string sound, string letter, string id, string art, string shortArt, string meaning, string note)[] YoungerRunes =
        {
            ("f", "Fé", "f", "ᚠ", "younger_fe", "elder_fehu", null, "Wealth", "Stands for strife among kin, as both Norse poems warn."),
            ("f", "Úr", "u", "ᚢ", "younger_ur", "elder_uruz", null, "Drizzle, or slag", "Stands for spoilt work: rain on the hay, dross from bad iron."),
            ("f", "Þurs", "þ", "ᚦ", "younger_thurs", "elder_thurisaz", null, "Giant", "Stands for torment, a giant dwelling among the cliffs."),
            ("f", "Áss", "ą", "ᚬ", "younger_oss", "younger_oss", "short_oss", "A god, or estuary", "Stands for Óðinn, prince of Ásgarðr and lord of Valhalla."),
            ("f", "Reið", "r", "ᚱ", "younger_reid", "elder_raido", null, "Riding", "Stands for a swift journey, joy to the rider and toil to the horse."),
            ("f", "Kaun", "k", "ᚴ", "younger_kaun", "younger_kaun", null, "Ulcer", "Stands for sickness, deadly to children."),
            ("h", "Hagall", "h", "ᚼ", "younger_hagall", "younger_hagall", "short_hagall", "Hail", "Stands for cold and ruin, the coldest of grain."),
            ("h", "Nauðr", "n", "ᚾ", "younger_naudr", "elder_naudiz", "short_naud", "Need, constraint", "Stands for hardship that leaves little choice."),
            ("h", "Íss", "i", "ᛁ", "younger_iss", "elder_isaz", null, "Ice", "Stands for the broad bridge that binds the rivers."),
            ("h", "Ár", "a", "ᛅ", "younger_ar", "younger_ar", "short_ar", "Plenty, a good year", "Stands for a good summer and thriving crops."),
            ("h", "Sól", "s", "ᛋ", "younger_sol", "younger_sol", "short_sol", "Sun", "Stands for light, the shield of the clouds and bane of ice."),
            ("t", "Týr", "t", "ᛏ", "younger_tyr", "elder_tiwaz", "short_tyr", "The god Týr", "Stands for the one-handed god, prince of temples."),
            ("t", "Bjǫrk", "b", "ᛒ", "younger_bjork", "elder_berkanan", "short_bjarkan", "Birch", "Stands for new growth, a leafy twig and a fresh young shrub."),
            ("t", "Maðr", "m", "ᛘ", "younger_madr", "elder_algiz", "short_madr", "Man", "Stands for people, made of earth, a joy to one another."),
            ("t", "Lǫgr", "l", "ᛚ", "younger_logr", "elder_laguz", null, "Water, waterfall", "Stands for running water, the land of the fish."),
            ("t", "Ýr", "ʀ", "ᛦ", "younger_yr", "younger_yr", "short_yr", "Yew, the bow", "Stands for the bow, from the yew that stays green in winter."),
        };

        // Anglo-Saxon futhorc, Codex Vindobonensis 795 order (28), then its later additions. Ġēar and ior share long-branch
        // hagall's shape (Noto Sans Runic draws them alike), calc is long-branch yr's (Wikipedia's Algiz page). Meanings and
        // notes: the Old English rune poem in Bruce Dickins' 1915 translation (read 2026-10-31 on ragweedforge.com,
        // rpae.html), reworded; it has no stanza for calc, gār, cweorð, stān or the Ruthwell rune, whose notes say where
        // they are found (claude/potatopainter-runes.md, Wikipedia's Anglo-Saxon runes page).
        private static readonly (string name, string sound, string letter, string id, string art, string meaning, string note)[] FuthorcRunes =
        {
            ("Feoh", "f", "ᚠ", "futhorc_feoh", "elder_fehu", "Wealth", "Stands for generosity: wealth comforts, if freely shared."),
            ("Ūr", "u", "ᚢ", "futhorc_ur", "elder_uruz", "Aurochs", "Stands for courage, the proud wild ox of the moors."),
            ("Þorn", "þ", "ᚦ", "futhorc_thorn", "elder_thurisaz", "Thorn", "Stands for harm to whoever grasps it."),
            ("Ōs", "o", "ᚩ", "futhorc_os", "futhorc_os", "Mouth", "Stands for speech, the source of wisdom."),
            ("Rād", "r", "ᚱ", "futhorc_rad", "elder_raido", "Riding", "Stands for the road, easy indoors and hard on a long ride."),
            ("Ċēn", "c", "ᚳ", "futhorc_cen", "futhorc_cen", "Torch", "Stands for light, a bright flame where princes sit."),
            ("Ġyfu", "g", "ᚷ", "futhorc_gyfu", "elder_gebo", "Gift", "Stands for giving, an honour to the giver and help to those with nothing."),
            ("Wynn", "w", "ᚹ", "futhorc_wynn", "elder_wunjo", "Joy", "Stands for a life with little woe and a good home."),
            ("Hæġl", "h", "ᚻ", "futhorc_haegl", "futhorc_haegl", "Hail", "Stands for change, white grain from heaven that turns to water."),
            ("Nēod", "n", "ᚾ", "futhorc_neod", "elder_naudiz", "Need", "Stands for hardship that becomes a help to those who heed it."),
            ("Īs", "i", "ᛁ", "futhorc_is", "elder_isaz", "Ice", "Stands for cold beauty, slippery and glittering like gems."),
            ("Ġēar", "j", "ᛡ", "futhorc_gear", "younger_hagall", "Year, harvest", "Stands for the earth's fruit, given to rich and poor."),
            ("Īw", "ï", "ᛇ", "futhorc_iw", "elder_ihwaz", "Yew", "Stands for steadfastness, a tree firm in its roots."),
            ("Peorþ", "p", "ᛈ", "futhorc_peorth", "elder_pertho", "Meaning unknown", "Stands for play and laughter in the beer hall."),
            ("Eolh", "x", "ᛉ", "futhorc_eolh", "elder_algiz", "Elk-sedge", "Stands for a guard, like the marsh sedge that cuts a grasping hand."),
            ("Siġel", "s", "ᛋ", "futhorc_sigel", "younger_sol", "Sun", "Stands for a seafarer's hope, until land is reached."),
            ("Tīw", "t", "ᛏ", "futhorc_tiw", "elder_tiwaz", "The god Tīw, or a guiding star", "Stands for faith that never fails, a star that holds its course."),
            ("Beorc", "b", "ᛒ", "futhorc_beorc", "elder_berkanan", "Birch, or poplar", "Stands for growth without fruit, spreading from its shoots."),
            ("Eoh", "e", "ᛖ", "futhorc_eoh", "elder_ehwaz", "Horse", "Stands for pride, and comfort to the restless."),
            ("Mann", "m", "ᛗ", "futhorc_mann", "elder_mannaz", "Man", "Stands for kinship, though each must fail the other in the end."),
            ("Lagu", "l", "ᛚ", "futhorc_lagu", "elder_laguz", "Sea, water", "Stands for the endless sea, daunting to all who venture out."),
            ("Ing", "ŋ", "ᛝ", "futhorc_ing", "futhorc_ing", "The hero Ing", "Stands for the hero seen among the East-Danes, who left over the waves."),
            ("Œ̄þel", "œ", "ᛟ", "futhorc_ethel", "elder_othala", "Estate, homeland", "Stands for a home of plenty."),
            ("Dæġ", "d", "ᛞ", "futhorc_daeg", "elder_dagaz", "Day", "Stands for hope and gladness to rich and poor."),
            ("Āc", "a", "ᚪ", "futhorc_ac", "futhorc_ac", "Oak", "Stands for worth, feeding swine on land and proving itself at sea."),
            ("Æsc", "æ", "ᚫ", "futhorc_aesc", "futhorc_aesc", "Ash", "Stands for standing firm against many attacks."),
            ("Ēar", "ea", "ᛠ", "futhorc_ear", "futhorc_ear", "Earth, the grave", "Stands for the end, when joys and pledges pass away."),
            ("Ȳr", "y", "ᚣ", "futhorc_yr", "futhorc_yr", "Bow", "Stands for trusty war gear, an honour to princes."),
            ("Ior", "io", "ᛡ", "futhorc_ior", "younger_hagall", "A river creature, perhaps an eel", "Stands for a creature at home in the water that feeds on land."),
            ("Calc", "k", "ᛣ", "futhorc_calc", "younger_yr", "Meaning unsure: chalk, chalice or sandal", "A later rune for k, found on rings and on the Ruthwell and Bewcastle crosses."),
            ("Gār", "ḡ", "ᚸ", "futhorc_gar", "futhorc_gar", "Spear", "A later rune for a hard g."),
            ("Cweorð", "q", "ᛢ", "futhorc_cweorth", "futhorc_cweorth", "Meaning unknown", "Found only in manuscripts, perhaps for writing Latin q."),
            ("Stān", "st", "ᛥ", "futhorc_stan", "futhorc_stan", "Stone", "Found only in manuscripts, for st."),
            ("Ruthwell rune", "k", "ᛤ", "futhorc_ruthwell", "futhorc_ruthwell", "Name unknown", "Seen only on the Ruthwell Cross, for a k sound."),
        };

        internal static readonly List<Rune> All = new List<Rune>();

        static Runes()
        {
            for (int i = 0; i < ElderRunes.Length; i++)
            {
                var r = ElderRunes[i];
                All.Add(new Rune { Row = Elder, Aett = r.aett, Position = i + 1, Name = r.name, Sound = r.sound, Letter = r.letter, Id = r.art, Art = r.art, Meaning = r.meaning, Note = r.note });
            }
            for (int i = 0; i < YoungerRunes.Length; i++)
            {
                var r = YoungerRunes[i];
                All.Add(new Rune { Row = Younger, Aett = r.aett, Position = i + 1, Name = r.name, Sound = r.sound, Letter = r.letter, Id = r.id, Art = r.art, ShortArt = r.shortArt, Meaning = r.meaning, Note = r.note });
            }
            for (int i = 0; i < FuthorcRunes.Length; i++)
            {
                var r = FuthorcRunes[i];
                All.Add(new Rune { Row = Futhorc, Aett = "", Position = i + 1, Name = r.name, Sound = r.sound, Letter = r.letter, Id = r.id, Art = r.art, Meaning = r.meaning, Note = r.note });
            }
        }

        // The drawing's PNG bytes, or null while it isn't drawn yet.
        internal static byte[] ArtBytes(string art)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("runes." + art + ".png"))
            {
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // The name as the game's menus can show it. Valheim's UI fonts (AveriaSerifLibre, AveriaSansLibre; checked in
        // resources.assets 2026-10-30 and 2026-10-31) have no ā ē ī ō ū ą ę ǫ ċ ġ ȳ, so those marks are dropped (the 0.1.1
        // video showed "Isaz" for Īsaz), as is the macron over œ; é á í ó ú ý ð þ æ œ are in the font and stay. Ī(h)waz is
        // shown as Ihwaz.
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
                    case '\u0304': break;   // combining macron (Œ̄þel)
                    default: sb.Append(c); break;
                }
            return sb.ToString();
        }

        // The build menu's text under the name: the rune's meaning, then a short line on what it stood for (the user,
        // 2026-10-30).
        internal static string Describe(Rune rune) => Plain(rune.Meaning + "\n" + rune.Note);
    }
}
