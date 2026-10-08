namespace Duzeltici.Engine;

public enum LanguageMode { Smart, Turkish, English }
public enum Sensitivity { Careful, Balanced, Bold }

/// <summary>
/// Yazılan kelimeye en olası düzeltmeyi bulur. Tek iş parçacığından kullanılır (klavye kancası).
///
/// Puanlama logaritmik: aday = ln(sıklık) − hata maliyeti, yazılan = ln(sıklık) + önyargı.
/// Aday yazılanı geçerse düzeltilir. Böylece sözlükte geçen ama çok nadir olan hatalı biçimler
/// ("temam" 16 kez) çok sık geçen komşularına ("tamam" 764 bin kez) düzelir, gerçek ama nadir
/// kelimeler ("evlerimizden") ise yerinde kalır.
/// </summary>
public sealed class Corrector
{
    // Hata maliyetleri (doğal log birimi): küçük = daha olası bir yazım hatası.
    const float TurkishCharCost = 0.3f;   // s yerine ş: Türkçe karakter kullanmadan yazmak
    const float DeturkishCost = 6.0f;     // ş yerine s: bilerek basılmış Türkçe harf
    const float TransposeCost = 2.0f;     // "teh" → "the"
    const float AdjacentCost = 2.0f;      // komşu tuşa basmak
    const float ReplaceCost = 4.0f;
    const float DoubledDeleteCost = 1.5f; // "okuldaa" → "okulda"
    const float SlipDeleteCost = 2.5f;    // komşu tuşa da basmak
    const float DeleteCost = 3.5f;
    const float DoubledInsertCost = 1.0f; // "helo" → "hello", "teşekürler" → "teşekkürler"
    const float InsertCost = 3.0f;
    const int MaxTurkishSpots = 10;       // en çok 2^10 Türkçe karakter birleşimi

    readonly WordTable?[] _tables = new WordTable?[2];
    readonly HashSet<string> _learned = new(StringComparer.Ordinal);
    readonly Lang[] _recent = new Lang[8];
    int _recentCount, _recentNext;

    // Arama sırasında kullanılan tek kopyalık durum (bellek ayırmamak için).
    WordTable _table = null!;
    Lang _lang;
    readonly byte[] _typed = new byte[Alphabet.MaxWord];
    int _typedLen;
    readonly byte[] _best = new byte[Alphabet.MaxWord + 2];
    int _bestLen;
    float _bestScore, _secondScore;

    public LanguageMode Mode { get; set; } = LanguageMode.Smart;
    public Sensitivity Sensitivity { get; set; } = Sensitivity.Balanced;
    public bool FixTurkishChars { get; set; } = true;

    public void SetTable(Lang lang, WordTable table) => _tables[(int)lang] = table;
    public bool HasTable(Lang lang) => _tables[(int)lang] != null;

    /// <summary>Kullanıcının geri aldığı kelimeyi bir daha düzeltmemek için öğrenir.</summary>
    public bool Learn(string word)
    {
        bool added = _learned.Add(Lower(word, Lang.Tr));
        return _learned.Add(Lower(word, Lang.En)) | added;
    }

    /// <summary>Düzeltilmiş kelimeyi (yazılanın büyük/küçük harf biçimiyle) ya da null döner.</summary>
    public string? Suggest(ReadOnlySpan<char> typed)
    {
        if (typed.Length < 2 || typed.Length > Alphabet.MaxWord) return null;
        bool title = char.IsUpper(typed[0]);
        for (int i = 1; i < typed.Length; i++)
            if (!char.IsLower(typed[i])) return null; // KISALTMA, camelCase vb. dokunma
        if (!title && !char.IsLower(typed[0])) return null;

        if (_learned.Count > 0 && (_learned.Contains(Lower(typed, Lang.Tr)) || _learned.Contains(Lower(typed, Lang.En))))
            return null;

        var (bias, protect, margin, maxDistance) = Sensitivity switch
        {
            Sensitivity.Careful => (5.0f, MathF.Log(300), 1.0f, 1),
            Sensitivity.Bold => (1.5f, MathF.Log(10_000), 0.2f, 2),
            _ => (3.0f, MathF.Log(2_000), 0.5f, 1),
        };

        // Her dil için iki seçenek olabilir: kelimeyi olduğu gibi bırakmak (o dilde varsa) ya da
        // o dildeki en iyi düzeltme. Puanlar dilin toplam sıklığına göre normalleştirilip son
        // kelimelerin diline biraz yakınlık eklenince hepsi aynı ölçekte yarışır. Böylece altyazı
        // listesinde 129 kez geçen "helo" Türkçe sayılıp "hello" düzeltmesini engellemez.
        float keepScore = float.NegativeInfinity, fixScore = float.NegativeInfinity;
        Lang keepLang = default, fixLang = default;
        string? fix = null;
        foreach (var lang in (ReadOnlySpan<Lang>)[Lang.Tr, Lang.En])
        {
            if (Mode == (lang == Lang.Tr ? LanguageMode.English : LanguageMode.Turkish)) continue;
            var o = Evaluate(typed, lang, bias, protect, margin, maxDistance);
            if (!o.Applicable) continue;
            if (o.Protected) return Keep(lang); // çok yaygın bir kelime: asla dokunma

            float shift = ContextBonus(lang) - _tables[(int)lang]!.LogTotal;
            if (o.Known && o.Own + shift > keepScore) (keepScore, keepLang) = (o.Own + shift, lang);
            if (o.Word != null && o.Best + shift > fixScore) (fixScore, fixLang, fix) = (o.Best + shift, lang, o.Word);
        }

        if (fix == null || keepScore >= fixScore)
            return float.IsNegativeInfinity(keepScore) ? null : Keep(keepLang);

        Remember(fixLang);
        return title ? Alphabet.ToUpper(fix[0], fixLang) + fix[1..] : fix;

        string? Keep(Lang lang)
        {
            Remember(lang);
            return null;
        }
    }

