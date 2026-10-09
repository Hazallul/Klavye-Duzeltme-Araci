namespace Duzeltici.Engine;

/// <summary>
/// Sıklığa bakarak bulunamayan Türkçe yazım yanlışları. Altyazı listesinde "herşey" 25 bin,
/// "birşey" 61 bin kez geçtiği için bunlar sözlükte "doğru" görünür; TDK'ye göre ayrı yazılır.
/// Kurallar dar tutuldu: yalnızca bilinen yanlışlar ve ayrı yazılması kesin olan "de/da", "ki".
/// </summary>
public static class TurkishRules
{
    /// <summary>Kelimenin tamamı.</summary>
    static readonly Dictionary<string, string> s_words = new(StringComparer.Ordinal)
    {
        ["diyip"] = "deyip",
        ["yada"] = "ya da",
        ["yinede"] = "yine de",
        ["tabiki"] = "tabii ki",
        ["malesef"] = "maalesef",
        ["birdaha"] = "bir daha",
        ["hergün"] = "her gün",
        ["herzaman"] = "her zaman",
        ["pekçok"] = "pek çok",
        ["birsürü"] = "bir sürü",
        ["hoşçakal"] = "hoşça kal",
        ["hoşbulduk"] = "hoş bulduk",
        ["küsür"] = "küsur",
    };

    /// <summary>Kelimenin başı; ekleri korunur: "herşeyi" → "her şeyi", "yanlızca" → "yalnızca".</summary>
    static readonly (string Wrong, string Right)[] s_stems =
    [
        ("hiçbirşey", "hiçbir şey"),
        ("herşey", "her şey"),
        ("birşey", "bir şey"),
        ("bişey", "bir şey"),
        ("hoşgel", "hoş gel"),
        ("herkez", "herkes"),
        ("yanlız", "yalnız"),
        ("yalnış", "yanlış"),
        ("orjinal", "orijinal"),
        ("şöför", "şoför"),
        ("eşortman", "eşofman"),
        ("süpriz", "sürpriz"),
        ("antreman", "antrenman"),
        ("poaça", "poğaça"),
        ("laboratuar", "laboratuvar"),
        ("egzos", "egzoz"),
        ("dinazor", "dinozor"),
        ("klavuz", "kılavuz"),
        ("aliminyum", "alüminyum"),
        ("entresan", "enteresan"),
        ("kirbit", "kibrit"),
        ("sandoviç", "sandviç"),
        ("traş", "tıraş"),
    ];

    /// <summary>
    /// Günlük dildeki olumsuz gelecek zamanda "y" atlanmış: "olmıcak" → "olmıycak" (= olmayacak).
    /// "y" yerine "m" silinip "olıcak" (= olacak) yapmak anlamı tersine çevirir; bu yüzden kural olarak
    /// ve yalnızca standart biçim ("olmayacak") sözlükte varsa uygulanır. Üslup korunur.
    /// </summary>
    static readonly (string Wrong, string Right, string Standard)[] s_negativeFuture =
    [
        ("mıcak", "mıycak", "mayacak"), ("micek", "miycek", "meyecek"),
        ("mıcam", "mıycam", "mayacağım"), ("micem", "miycem", "meyeceğim"),
        ("mıcaz", "mıycaz", "mayacağız"), ("micez", "miycez", "meyeceğiz"),
        ("mıcan", "mıycan", "mayacaksın"), ("micen", "miycen", "meyeceksin"),
    ];

    /// <param name="word">Türkçe kurallarla küçültülmüş kelime.</param>
    /// <param name="frequency">Kelimenin Türkçe sözlükteki ln(sıklık) değeri; yoksa null.</param>
    public static string? Apply(string word, Func<string, float?> frequency)
    {
        if (s_words.TryGetValue(word, out var right)) return right;
        foreach (var (wrong, fix) in s_stems)
            if (word.StartsWith(wrong, StringComparison.Ordinal)) return fix + word[wrong.Length..];
        foreach (var (wrong, fix, standard) in s_negativeFuture)
        {
            if (word.Length <= wrong.Length || !word.EndsWith(wrong, StringComparison.Ordinal)) continue;
            string stem = word[..^wrong.Length];
            if (frequency(stem + standard) is >= 3f) return stem + fix; // standart biçim en az ~20 kez geçmeli
        }
        return SplitClitic(word, frequency);
    }

