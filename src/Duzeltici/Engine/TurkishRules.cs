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

    /// <param name="word">Türkçe kurallarla küçültülmüş kelime.</param>
    /// <param name="frequency">Kelimenin Türkçe sözlükteki ln(sıklık) değeri; yoksa null.</param>
    public static string? Apply(string word, Func<string, float?> frequency)
    {
        if (s_words.TryGetValue(word, out var right)) return right;
        foreach (var (wrong, fix) in s_stems)
            if (word.StartsWith(wrong, StringComparison.Ordinal)) return fix + word[wrong.Length..];
        return SplitClitic(word, frequency);
    }

    /// <summary>
    /// Bağlaç olan "de/da" ve "ki" ayrı yazılır: "bunuda" → "bunu da", "dediki" → "dedi ki".
    /// Ama "evde" (bulunma eki) ve "belki" bitişiktir. Ayırt etmek için sıklığa bakılır: kök çok yaygın
    /// ("bunu" 784 bin) ve bitişik hali ondan en az 150 kat nadirse ("bunuda" 176) ayrılır. "evde" ile
    /// "ev" arasında böyle bir fark yoktur. "de/da" için ünlü uyumu da tutmalı.
    /// </summary>
    static string? SplitClitic(string word, Func<string, float?> frequency)
    {
        if (word.Length < 4) return null;
        string clitic = word[^2..];
        if (clitic is not ("da" or "de" or "ki")) return null;
        string stem = word[..^2];
        if (clitic != "ki" && clitic != (BackVowelLast(stem) ? "da" : "de")) return null;

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
