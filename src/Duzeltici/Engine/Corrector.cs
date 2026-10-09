namespace Duzeltici.Engine;

public enum LanguageMode { Smart, Turkish, English }
public enum Sensitivity { Careful, Balanced, Bold }

/// <summary>
/// Yazılan kelimeye en olası düzeltmeyi bulur. Tek iş parçacığından kullanılır (klavye kancası);
/// yalnızca öğrenilen kelimeler arayüzden de değişebildiği için kilitlidir.
///
/// Puanlama logaritmik: aday = ln(sıklık) − hata maliyeti, yazılan = ln(sıklık) + önyargı.
/// Aday yazılanı açık farkla geçerse düzeltilir.
///
/// İlke: yanlış düzeltme, düzeltmemekten çok daha kötüdür. Bu yüzden şunlara dokunulmaz:
/// sık geçen kelimeler, Türkçe ek yapısına uyan bilinmeyen kelimeler ("numunelerimi"), kısa
/// bilinmeyen kelimeler (exe, dk, ing) — ucuz bir hata değilse, q/w/x içeren yabancı kelimeler,
/// cümle ortasında büyük harfle başlayan bilinmeyen kelimeler (isimler).
/// Değişikliklerin etkisi tests/Duzeltici.Tests --olcum ile binlerce kelimede ölçülür.
/// </summary>
public sealed class Corrector
{
    // Hata maliyetleri (doğal log birimi): küçük = daha olası bir yazım hatası.
    const float TransposeCost = 2.0f;      // "teh" → "the"
    const float AdjacentCost = 2.2f;       // komşu tuşa basmak (kısa bilinmeyen kelimelerde izin verilmez)
    const float VowelSwapCost = 3.0f;      // "tımam" → "tamam": ünlüleri karıştırmak
    const float ReplaceCost = 5.0f;        // uzak bir tuş: yazım hatasından çok başka bir kelime
    const float DoubledDeleteCost = 1.5f;  // "okuldaa" → "okulda"
    const float SlipDeleteCost = 2.5f;     // yandaki tuşa da basmak
    const float DeleteCost = 3.5f;
    const float DoubledInsertCost = 1.0f;  // "helo" → "hello", "teşekürler" → "teşekkürler"
    const float VowelInsertCost = 2.5f;    // hızlı yazarken en çok ünlüler atlanır: "işlvli"
    const float InsertCost = 3.0f;

    // Türkçe karakterler. Kullanıcı hiç Türkçe karakter kullanmıyorsa (ASCII alışkanlığı) "s yerine ş"
    // neredeyse kesin bir düzeltmedir; Türkçe klavyeyle yazıyorsa ı/i, o/ö, u/ü karışması orta
    // olasılıklı bir hatadır ve iki yönde de olur ("oldü" → "oldu", "şımdı" → "şimdi").
    const float AsciiHabitCost = 0.3f;
    const float DeturkishCost = 6.0f;      // ASCII alışkanlığında ş → s: bilerek basılmış harf
    const float DotSwapCost = 1.5f;        // ı ↔ i
    const float TwinSwapCost = 2.0f;       // o↔ö, u↔ü, s↔ş, c↔ç, g↔ğ
    const int MaxTwinSpots = 10;
    const int TurkishEvidenceWords = 20;   // son 20 kelimede Türkçe karakter varsa Türkçe klavye

    const float ShortKnownBonus = 3.0f;    // 2–3 harflik bilinen kelimeler ("mer", "ece") komşularla dolu
    const float CheapCost = 2.2f;          // doğru görünen kelimede serbest olan en pahalı hata (komşu tuş dahil)
    static readonly float StrongDominance = MathF.Log(1000); // ondan pahalısı: doğru biçim 1000 kat sık olmalı
    const float UnknownStrongMargin = 3.0f; // bilinmeyen kelimede uzak harf değişimi için ek pay
    const float DominanceMaxCost = 1.0f;   // sık kelimede: yalnızca çift harf ya da Türkçe karakter
    static readonly float Dominance = MathF.Log(100);   // sık kelimede: doğru biçim en az 100 kat sık olmalı
    static readonly float RareLogFreq = MathF.Log(20);  // sözlükte 20'den az: büyük ihtimalle altyazı hatası
    static readonly float PlausibleLogFreq = MathF.Log(30); // ek yapısına uyan bilinmeyen kelime ≈ 30 kez geçmiş gibi
    static readonly float AsciiHabitDominance = MathF.Log(20); // sık kelimede bile: Türkçe biçimi 20 kat sıksa