    /// <param name="Own">Yazılanın puanı: ln(sıklık) + önyargı.</param>
    /// <param name="Best">En iyi adayın puanı: ln(sıklık) − hata maliyeti.</param>
    /// <param name="Word">Yazılanı açık farkla geçen aday; yoksa null.</param>
    readonly record struct Outcome(bool Applicable, bool Known, bool Protected, float Own, float Best, string? Word);

    Outcome Evaluate(ReadOnlySpan<char> typed, Lang lang, float bias, float protect, float margin, int maxDistance)
    {
        var table = _tables[(int)lang];
        if (table == null || !Alphabet.TryEncode(typed, lang, _typed)) return default;

        _table = table;
        _lang = lang;
        _typedLen = typed.Length;
        var t = _typed.AsSpan(0, _typedLen);

        int q = table.Find(t);
        bool known = q >= 0;
        float own = (known ? q / 16f : 0f) + bias;
        if (known && q / 16f >= protect) return new Outcome(true, true, true, own, 0, null);

        _bestScore = _secondScore = float.NegativeInfinity;
        Edits(t, 1, 0f, cheapOnly: false);
        if (lang == Lang.Tr)
        {
            if (FixTurkishChars) TurkishChars(t);
            // Türkçe klavyede büyük I, ı'nın büyüğüdür: "Istanbul" yazan İ'yi kastetmiştir.
            if (typed[0] == 'I')
            {
                Span<byte> c = stackalloc byte[t.Length];
                t.CopyTo(c);
                c[0] = Alphabet.AsciiTwin(t[0]);
                Consider(c, TurkishCharCost);
            }
        }
        if (float.IsNegativeInfinity(_bestScore) && maxDistance >= 2 && !known && t.Length is >= 5 and <= 12)
            Edits(t, 2, 0f, cheapOnly: false);

        bool wins = _bestScore > own && _bestScore - _secondScore >= margin;
        if (!wins) return new Outcome(true, known, false, own, _bestScore, null);

        var chars = new char[_bestLen];
        for (int i = 0; i < _bestLen; i++) chars[i] = Alphabet.Char(_best[i]);
        return new Outcome(true, known, false, own, _bestScore, new string(chars));
    }

