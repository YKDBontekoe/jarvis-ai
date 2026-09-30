using System.Globalization;
using System.Text;

namespace Jarvis.Application.Memory;

/// <summary>One searchable term: the literal term for fuzzy matching, its tsquery form, and its weight.</summary>
public sealed record MemoryQueryTerm(string Term, string TsQuery, double Weight);

/// <summary>
/// Turns a chat message or search phrase into OR-able keyword terms for memory search. Chat turns are full
/// sentences, so requiring every word (websearch_to_tsquery) almost never matched; instead stopwords are dropped,
/// longer terms become prefix queries (Dutch and English inflections such as woon/woont or train/training), and a
/// small Dutch-English lexicon adds the other language's word at a lower weight because memories may be written in
/// either language.
/// </summary>
public sealed class MemoryQuery
{
    public const int MaxTerms = 16;
    internal const double ExpansionWeight = 0.5;

    private MemoryQuery(string text, IReadOnlyList<MemoryQueryTerm> terms)
    {
        Text = text;
        Terms = terms;
    }

    public string Text { get; }
    public IReadOnlyList<MemoryQueryTerm> Terms { get; }
    public bool IsEmpty => Terms.Count == 0;

    public static MemoryQuery Parse(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        var terms = new List<MemoryQueryTerm>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var originals = Tokenize(trimmed).Where(token => !Stopwords.Contains(token)).ToArray();
        foreach (var token in originals)
            Add(token, 1d);
        foreach (var token in originals)
            foreach (var translation in Lexicon.GetValueOrDefault(Stem(token)) ?? [])
                Add(translation, ExpansionWeight);
        return new MemoryQuery(trimmed, terms);

        void Add(string token, double weight)
        {
            if (terms.Count >= MaxTerms) return;
            var stem = Stem(token);
            if (!seen.Add(stem)) return;
            terms.Add(new MemoryQueryTerm(token, stem.Length >= 4 ? stem + ":*" : stem, weight));
        }
    }