    /// <summary>Günlük dil biçimleri sözlükte ya yok ya da çok az geçer; sıklıkları standart biçimden
    /// alınır, kullanıcının üslubu korunur ("düzltmiyo" → "düzeltmiyo", "olmıcak" → "olmıycak").</summary>
    static readonly (byte[] Colloquial, byte[] Standard)[] s_colloquial = new[]
    {
        ("yosunuz", "yorsunuz"), ("yosun", "yorsun"), ("yonuz", "yorsunuz"), ("yolar", "yorlar"), ("yom", "yorum"), ("yon", "yorsun"), ("yoz", "yoruz"), ("yo", "yor"),
        ("mıycam", "mayacağım"), ("miycem", "meyeceğim"), ("mıycak", "mayacak"), ("miycek", "meyecek"),
        ("ıcam", "acağım"), ("icem", "eceğim"), ("ucam", "acağım"), ("ücem", "eceğim"),
        ("ıcak", "acak"), ("icek", "ecek"), ("ucak", "acak"), ("ücek", "ecek"),
        ("cam", "acağım"), ("cem", "eceğim"), ("cak", "acak"), ("cek", "ecek"),
    }.Select(p => (Alphabet.Encode(p.Item1), Alphabet.Encode(p.Item2))).ToArray();

    readonly record struct Profile(float Bias, float UnknownBias, float Protect, float Margin, int MaxDistance, int BudgetMs);

    static Profile ProfileFor(Sensitivity s) => s switch
    {
        Sensitivity.Careful => new(5.0f, 4.0f, MathF.Log(20), 1.0f, 1, 1),
        Sensitivity.Bold => new(2.0f, 1.5f, MathF.Log(200), 0.3f, 2, 4),
        _ => new(3.0f, 2.5f, MathF.Log(50), 0.7f, 2, 1),
    };

    readonly WordTable?[] _tables = new WordTable?[2];
    readonly Morphology?[] _morphology = new Morphology?[2];
    readonly HashSet<string> _protected = new(StringComparer.Ordinal);

    // Öğrenilenler klavye iş parçacığında eklenir, arayüzden temizlenebilir; kilit bu ikisi için.
    readonly Lock _gate = new();
    readonly HashSet<string> _learned = new(StringComparer.Ordinal);  // kalıcı (ogrenilen.txt)
    readonly HashSet<string> _rejected = new(StringComparer.Ordinal); // bu oturumda bir kez geri alındı

    readonly Lang[] _recent = new Lang[8];
    int _recentCount, _recentNext;
    int _wordsSinceTurkish = int.MaxValue / 2;

    // Arama sırasında kullanılan tek kopyalık durum (bellek ayırmamak için).
    WordTable _table = null!;
    Lang _lang;
    readonly byte[] _typed = new byte[Alphabet.MaxWord];
    int _typedLen;
    readonly byte[] _best = new byte[Alphabet.MaxWord + 2];
    int _bestLen;
    float _bestScore, _secondScore, _maxCost;
    float _strongFrom, _strongNeeded; // bu maliyetin üstündeki adaylar en az _strongNeeded puan almalı
    long _deadline;
    bool _outOfTime;
    List<string>? _trace; // yalnızca Explain sırasında dolu

    public LanguageMode Mode { get; set; } = LanguageMode.Smart;
    public Sensitivity Sensitivity { get; set; } = Sensitivity.Balanced;
    /// <summary>ASCII alışkanlığıyla yazılanları tamamla: "calisiyorum" → "çalışıyorum".</summary>
    public bool FixTurkishChars { get; set; } = true;
    /// <summary>TDK yazım kuralları: "herşey" → "her şey", "bunuda" → "bunu da", "diyip" → "deyip".</summary>
    public bool SpellingRules { get; set; } = true;