    /// <summary>Tek hatalık (depth=2 ise iki hatalık) adayları üretir. İki hatalıkta ikinci
    /// hata yalnızca ucuz türlerden seçilir (komşu tuş, yer değiştirme, fazla/eksik çift harf):
    /// aday sayısı ~300 binden ~40 bine iner, Cesur mod da birkaç milisaniyede biter.</summary>
    void Edits(ReadOnlySpan<byte> t, int depth, float baseCost, bool cheapOnly)
    {
        int n = t.Length;
        Span<byte> c = stackalloc byte[Alphabet.MaxWord + 1];
        var letters = _lang == Lang.Tr ? Alphabet.TrEdits : Alphabet.EnEdits;

        // Fazla harf: "okuldaa" → "okulda"
        if (n > 1)
        {
            for (int i = 0; i < n; i++)
            {
                t[..i].CopyTo(c);
                t[(i + 1)..].CopyTo(c[i..]);
                byte x = t[i];
                bool doubled = (i > 0 && t[i - 1] == x) || (i < n - 1 && t[i + 1] == x);
                bool slip = (i > 0 && Alphabet.Adjacent(t[i - 1], x)) || (i < n - 1 && Alphabet.Adjacent(t[i + 1], x));
                if (cheapOnly && !doubled && !slip) continue;
                Emit(c[..(n - 1)], depth, baseCost + (doubled ? DoubledDeleteCost : slip ? SlipDeleteCost : DeleteCost));
            }
        }

        // Yer değiştirmiş harfler: "teh" → "the"
        for (int i = 0; i < n - 1; i++)
        {
            if (t[i] == t[i + 1]) continue;
            t.CopyTo(c);
            (c[i], c[i + 1]) = (c[i + 1], c[i]);
            Emit(c[..n], depth, baseCost + TransposeCost);
        }

        // Yanlış harf: "temam" → "tamam"
        t.CopyTo(c);
        for (int i = 0; i < n; i++)
        {
            byte x = t[i];
            foreach (byte y in letters)
            {
                if (y == x) continue;
                float cost = FixTurkishChars && y == Alphabet.TurkishTwin(x) ? TurkishCharCost
                    : y == Alphabet.AsciiTwin(x) ? DeturkishCost
                    : Alphabet.Adjacent(x, y) ? AdjacentCost
                    : ReplaceCost;
                if (cheapOnly && cost > AdjacentCost) continue;
                c[i] = y;
                Emit(c[..n], depth, baseCost + cost);
            }
            c[i] = x;
        }

        // Eksik harf: "gelyorum" → "geliyorum"
        if (n < Alphabet.MaxWord)
        {
            for (int i = 0; i <= n; i++)
            {
                t[..i].CopyTo(c);
                t[i..].CopyTo(c[(i + 1)..]);
                if (cheapOnly)
                {
                    // yalnızca çift harf: "helo" → "hello"
                    if (i > 0) { c[i] = t[i - 1]; Emit(c[..(n + 1)], depth, baseCost + DoubledInsertCost); }
                    continue;
                }
                foreach (byte y in letters)
                {
                    c[i] = y;
                    bool doubled = (i > 0 && t[i - 1] == y) || (i < n && t[i] == y);
                    Emit(c[..(n + 1)], depth, baseCost + (doubled ? DoubledInsertCost : InsertCost));
                }
            }
        }
    }

    void Emit(ReadOnlySpan<byte> candidate, int depth, float cost)
    {
        if (depth > 1) Edits(candidate, depth - 1, cost, cheapOnly: true);
        else Consider(candidate, cost);
    }

    /// <summary>"calisiyorum" → "çalışıyorum": c/g/i/o/s/u harflerinin Türkçe karşılıklarının
    /// tüm birleşimleri. Tek harflik olanları Edits zaten deniyor.</summary>
    void TurkishChars(ReadOnlySpan<byte> t)
    {
        Span<int> spots = stackalloc int[MaxTurkishSpots];
        int k = 0;
        for (int i = 0; i < t.Length && k < MaxTurkishSpots; i++)
            if (Alphabet.TurkishTwin(t[i]) != 0) spots[k++] = i;
        if (k < 2) return;

        Span<byte> c = stackalloc byte[t.Length];
        for (int mask = 1; mask < 1 << k; mask++)
        {
            int changed = BitOperationsPopCount(mask);
            if (changed < 2) continue;
            t.CopyTo(c);
            for (int b = 0; b < k; b++)
                if ((mask & (1 << b)) != 0) c[spots[b]] = Alphabet.TurkishTwin(t[spots[b]]);
            Consider(c, changed * TurkishCharCost);
        }
    }

    static int BitOperationsPopCount(int v) => System.Numerics.BitOperations.PopCount((uint)v);

    void Consider(ReadOnlySpan<byte> candidate, float cost)
    {
        int q = _table.Find(candidate);
        if (q < 0) return;
        if (candidate.SequenceEqual(_typed.AsSpan(0, _typedLen))) return;

        float score = q / 16f - cost;
        if (candidate.SequenceEqual(_best.AsSpan(0, _bestLen)) && !float.IsNegativeInfinity(_bestScore))
        {
            if (score > _bestScore) _bestScore = score;
            return;
        }
        if (score > _bestScore)
        {
            _secondScore = _bestScore;
            _bestScore = score;
            candidate.CopyTo(_best);
            _bestLen = candidate.Length;
        }
        else if (score > _secondScore)
        {
            _secondScore = score;
        }
    }

    float ContextBonus(Lang lang)
    {
        int same = 0;
        for (int i = 0; i < _recentCount; i++) if (_recent[i] == lang) same++;
        return 1.5f * (2 * same - _recentCount) / _recent.Length;
    }

    void Remember(Lang lang)
    {
        _recent[_recentNext] = lang;
        _recentNext = (_recentNext + 1) % _recent.Length;
        if (_recentCount < _recent.Length) _recentCount++;
    }

    static string Lower(ReadOnlySpan<char> s, Lang lang)
    {
        var chars = new char[s.Length];
        for (int i = 0; i < s.Length; i++) chars[i] = Alphabet.ToLower(s[i], lang);
        return new string(chars);
    }
}