    /// <summary>Lower-cased letter/digit runs; diacritics are kept so tsvector lexemes still match.</summary>
    public static IEnumerable<string> Tokenize(string text)
    {
        var builder = new StringBuilder();
        foreach (var character in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
                continue;
            }
            if (builder.Length >= 2) yield return builder.ToString();
            builder.Clear();
        }
        if (builder.Length >= 2) yield return builder.ToString();
    }

    /// <summary>
    /// Light suffix stripping so a prefix query also finds shorter forms (training → train:*, sisters → sister:*).
    /// It never shortens a term below four characters, which keeps prefix queries selective.
    /// </summary>
    public static string Stem(string token)
    {
        foreach (var suffix in Suffixes)
        {
            if (token.Length - suffix.Length >= 4 && token.EndsWith(suffix, StringComparison.Ordinal))
                return token[..^suffix.Length];
        }
        return token;
    }

    private static readonly string[] Suffixes = ["ing", "ies", "es", "en", "ed", "s", "e"];

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        // English
        "a", "an", "the", "and", "or", "but", "if", "of", "to", "in", "on", "at", "for", "with", "from", "by",
        "about", "as", "into", "is", "am", "are", "was", "were", "be", "been", "being", "do", "does", "did",
        "have", "has", "had", "i", "me", "my", "mine", "myself", "you", "your", "yours", "we", "us", "our",
        "he", "she", "it", "its", "they", "them", "their", "this", "that", "these", "those", "what", "which",
        "who", "whom", "when", "where", "why", "how", "can", "could", "should", "would", "will", "shall", "may",
        "might", "must", "please", "some", "any", "all", "again", "also", "just", "still", "now", "then", "so",
        "not", "no", "yes", "up", "out", "there", "here", "tell", "remind", "know", "let", "get", "got",
        "usually", "user", "jarvis", "hey", "hi", "thanks", "ok", "okay", "very", "really", "too", "right",
        // Dutch
        "de", "het", "een", "en", "of", "maar", "als", "van", "naar", "in", "op", "aan", "met", "voor", "bij",
        "om", "uit", "over", "tot", "door", "is", "ben", "bent", "zijn", "was", "waren", "wordt", "worden",
        "heb", "hebt", "heeft", "hebben", "had", "ik", "mij", "me", "mijn", "je", "jij", "jou", "jouw", "u",
        "uw", "we", "wij", "ons", "onze", "hij", "zij", "ze", "hem", "haar", "hun", "dit", "dat", "deze", "die",
        "wat", "welke", "welk", "wie", "wanneer", "waar", "waarom", "hoe", "kun", "kan", "kunnen", "kunt",
        "zou", "zouden", "wil", "wilt", "willen", "moet", "moeten", "mag", "zal", "zullen", "ga", "gaat",
        "gaan", "doe", "doet", "doen", "er", "hier", "daar", "nog", "ook", "al", "alweer", "weer", "nu", "dan",
        "toch", "wel", "niet", "geen", "ja", "nee", "even", "eens", "graag", "alsjeblieft", "aub", "zeg",
        "vertel", "weet", "meestal", "gebruiker", "heet", "heten", "hoi", "hallo", "bedankt", "dank", "oké", "heel", "erg", "zo"
    };

    /// <summary>
    /// Everyday Dutch-English pairs for the life areas memories cover. Keys are stems; values are added as
    /// lower-weight alternatives so a Dutch question can still find an English memory and the other way round.
    /// </summary>
    private static readonly Dictionary<string, string[]> Lexicon = BuildLexicon(
    [
        ["zus", "sister"], ["broer", "brother"], ["moeder", "mother", "mom", "mama"], ["vader", "father", "dad", "papa"],
        ["ouders", "parents"], ["zoon", "son"], ["dochter", "daughter"], ["kind", "child", "kids"],
        ["kinderen", "children"], ["oma", "grandmother", "grandma"], ["opa", "grandfather", "grandpa"],
        ["vriend", "friend", "boyfriend"], ["vriendin", "girlfriend", "friend"], ["partner", "partner"],
        ["vrouw", "wife"], ["man", "husband"], ["collega", "colleague", "coworker"], ["baas", "boss", "manager"],
        ["hond", "dog"], ["kat", "cat"], ["huisdier", "pet"], ["dierenarts", "vet"],
        ["werk", "work", "job"], ["baan", "job"], ["kantoor", "office"], ["vergadering", "meeting"],
        ["salaris", "salary"], ["geld", "money"], ["kosten", "cost", "budget"],
        ["woon", "live", "lives"], ["wonen", "live"], ["huis", "house", "home"], ["thuis", "home"],
        ["stad", "city"], ["adres", "address"], ["buurt", "neighbourhood", "neighborhood"],
        ["eten", "food", "eat", "dinner"], ["avondeten", "dinner"], ["ontbijt", "breakfast"], ["lunch", "lunch"],
        ["koken", "cook", "cooking"], ["kook", "cook"], ["koffie", "coffee"], ["thee", "tea"],
        ["vegetarisch", "vegetarian"], ["vis", "fish"], ["vlees", "meat"], ["drinken", "drink"],
        ["allergie", "allergy", "allergic"], ["hooikoorts", "hay", "fever", "pollen"], ["dokter", "doctor"],
        ["ziek", "sick", "ill"], ["medicijn", "medication", "medicine"], ["tandarts", "dentist"],
        ["slapen", "sleep", "bed"], ["slaap", "sleep"], ["bed", "bed"],
        ["reis", "trip", "travel"], ["reizen", "travel"], ["vakantie", "holiday", "vacation"],
        ["vlucht", "flight"], ["hotel", "hotel"], ["auto", "car"], ["fiets", "bike", "bicycle"],
        ["bakfiets", "cargo", "bike"], ["trein", "train"], ["pendelen", "commute"], ["pendel", "commute"],
        ["verjaardag", "birthday"], ["jarig", "birthday"], ["bruiloft", "wedding"], ["afspraak", "appointment"], ["afspraken", "appointments"],
        ["boek", "book"], ["boeken", "books"], ["lezen", "read", "reading"], ["lees", "read"],
        ["muziek", "music"], ["film", "movie", "film"], ["sporten", "sport", "workout", "gym"],
        ["hardlopen", "running", "run"], ["loop", "run"], ["klimmen", "climbing", "bouldering"],
        ["boulderen", "bouldering"], ["zwemmen", "swimming"], ["wandelen", "walk", "walking"],
        ["taal", "language"], ["leren", "learn", "learning"], ["studeren", "study"], ["japans", "japanese"], ["engels", "english"], ["nederlands", "dutch"],
        ["verwarming", "heating", "thermostat"], ["licht", "light", "lights"], ["lampen", "lights"],
        ["planten", "plants"], ["plant", "plant"], ["tuin", "garden"], ["balkon", "balcony"],
        ["maandag", "monday"], ["dinsdag", "tuesday"], ["woensdag", "wednesday"], ["donderdag", "thursday"],
        ["vrijdag", "friday"], ["zaterdag", "saturday"], ["zondag", "sunday"], ["weekend", "weekend"],
        ["ochtend", "morning"], ["middag", "afternoon"], ["avond", "evening"], ["vanavond", "tonight"],
        ["week", "week"], ["maand", "month"], ["jaar", "year"], ["dag", "day"],
        ["project", "project"], ["beslissing", "decision"], ["besloten", "decided"], ["voorkeur", "prefer"],
        ["favoriet", "favourite", "favorite"], ["hekel", "hate", "dislike"], ["houd", "love", "like"],
        ["cadeau", "present", "gift"], ["winkel", "shop", "store"], ["boodschappen", "groceries"]
    ]);

    private static Dictionary<string, string[]> BuildLexicon(string[][] groups)
    {
        var lexicon = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var word in group)
            {
                var key = Stem(word);
                if (!lexicon.TryGetValue(key, out var translations))
                    lexicon[key] = translations = new HashSet<string>(StringComparer.Ordinal);
                foreach (var other in group)
                    if (Stem(other) != key) translations.Add(other);
            }
        }
        return lexicon.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }
}