    /// <summary>Sözlüğün ek istatistiğini çıkarır (yüz milisaniyeler sürer; arka planda çağrılmalı).</summary>
    public static Morphology BuildMorphology(Lang lang, WordTable table) =>
        // Tek harflik sonlar: Türkçede yalnızca ünlüler (belirtme, yönelme) ve m/n (iyelik); İngilizcede -s, -d, -r, -y.
        Morphology.Build(table, lang == Lang.Tr ? "aeıiuümn" : "sdry");

    /// <summary>Sözlüğü ekler. Klavye iş parçacığı aynı anda okuyabilir: önce ek bilgisi, sonra sözlük
    /// atanır, böylece sözlüğü gören kod ek bilgisini de hazır bulur.</summary>
    public void SetTable(Lang lang, WordTable table, Morphology? morphology = null)
    {
        _morphology[(int)lang] = morphology ?? BuildMorphology(lang, table);
        _tables[(int)lang] = table;
    }

    public bool HasTable(Lang lang) => _tables[(int)lang] != null;

    /// <summary>korunan.txt biçimi: boşlukla ayrılmış küçük harfli kelimeler, # ile başlayan satırlar açıklama.</summary>
    public void LoadProtected(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('#')) continue;
            foreach (var word in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) _protected.Add(word);
        }
    }

    /// <summary>Kelimeyi kalıcı olarak öğrenir (ogrenilen.txt'den yüklerken).</summary>
    public void Learn(string word)
    {
        lock (_gate)
        {
            _learned.Add(Lower(word, Lang.Tr));
            _learned.Add(Lower(word, Lang.En));
        }
    }

    /// <summary>
    /// Kullanıcı düzeltmeyi Backspace ile geri aldı. İlk seferde kelime yalnızca bu oturumda rahat
    /// bırakılır: Backspace'e çoğu zaman sadece boşluğu silmek için de basılır. Aynı kelime ikinci kez
    /// geri alınırsa gerçekten böyle yazılmak isteniyordur; kalıcı öğrenilir.
    /// </summary>
    /// <returns>true: kalıcı olarak öğrenildi (dosyaya yazılmalı).</returns>
    public bool Reject(string word)
    {
        string key = Lower(word, Lang.Tr);
        lock (_gate)
        {
            if (_rejected.Add(key)) return false;
            _learned.Add(key);
            _learned.Add(Lower(word, Lang.En));
            return true;
        }
    }

    /// <summary>Kalıcı öğrenilen kelimeler (Türkçe küçük harfle, sıralı).</summary>
    public string[] LearnedWords(IEnumerable<string> saved)
    {
        lock (_gate) return saved.Select(w => Lower(w, Lang.Tr)).Where(_learned.Contains).Distinct().Order(StringComparer.Ordinal).ToArray();
    }

    public void ClearLearned()
    {
        lock (_gate)
        {
            _learned.Clear();
            _rejected.Clear();
        }
    }

    /// <summary>Son kelimelerin dilini ve klavye alışkanlığını unutur (ölçümde her kelime bağımsız olsun diye).</summary>
    public void ResetContext()
    {
        _recentCount = _recentNext = 0;
        _wordsSinceTurkish = int.MaxValue / 2;
    }

    /// <summary>Ölçüm için: kullanıcının Türkçe klavyeyle (ş, ı, ü... yazarak) yazdığını varsay.</summary>
    public void AssumeTurkishKeyboard() => _wordsSinceTurkish = 0;

    /// <summary>Son kelimelerde hiç Türkçe karakter yoksa kullanıcı ASCII alışkanlığıyla yazıyordur.</summary>
    bool AsciiHabit => _wordsSinceTurkish >= TurkishEvidenceWords;

    /// <summary>Düzeltilmiş kelimeyi (yazılanın büyük/küçük harf biçimiyle) ya da null döner.</summary>
    /// <param name="sentenceStart">Kelime cümlenin başında mı? Cümle ortasında büyük harfle başlayan ve
    /// sözlükte olmayan kelime büyük ihtimalle bir isimdir (Hazal, Antigravity); ona dokunulmaz.</param>
    public string? Suggest(ReadOnlySpan<char> typed, bool sentenceStart = true)
    {
        if (typed.Length < 2 || typed.Length > Alphabet.MaxWord) return null;
        bool title = char.IsUpper(typed[0]);
        for (int i = 1; i < typed.Length; i++)
            if (!char.IsLower(typed[i])) return null; // KISALTMA, camelCase vb. dokunma
        if (!title && !char.IsLower(typed[0])) return null;

        // Klavye alışkanlığını güncelle (bu kelime kendi kararını etkilemesin diye önce oku).
        bool asciiHabit = AsciiHabit;
        if (typed.ContainsAny("çğıöşüÇĞİÖŞÜ")) _wordsSinceTurkish = 0;
        else if (_wordsSinceTurkish < int.MaxValue / 2) _wordsSinceTurkish++;

        string lowerTr = Lower(typed, Lang.Tr);
        if (_protected.Contains(lowerTr) || _protected.Contains(Lower(typed, Lang.En))) return null;
        lock (_gate)
        {
            if (_rejected.Contains(lowerTr) || _learned.Contains(lowerTr) || _learned.Contains(Lower(typed, Lang.En))) return null;
        }

        if (SpellingRules && Mode != LanguageMode.English && _tables[(int)Lang.Tr] != null)
        {
            string? rule = TurkishRules.Apply(lowerTr, TurkishFrequency);
            if (rule != null)
            {
                Remember(Lang.Tr);
                return title ? Alphabet.ToUpper(rule[0], Lang.Tr) + rule[1..] : rule;
            }
        }

        var profile = ProfileFor(Sensitivity);
        _deadline = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * profile.BudgetMs / 1000;
        _outOfTime = false;
        // Cümle ortasında büyük harfle başlayan bilinmeyen kelime çoğu zaman bir isimdir (Hazal, Burak).
        // İsimler yaygın bir kelimeye ancak pahalı bir değişiklikle benzer (z→y, u→ı); büyük harfle yazılmış
        // bir hata ("Merhab") ise ucuz bir hatayla. Bu yüzden isim gibi görünenlerde yalnızca ucuz düzeltme.
        bool nameLike = title && !sentenceStart;

        // Her dil için iki seçenek olabilir: kelimeyi olduğu gibi bırakmak (o dilde varsa ya da ek
        // yapısına uyuyorsa) ya da o dildeki en iyi düzeltme. Puanlar dilin toplam sıklığına göre
        // normalleştirilip son kelimelerin diline biraz yakınlık eklenince hepsi aynı ölçekte yarışır.
        float keepScore = float.NegativeInfinity, fixScore = float.NegativeInfinity;
        Lang keepLang = default, fixLang = default;
        string? fix = null;
        foreach (var lang in (ReadOnlySpan<Lang>)[Lang.Tr, Lang.En])
        {
            if (Mode == (lang == Lang.Tr ? LanguageMode.English : LanguageMode.Turkish)) continue;
            var o = Evaluate(typed, lang, profile, asciiHabit, nameLike);
            if (!o.Applicable) continue;
            if (o.Protected && o.Word == null) return Keep(lang); // sık kelime: dokunma

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

    /// <summary>Geliştirme aracı: bir kelime için her dildeki kararı ve en iyi adayları açıklar.</summary>
    public string Explain(string typed, bool sentenceStart = true)
    {
        var sb = new System.Text.StringBuilder();
        var profile = ProfileFor(Sensitivity);
        foreach (var lang in (ReadOnlySpan<Lang>)[Lang.Tr, Lang.En])
        {
            _trace = [];
            _deadline = long.MaxValue;
            var o = Evaluate(typed, lang, profile, AsciiHabit, char.IsUpper(typed[0]) && !sentenceStart);
            sb.AppendLine($"[{lang}] uygun={o.Applicable} biliniyor/ek uyumlu={o.Known} sık={o.Protected} yazılanın puanı={o.Own:F2} en iyi={o.Best:F2} → {o.Word ?? "(dokunma)"}");
            foreach (var line in _trace.OrderByDescending(l => float.Parse(l[..7], System.Globalization.CultureInfo.InvariantCulture)).Take(6)) sb.AppendLine("    " + line);
        }
        _trace = null;
        sb.AppendLine($"Sonuç: {Suggest(typed, sentenceStart) ?? "(dokunma)"}");
        return sb.ToString();
    }

    /// <param name="Known">Sözlükte var ya da ek yapısına uyuyor (olduğu gibi bırakmak bir seçenek).</param>
    /// <param name="Own">Yazılanın puanı: ln(sıklık) + önyargı.</param>
    /// <param name="Best">En iyi adayın puanı: ln(sıklık) − hata maliyeti.</param>
    /// <param name="Word">Yazılanı açık farkla geçen aday; yoksa null.</param>
    readonly record struct Outcome(bool Applicable, bool Known, bool Protected, float Own, float Best, string? Word);

    Outcome Evaluate(ReadOnlySpan<char> typed, Lang lang, Profile p, bool asciiHabit, bool nameLike)
    {
        var table = _tables[(int)lang];
        if (table == null || !Alphabet.TryEncode(typed, lang, _typed)) return default;

        _table = table;
        _lang = lang;
        _typedLen = typed.Length;
        var t = _typed.AsSpan(0, _typedLen);
        int n = t.Length;

        int foreignLetters = 0;
        if (lang == Lang.Tr)
            foreach (byte b in t)
                if (Alphabet.IsForeign(b)) foreignLetters++;

        // Türkçe ek almış yabancı kelime ("windowsda", "commitledim"): İngilizce kök + Türkçe son.
        var english = _tables[(int)Lang.En];
        bool foreignStemmed = lang == Lang.Tr && english != null && _morphology[(int)Lang.Tr]!.Plausible(t, english, minStemLength: 3, ignoreVoicing: true);
        _bestScore = _secondScore = float.NegativeInfinity;
        _strongFrom = float.MaxValue;
        _strongNeeded = 0;

        if (foreignLetters > 0)
        {
            // q/w/x Türkçede yok: yabancı ya da teknik bir kelime (exe, windows, wifi); Türkçe ek almışsa
            // olduğu gibi kalır. Tek istisna: Türkçe Q klavyede w ve q gerçekten komşu tuşlar; uzun bir
            // kelimede tek bir w/q/x varsa ve onu silmek/değiştirmek ucuz bir hataysa ("işwreti") düzelt.
            if (foreignStemmed) return new Outcome(true, true, true, PlausibleLogFreq, 0, null);
            if (foreignLetters > 1 || n < 5) return default;
            _maxCost = SlipDeleteCost;
            Edits(t, 1, 0f, cheapOnly: true, asciiHabit);
            bool fixes = _bestScore > p.UnknownBias && _bestScore - _secondScore >= p.Margin && !BestHasForeignLetter();
            return new Outcome(true, false, false, p.UnknownBias, _bestScore, fixes ? BestWord() : null);
        }

        int q = Lookup(t);
        bool known = q >= 0;
        bool plausible = foreignStemmed || _morphology[(int)lang]?.Plausible(t, table) == true;

        float freq = known ? q / 16f : PlausibleLogFreq;
        float own;

        if (known && freq >= p.Protect)
        {
            // Sık geçen bir kelime: yalnızca en ucuz hatalarla (çift harf, Türkçe karakter) ve doğru
            // biçimi çok daha sıksa düzeltilir. Altyazı listesinde "teşekürler" yüzlerce kez geçer ama
            // "teşekkürler"in yanında bir yazım hatasıdır. ASCII alışkanlığında "nasilsin" de böyle.
            _maxCost = DominanceMaxCost;
            if (n >= 4) Edits(t, 1, 0f, cheapOnly: true, asciiHabit);
            // ASCII alışkanlığında Türkçe karakter birleşimleri, kaç harf olursa olsun. Türkçe klavyeyle
            // yazan biri ise "ş"yi bilerek basmıştır: sık bir kelimede ("şen") Türkçe karaktere dokunulmaz.
            _maxCost = float.MaxValue;
            if (lang == Lang.Tr && asciiHabit) TwinVariants(t, asciiHabit, maxChanges: MaxTwinSpots);
            float needed = freq + (asciiHabit && lang == Lang.Tr ? AsciiHabitDominance : Dominance);
            bool dominant = _bestScore >= needed && _bestScore - _secondScore >= p.Margin;
            own = freq + p.Bias;
            return new Outcome(true, true, true, own, _bestScore, dominant ? BestWord() : null);
        }

        // Yazılan kelime ne kadar "doğru görünüyor"?
        //  - Türkçe ses yapısına aykırı ("işlvli": üç ünsüz yan yana) ya da sözlükte çok az geçen ve ek
        //    yapısına da uymayan: neredeyse kesin yazım hatası; her düzeltme denenir.
        //  - Sözlükte orta sıklıkta ya da ek yapısına uyuyor: muhtemelen doğru. Ucuz yazım hataları
        //    (komşu tuş, yer değiştirme, çift harf, Türkçe karakter) serbest; pahalı bir değişiklik ancak
        //    doğru biçim 1000 kat daha sıksa yapılır ("temam" → "tamam" evet; "osur" → "olur", "acak" →
        //    "ancak" hayır: bunlar başka kelimeler).
        //  - Bilinmeyen kısa kelime (exe, dk, ing): komşuluk çok yoğun; yalnızca yer değiştirme, çift harf,
        //    Türkçe karakter.
        bool impossible = lang == Lang.Tr && Alphabet.ViolatesTurkishPhonotactics(t) && !StartsWithEnglishWord(t);
        bool looksValid = !impossible && (plausible || nameLike || (known && freq >= RareLogFreq));
        own = known || looksValid
            ? freq + p.Bias + (n <= 3 ? ShortKnownBonus : 0)
            : impossible ? 0f : p.UnknownBias;
        if (looksValid)
        {
            _maxCost = float.MaxValue;
            _strongFrom = nameLike && !known && !plausible ? VowelInsertCost : CheapCost;
            _strongNeeded = freq + StrongDominance;
        }
        else
        {
            _maxCost = impossible || known || n >= 5 ? float.MaxValue : TransposeCost;
            // Bilinmeyen kelimede de uzak harf değişimi ("pushla" → "pusula") başka bir kelime demektir;
            // ancak doğru biçim çok yaygınsa yapılır. Ünlü karışması ve harf eksikliği bu sınırın altında.
            if (!impossible)
            {
                _strongFrom = DeleteCost;
                _strongNeeded = own + UnknownStrongMargin;
            }
        }

        Edits(t, 1, 0f, cheapOnly: false, asciiHabit);
        if (lang == Lang.Tr)
        {
            TwinVariants(t, asciiHabit, maxChanges: asciiHabit ? MaxTwinSpots : 2);
            // Türkçe klavyede büyük I, ı'nın büyüğüdür; "Istanbul" yazan İ'yi kastetmiştir.
            if (typed[0] == 'I' && Alphabet.AsciiTwin(t[0]) != 0)
            {
                Span<byte> c = stackalloc byte[n];
                t.CopyTo(c);
                c[0] = Alphabet.AsciiTwin(t[0]);
                Consider(c, asciiHabit ? AsciiHabitCost : DotSwapCost);
            }
        }
        if (float.IsNegativeInfinity(_bestScore) && p.MaxDistance >= 2 && !known && !plausible && n is >= 5 and <= 12)
            Edits(t, 2, 0f, cheapOnly: false, asciiHabit);

        bool wins = _bestScore > own && _bestScore - _secondScore >= p.Margin;
        return new Outcome(true, known || plausible, false, own, _bestScore, wins ? BestWord() : null);
    }

    /// <summary>"pushla", "commitle": baştaki İngilizce kelime ses yapısı kuralını geçersiz kılar.</summary>
    bool StartsWithEnglishWord(ReadOnlySpan<byte> t)
    {
        var english = _tables[(int)Lang.En];
        if (english == null) return false;
        for (int k = t.Length - 1; k >= 4; k--)
            if (english.Find(t[..k]) >= 0) return true;
        return false;
    }

    bool BestHasForeignLetter()
    {
        foreach (byte b in _best.AsSpan(0, _bestLen)) if (Alphabet.IsForeign(b)) return true;
        return false;
    }

    string BestWord()
    {
        var chars = new char[_bestLen];
        for (int i = 0; i < _bestLen; i++) chars[i] = Alphabet.Char(_best[i]);
        return new string(chars);
    }

    /// <summary>Bir harfin ikiziyle değiştirilmesinin maliyeti (c↔ç, ı↔i...); ikiz değilse -1.</summary>
    float TwinCost(byte from, byte to, bool asciiHabit)
    {
        bool toTurkish = Alphabet.TurkishTwin(from) == to;
        if (!toTurkish && Alphabet.AsciiTwin(from) != to) return -1;
        if (asciiHabit) return toTurkish && FixTurkishChars ? AsciiHabitCost : DeturkishCost;
        return Alphabet.IsDotPair(from, to) ? DotSwapCost : TwinSwapCost;
    }

    /// <summary>Tek hatalık (depth=2 ise iki hatalık) adayları üretir. İki hatalıkta ikinci hata
    /// yalnızca ucuz türlerden seçilir; aday sayısı ~300 binden ~40 bine iner.</summary>
    void Edits(ReadOnlySpan<byte> t, int depth, float baseCost, bool cheapOnly, bool asciiHabit)
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
                Emit(c[..(n - 1)], depth, baseCost + (doubled ? DoubledDeleteCost : slip ? SlipDeleteCost : DeleteCost), asciiHabit);
            }
        }

        // Yer değiştirmiş harfler: "teh" → "the"
        for (int i = 0; i < n - 1; i++)
        {
            if (t[i] == t[i + 1]) continue;
            t.CopyTo(c);
            (c[i], c[i + 1]) = (c[i + 1], c[i]);
            Emit(c[..n], depth, baseCost + TransposeCost, asciiHabit);
        }

        // Yanlış harf: "temam" → "tamam"
        t.CopyTo(c);
        for (int i = 0; i < n; i++)
        {
            byte x = t[i];
            foreach (byte y in letters)
            {
                if (y == x) continue;
                float cost = TwinCost(x, y, asciiHabit);
                if (cost < 0)
                    cost = Alphabet.Adjacent(x, y) ? AdjacentCost
                        : Alphabet.IsVowel(x) && Alphabet.IsVowel(y) ? VowelSwapCost
                        : ReplaceCost;
                // İki hatalık aramada uzak harf değişimi atlanır (işin yarısı, isabeti düşük).
                if ((cheapOnly || depth > 1) && cost > AdjacentCost) continue;
                c[i] = y;
                Emit(c[..n], depth, baseCost + cost, asciiHabit);
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
                    // çift harf ya da en sık atlanan harfler (ünlüler, r, n, l...)
                    if (i > 0) { c[i] = t[i - 1]; Emit(c[..(n + 1)], depth, baseCost + DoubledInsertCost, asciiHabit); }
                    foreach (byte y in _lang == Lang.Tr ? Alphabet.TrCommonInserts : Alphabet.EnCommonInserts)
                    {
                        if (i > 0 && y == t[i - 1]) continue;
                        c[i] = y;
                        Emit(c[..(n + 1)], depth, baseCost + (Alphabet.IsVowel(y) ? VowelInsertCost : InsertCost), asciiHabit);
                    }
                    continue;
                }
                foreach (byte y in letters)
                {
                    c[i] = y;
                    bool doubled = (i > 0 && t[i - 1] == y) || (i < n && t[i] == y);
                    Emit(c[..(n + 1)], depth, baseCost + (doubled ? DoubledInsertCost : Alphabet.IsVowel(y) ? VowelInsertCost : InsertCost), asciiHabit);
                }
            }
        }
    }

    void Emit(ReadOnlySpan<byte> candidate, int depth, float cost, bool asciiHabit)
    {
        if (depth == 1)
        {
            Consider(candidate, cost);
            return;
        }
        if (_outOfTime || System.Diagnostics.Stopwatch.GetTimestamp() > _deadline)
        {
            _outOfTime = true;
            return;
        }
        Edits(candidate, depth - 1, cost, cheapOnly: true, asciiHabit);
    }

    /// <summary>
    /// Türkçe karakter ikizlerinin birleşimleri: "calisiyorum" → "çalışıyorum" (ASCII alışkanlığı, çok
    /// sayıda değişiklik), "şımdı" → "şimdi" (Türkçe klavye, en çok 2 değişiklik). Tek değişiklik
    /// Edits'te de denenir.
    /// </summary>
    void TwinVariants(ReadOnlySpan<byte> t, bool asciiHabit, int maxChanges)
    {
        Span<int> spots = stackalloc int[MaxTwinSpots];
        int k = 0;
        for (int i = 0; i < t.Length && k < MaxTwinSpots; i++)
        {
            byte twin = Alphabet.TurkishTwin(t[i]) != 0 ? Alphabet.TurkishTwin(t[i]) : Alphabet.AsciiTwin(t[i]);
            if (twin != 0 && TwinCost(t[i], twin, asciiHabit) < DeturkishCost) spots[k++] = i;
        }
        if (k == 0) return;

        Span<byte> c = stackalloc byte[t.Length];
        for (int mask = 1; mask < 1 << k; mask++)
        {
            int changed = System.Numerics.BitOperations.PopCount((uint)mask);
            if (changed > maxChanges) continue;
            t.CopyTo(c);
            float cost = 0;
            for (int b = 0; b < k; b++)
            {
                if ((mask & (1 << b)) == 0) continue;
                byte x = t[spots[b]];
                byte twin = Alphabet.TurkishTwin(x) != 0 ? Alphabet.TurkishTwin(x) : Alphabet.AsciiTwin(x);
                c[spots[b]] = twin;
                cost += TwinCost(x, twin, asciiHabit);
            }
            Consider(c, cost);
        }
    }

    /// <summary>Sözlükte arar; Türkçede günlük dil biçimini de dener ("diyo" → "diyor" sıklığı) ve
    /// ikisinden büyüğünü alır: "diyo" altyazılarda 106 kez geçer ama aslında "diyor" kadar yaygındır.</summary>
    int Lookup(ReadOnlySpan<byte> word)
    {
        int q = _table.Find(word);
        if (_lang != Lang.Tr) return q;
        Span<byte> full = stackalloc byte[Alphabet.MaxWord];
        foreach (var (colloquial, standard) in s_colloquial)
        {
            if (word.Length < colloquial.Length + 2 || !word.EndsWith(colloquial)) continue;
            int stem = word.Length - colloquial.Length;
            if (stem + standard.Length > Alphabet.MaxWord) continue;
            word[..stem].CopyTo(full);
            standard.CopyTo(full[stem..]);
            q = Math.Max(q, _table.Find(full[..(stem + standard.Length)]));
        }
        return q;
    }

    float? TurkishFrequency(string word)
    {
        Span<byte> codes = stackalloc byte[Alphabet.MaxWord];
        if (word.Length > Alphabet.MaxWord || !Alphabet.TryEncode(word, Lang.Tr, codes)) return null;
        (_table, _lang) = (_tables[(int)Lang.Tr]!, Lang.Tr);
        int q = Lookup(codes[..word.Length]);
        return q < 0 ? null : q / 16f;
    }

    void Consider(ReadOnlySpan<byte> candidate, float cost)
    {
        if (cost > _maxCost) return;
        int q = Lookup(candidate);
        if (q < 0) return;
        if (cost > _strongFrom && q / 16f - cost < _strongNeeded) return; // doğru görünen kelimede pahalı değişiklik
        if (_trace != null)
        {
            var chars = new char[candidate.Length];
            for (int i = 0; i < chars.Length; i++) chars[i] = Alphabet.Char(candidate[i]);
            _trace.Add($"{q / 16f - cost,7:F2}  {new string(chars),-18} ln(sıklık)={q / 16f:F2} maliyet={cost:F1}");
        }
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