    /// <summary>Bağlaç "de/da"nın ayrılabileceği kökler: bulunma eki alamayan zamir/zarf biçimleri.
    /// ("ben de"/"bende", "o da"/"oda" iki türlü de doğru olduğu için listede yok.)</summary>
    static readonly HashSet<string> s_cliticHosts = new(StringComparer.Ordinal)
    {
        "bunu", "şunu", "onu", "beni", "seni", "bizi", "sizi", "onları", "bunları", "şunları", "kendini",
        "bana", "sana", "ona", "bize", "size", "onlara", "buna", "şuna", "kendine",
        "sonra", "yine", "hem", "ama", "artık", "şimdi", "bugün", "yarın", "belki", "bir", "daha", "ne",
        "böyle", "öyle", "şöyle", "hala", "hâlâ", "pek", "çok",
    };

    /// <summary>Çekimli fiil sonları: fiil bulunma eki alamaz, ardından gelen "de/da" kesin bağlaçtır
    /// ("olmuyoda" → "olmuyo da", "geldide" → "geldi de"); "ki" de öyle ("dediki" → "dedi ki").</summary>
    static readonly string[] s_verbEndings =
    [
        "yor", "yo", "dı", "di", "du", "dü", "tı", "ti", "tu", "tü", "mış", "miş", "muş", "müş",
        "acak", "ecek", "malı", "meli", "yorum", "yorsun", "dım", "dim", "dum", "düm", "mam", "mem",
    ];

    /// <summary>
    /// Bağlaç olan "de/da" ve "ki" ayrı yazılır: "bunuda" → "bunu da", "dediki" → "dedi ki". Ama "kelimede",
    /// "evde" (bulunma eki), "şimdiki", "akşamki" (ilgi eki) ve "belki" bitişiktir; yazılışa bakarak ayırt
    /// edilemezler. Bu yüzden yalnızca dilbilgisi olarak bağlacın gelebileceği yerlerde ayrılır: zamir/zarf
    /// biçimlerinden ve çekimli fiillerden sonra. Ek güvence olarak kök bitişik halinden çok daha yaygın
    /// olmalı ("bunu" 784 bin, "bunuda" 176). "de/da" için ünlü uyumu da tutmalı.
    /// </summary>
    static string? SplitClitic(string word, Func<string, float?> frequency)
    {
        if (word.Length < 4) return null;
        string clitic = word[^2..];
        if (clitic is not ("da" or "de" or "ki")) return null;
        string stem = word[..^2];
        if (clitic != "ki" && clitic != (BackVowelLast(stem) ? "da" : "de")) return null;
        bool verb = s_verbEndings.Any(e => stem.Length > e.Length + 1 && stem.EndsWith(e, StringComparison.Ordinal));
        if (!verb && (clitic == "ki" || !s_cliticHosts.Contains(stem))) return null;

        float? stemFreq = frequency(stem);
        if (stemFreq is not { } s || s < MathF.Log(2_000)) return null;
        float joined = frequency(word) ?? 0f;
        return s - joined >= MathF.Log(150) ? $"{stem} {clitic}" : null;
    }

    static bool BackVowelLast(string s)
    {
        for (int i = s.Length - 1; i >= 0; i--)
        {
            if (s[i] is 'a' or 'ı' or 'o' or 'u' or 'â' or 'û') return true;
            if (s[i] is 'e' or 'i' or 'ö' or 'ü' or 'î') return false;
        }
        return false;
    }
}
