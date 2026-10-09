using System.Diagnostics;
using Duzeltici.Engine;

/// <summary>
/// Düzelticinin isabetini binlerce kelimeyle ölçer. Tek tek örnek eklemek ("exe düzelmesin") bir
/// yanlışı kapatırken başkasını açabiliyor; bu ölçüm her değişikliğin bütün tabloya etkisini gösterir.
///
///  1. Sözlükteki doğru kelimeler        → hiçbiri değişmemeli
///  2. Sözlükte OLMAYAN doğru kelimeler   → hiçbiri değişmemeli. Taklit: nadir kelimeleri sözlükten
///     çıkarıp yükleriz; "exe", isimler, nadir çekimler bu sınıfta.
///  3. Elle seçilmiş dokunulmayacaklar    → teknik terim, isim, günlük dil, kullanıcının yazdıkları
///  4. Gerçek yazım hataları               → beklenen düzeltme
///  5. Üretilmiş yazım hataları            → komşu tuş, eksik/fazla harf, yer değiştirme, ı/i...
///
/// En kötü sonuç "yanlış düzeltme"dir (kelimeyi başka bir kelimeye çevirmek); "dokunmadı" ondan iyidir.
/// </summary>
static class Evaluation
{
    // Kalite sınırları. Bunlar aşılırsa test (ve CI) başarısız olur.
    const double MaxKnownFalseFix = 0.003;    // sözlükteki doğru kelimelerin en çok %0,3'ü değişebilir
    const double MaxUnknownFalseFix = 0.04;   // sözlükte olmayan doğru kelimelerin en çok %4'ü
    const double MaxSyntheticWrong = 0.05;    // üretilmiş hataların en çok %5'i yanlış kelimeye
    const double MinSyntheticFixed = 0.60;    // en az %60'ı doğru düzeltilmeli
    const double MinRealTyposFixed = 0.95;

    static readonly string[] MustKeep =
    [
        // kullanıcının mesajlarından, doğru ya da bilerek böyle yazılmış
        "exe", "eve", "pdf", "github", "claude", "antigravity", "antigravitye", "pushlasana", "ing", "dk", "keza",
        "futuristik", "yapmalık", "olucak", "yazıcam", "düzeltcek", "windowsda", "eğitiyo", "çalışmıyo", "diyo",
        "olmıycak", "olmuyo", "düzeltmiyo", "yapıyom", "geliyo", "sorunsuz", "patlıyor", "yapsana", "kapsamlı",
        "incele", "kusursuz", "minimal", "performanslı", "benzetirse", "kelimeyse", "yapabilelim", "pushla",
        "kelimede", "evde", "bende", "sende", "oda", "şimdiki", "akşamki", "dünkü", "belki", "mademki", "karda",
        "yapıyosun", "ediyosun", "biliyosun", "gelicem", "olıcak", "yazcam", "olcak", "commitledim", "pushladım",
        // günlük dil, sohbet
        "napıyon", "gelcem", "yapcam", "gidicem", "bilmiyom", "naber", "kanka", "hocam", "aynen", "valla", "tmm",
        "slm", "bi", "şey", "falan", "abi", "abla", "yaa", "hadi", "tamamdır", "eyvallah", "inşallah", "herhalde",
        "sanırım", "bence", "neyse", "lol", "btw", "ok", "okey",
        // teknik
        "staj", "stajyer", "localhost", "backend", "frontend", "spring", "flutter", "docker", "api", "json", "sql",
        "html", "css", "javascript", "typescript", "python", "kotlin", "excel", "whatsapp", "instagram", "youtube",
        "commit", "push", "npm", "git", "dll", "png", "jpg", "zip", "url", "wifi", "usb",
    ];

    /// <summary>Cümle ortasında büyük harfle yazılan, sözlükte olmayan isimler.</summary>
    static readonly string[] Names = ["Hazal", "Antigravity", "Claude", "Zeynep", "Burak", "Kadıköy", "Elif", "Emre", "Github", "Gemini"];

    /// <summary>Bu gerçek hatalar Türkçe karakter kullanmayan birinden.</summary>
    static readonly HashSet<string> AsciiTypos = ["calisiyorum", "nasilsin", "gorusuruz", "guzeldi"];

    static readonly (string Typed, string Expected)[] RealTypos =
    [
        ("temam", "tamam"), ("tımam", "tamam"), ("işlvli", "işlevli"), ("düzltmiyo", "düzeltmiyo"),
        ("düzeltmey", "düzeltmeye"), ("olmıcak", "olmıycak"), ("diyip", "deyip"), ("bunuda", "bunu da"),
        ("herşey", "her şey"), ("olmuyoda", "olmuyo da"), ("dediki", "dedi ki"), ("geldide", "geldi de"), ("banada", "bana da"), ("dahada", "daha da"), ("powerr", "power"), ("merhab", "merhaba"), ("gelyorum", "geliyorum"),
        ("okuldaa", "okulda"), ("bilmiyorm", "bilmiyorum"), ("teşekürler", "teşekkürler"),
        ("calisiyorum", "çalışıyorum"), ("nasilsin", "nasılsın"), ("gorusuruz", "görüşürüz"), ("şımdı", "şimdi"),
        ("oldü", "oldu"), ("guzeldi", "güzeldi"), ("yaptm", "yaptım"), ("geldm", "geldim"), ("bişey", "bir şey"),
        ("teh", "the"), ("recieve", "receive"), ("becuase", "because"), ("thier", "their"),
    ];

    record Tally(string Name)
    {
        public int Total, Fixed, Wrong, Untouched;
        public List<string> Examples { get; } = [];

        public void Add(string typed, string? expected, string? got)
        {
            Total++;
            if (got == expected) { if (expected != null) Fixed++; else Untouched++; return; }
            if (got == null) Untouched++;
            else Wrong++;
            if (Examples.Count < 12) Examples.Add($"{typed} → {got ?? "(dokunmadı)"}{(expected != null ? $"  [beklenen {expected}]" : "")}");
        }

        public double WrongRate => Total == 0 ? 0 : (double)Wrong / Total;
        public double FixedRate => Total == 0 ? 0 : (double)Fixed / Total;
    }

    public static int Run(string data, bool verbose)
    {
        var sw = Stopwatch.StartNew();
        var trWords = ReadList(Path.Combine(data, "tr.txt"));
        var enWords = ReadList(Path.Combine(data, "en.txt"));
        var rng = new Random(20261009);
        string protectedPath = Path.Combine(data, "korunan.txt");

        Corrector Make(WordTable tr, WordTable en)
        {
            var c = new Corrector();
            c.SetTable(Lang.Tr, tr);
            c.SetTable(Lang.En, en);
            c.LoadProtected(protectedPath);
            return c;
        }

        var full = Make(WordTable.Load(Path.Combine(data, "tr.txt"), Lang.Tr), WordTable.Load(Path.Combine(data, "en.txt"), Lang.En));

        // 1. Sözlükteki doğru kelimeler (yeterince sık geçenler; çok nadirleri altyazı hatası olabilir).
        // Yazım kuralları bu kümede kapalı: altyazıdaki "iyiki"yi "iyi ki" yapmak doğru bir düzeltmedir.
        full.SpellingRules = false;
        var known = new Tally("Sözlükteki doğru kelimeler");
        foreach (var w in Sample(trWords.Where(x => x.Count >= 50 && x.Word.Length >= 2), 3000, rng)) known.Add(w, null, Fresh(full, w));
        foreach (var w in Sample(enWords.Where(x => x.Count >= 500 && x.Word.Length >= 2), 1000, rng)) known.Add(w, null, Fresh(full, w));

        // 2. Sözlükte olmayan doğru kelimeler: nadir kelimeleri çıkarıp yeniden yükle
        var heldTr = Sample(trWords.Where(x => x.Count is >= 8 and <= 40 && x.Word.Length >= 3), 1500, rng).ToHashSet();
        var heldEn = Sample(enWords.Where(x => x.Count <= 400 && x.Word.Length >= 3), 500, rng).ToHashSet();
        // Altyazı listesinde yazım hataları da var ("herkeş", "gidyorsun"). Sözlükte dururken bile
        // düzelticinin "hata" dediği kelimeler "doğru kelime" sayılmaz; çıkarılır.
        heldTr.RemoveWhere(w => Fresh(full, w) != null);
        heldEn.RemoveWhere(w => Fresh(full, w) != null);
        var holed = Make(
            WordTable.Load(Path.Combine(data, "tr.txt"), Lang.Tr, heldTr.Contains),
            WordTable.Load(Path.Combine(data, "en.txt"), Lang.En, heldEn.Contains));
        holed.SpellingRules = false;
        var unknownShort = new Tally("Sözlükte olmayan doğru kelimeler (3–4 harf)");
        var unknownLong = new Tally("Sözlükte olmayan doğru kelimeler (5+ harf)");
        foreach (var w in heldTr.Concat(heldEn))
            (w.Length <= 4 ? unknownShort : unknownLong).Add(w, null, Fresh(holed, w));

        full.SpellingRules = true;

        // 3. Elle seçilmiş dokunulmayacaklar
        var keep = new Tally("Dokunulmaması gerekenler (teknik, isim, günlük dil)");
        foreach (var w in MustKeep) keep.Add(w, null, Fresh(full, w));
        foreach (var w in Names) keep.Add(w, null, Fresh(full, w, sentenceStart: false));

        // 4. Gerçek yazım hataları
        var real = new Tally("Gerçek yazım hataları");
        foreach (var (typed, expected) in RealTypos) real.Add(typed, expected, Fresh(full, typed, asciiHabit: AsciiTypos.Contains(typed)));

        // Bilgi: Türkçe karakter kullanmayan biri için sözlükteki doğru kelimeler (aksama → akşama gibi
        // değişiklikler bu kullanıcı için çoğu zaman istenen şeydir; sınır uygulanmaz).
        var knownAscii = new Tally("Sözlükteki kelimeler (ASCII alışkanlığı, bilgi)");
        foreach (var w in Sample(trWords.Where(x => x.Count >= 50 && x.Word.Length >= 2 && !x.Word.Any(ch => "çğıöşü".Contains(ch))), 1500, rng))
            knownAscii.Add(w, null, Fresh(full, w, asciiHabit: true));

        // 5. Üretilmiş yazım hataları
        var trSet = trWords.Select(x => x.Word).ToHashSet();
        var enSet = enWords.Select(x => x.Word).ToHashSet();
        var synthetic = new Dictionary<string, Tally>();
        var pool = Sample(trWords.Where(x => x.Count >= 2000 && x.Word.Length is >= 4 and <= 14), 3000, rng).ToArray();
        for (int i = 0; i < pool.Length; i++)
        {
            var kind = Typos.Kinds[i % Typos.Kinds.Length];
            string? typo = Typos.Make(pool[i], kind, rng);
            if (typo == null || trSet.Contains(typo) || enSet.Contains(typo)) continue; // gerçek kelimeye dönüştüyse belirsiz
            Get(synthetic, "Türkçe: " + kind).Add(typo, pool[i], Fresh(full, typo, asciiHabit: kind == "Türkçe karaktersiz"));
        }
        var enPool = Sample(enWords.Where(x => x.Count >= 5000 && x.Word.Length is >= 4 and <= 12), 800, rng).ToArray();
        for (int i = 0; i < enPool.Length; i++)
        {
            var kind = Typos.EnglishKinds[i % Typos.EnglishKinds.Length];
            string? typo = Typos.Make(enPool[i], kind, rng);
            if (typo == null || enSet.Contains(typo) || trSet.Contains(typo)) continue;
            Get(synthetic, "İngilizce: " + kind).Add(typo, enPool[i], Fresh(full, typo));
        }

        // Rapor
        Console.WriteLine($"\n{"Küme",-52} {"adet",6} {"doğru",8} {"YANLIŞ",8} {"dokunmadı",10}");
        Console.WriteLine(new string('─', 88));
        foreach (var t in new[] { known, knownAscii, unknownShort, unknownLong, keep, real }) Row(t, expectsFix: t == real);
        var synthAll = new Tally("Üretilmiş hatalar (toplam)");
        foreach (var t in synthetic.Values.OrderBy(t => t.Name))
        {
            Row(t, expectsFix: true);
            synthAll.Total += t.Total; synthAll.Fixed += t.Fixed; synthAll.Wrong += t.Wrong; synthAll.Untouched += t.Untouched;
        }
        Row(synthAll, expectsFix: true);
        Console.WriteLine($"\nÖlçüm süresi: {sw.Elapsed.TotalSeconds:F1} sn");

        // Kapılar
        var failures = new List<string>();
        if (known.WrongRate > MaxKnownFalseFix) failures.Add($"sözlükteki doğru kelimelerin %{known.WrongRate * 100:F2}'si değişti (sınır %{MaxKnownFalseFix * 100})");
        double unknownRate = (double)(unknownShort.Wrong + unknownLong.Wrong) / Math.Max(1, unknownShort.Total + unknownLong.Total);
        if (unknownRate > MaxUnknownFalseFix) failures.Add($"sözlükte olmayan doğru kelimelerin %{unknownRate * 100:F1}'i değişti (sınır %{MaxUnknownFalseFix * 100})");
        if (keep.Wrong > 0) failures.Add($"dokunulmaması gereken {keep.Wrong} kelime değişti");
        if (real.FixedRate < MinRealTyposFixed) failures.Add($"gerçek hataların yalnızca %{real.FixedRate * 100:F0}'i düzeldi (sınır %{MinRealTyposFixed * 100})");
        if (synthAll.WrongRate > MaxSyntheticWrong) failures.Add($"üretilmiş hataların %{synthAll.WrongRate * 100:F1}'i yanlış kelimeye çevrildi (sınır %{MaxSyntheticWrong * 100})");
        if (synthAll.FixedRate < MinSyntheticFixed) failures.Add($"üretilmiş hataların yalnızca %{synthAll.FixedRate * 100:F0}'i düzeldi (sınır %{MinSyntheticFixed * 100})");

        foreach (var t in new[] { known, unknownShort, unknownLong, keep, real }.Concat(synthetic.Values))
        {
            if (t.Examples.Count == 0 || (!verbose && t.Wrong == 0 && t != real)) continue;
            Console.WriteLine($"\n{t.Name} — örnekler:");
            foreach (var e in t.Examples) Console.WriteLine("    " + e);
        }

        Console.WriteLine(failures.Count == 0 ? "\nÖlçüm sınırları içinde." : "\nÖLÇÜM SINIRLARI AŞILDI:\n  - " + string.Join("\n  - ", failures));
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>Her kelimede bağlam sıfırlanır: ölçüm sırası sonucu etkilemesin. Varsayılan kullanıcı Türkçe
    /// klavyeyle yazar (ş, ı, ü kullanır); asciiHabit ise hiç Türkçe karakter kullanmayan biri.</summary>
    static string? Fresh(Corrector c, string w, bool sentenceStart = true, bool asciiHabit = false)
    {
        c.ResetContext();
        if (!asciiHabit) c.AssumeTurkishKeyboard();
        return c.Suggest(w, sentenceStart);
    }

    static void Row(Tally t, bool expectsFix)
    {
        string pct(int n) => t.Total == 0 ? "-" : $"%{100.0 * n / t.Total:F1}";
        Console.WriteLine($"{t.Name,-52} {t.Total,6} {(expectsFix ? pct(t.Fixed) : "-"),8} {pct(t.Wrong),8} {pct(t.Untouched),10}");
    }

    static Tally Get(Dictionary<string, Tally> d, string name) => d.TryGetValue(name, out var t) ? t : d[name] = new Tally(name);

    static List<(string Word, long Count)> ReadList(string path) =>
        File.ReadLines(path)
            .Select(l => l.Split(' '))
            .Where(p => p.Length == 2 && long.TryParse(p[1], out _))
            .Select(p => (p[0], long.Parse(p[1])))
            .ToList();

    static IEnumerable<string> Sample(IEnumerable<(string Word, long Count)> items, int n, Random rng)
    {
        var list = items.Select(x => x.Word).ToList();
        for (int i = 0; i < Math.Min(n, list.Count); i++)
        {
            int j = rng.Next(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
            yield return list[i];
        }
    }
}

/// <summary>Gerçekçi yazım hataları üretir (Türkçe Q klavye).</summary>
static class Typos
{
    public static readonly string[] Kinds = ["komşu tuş", "eksik harf", "fazla harf", "yer değiştirme", "çift harf", "eksik ünlü", "Türkçe karaktersiz", "ı/i karışması"];
    public static readonly string[] EnglishKinds = ["komşu tuş", "eksik harf", "fazla harf", "yer değiştirme", "çift harf"];

    static readonly string[] Rows = ["qwertyuıopğü", "asdfghjklşi", "zxcvbnmöç"];
    static readonly float[] Shift = [0f, 0.25f, 0.75f];
    const string Vowels = "aeıioöuü";

    static List<char> Neighbors(char c)
    {
        var result = new List<char>();
        for (int r = 0; r < Rows.Length; r++)
        {
            int i = Rows[r].IndexOf(c);
            if (i < 0) continue;
            for (int r2 = 0; r2 < Rows.Length; r2++)
            for (int j = 0; j < Rows[r2].Length; j++)
            {
                float dx = Math.Abs(i + Shift[r] - (j + Shift[r2]));
                if (r == r2 ? dx is > 0 and < 1.01f : Math.Abs(r - r2) == 1 && dx < 0.8f) result.Add(Rows[r2][j]);
            }
        }
        return result;
    }

    public static string? Make(string w, string kind, Random rng)
    {
        switch (kind)
        {
            case "komşu tuş":
            {
                int i = rng.Next(w.Length);
                var n = Neighbors(w[i]);
                return n.Count == 0 ? null : w[..i] + n[rng.Next(n.Count)] + w[(i + 1)..];
            }
            case "eksik harf":
            {
                int i = rng.Next(1, w.Length); // ilk harfi atlamak nadirdir
                return w.Remove(i, 1);
            }
            case "fazla harf":
            {
                int i = rng.Next(w.Length);
                var n = Neighbors(w[i]);
                return n.Count == 0 ? null : w.Insert(i + rng.Next(2), n[rng.Next(n.Count)].ToString());
            }
            case "yer değiştirme":
            {
                int i = rng.Next(1, w.Length - 1);
                return w[i] == w[i + 1] ? null : w[..i] + w[i + 1] + w[i] + w[(i + 2)..];
            }
            case "çift harf":
            {
                int i = rng.Next(w.Length);
                return w.Insert(i, w[i].ToString());
            }
            case "eksik ünlü":
            {
                var spots = Enumerable.Range(1, w.Length - 1).Where(i => Vowels.Contains(w[i])).ToList();
                return spots.Count == 0 ? null : w.Remove(spots[rng.Next(spots.Count)], 1);
            }
            case "Türkçe karaktersiz":
            {
                string a = w.Replace('ç', 'c').Replace('ğ', 'g').Replace('ı', 'i').Replace('ö', 'o').Replace('ş', 's').Replace('ü', 'u');
                return a == w ? null : a;
            }
            case "ı/i karışması":
            {
                var spots = Enumerable.Range(0, w.Length).Where(i => w[i] is 'ı' or 'i').ToList();
                if (spots.Count == 0) return null;
                int k = spots[rng.Next(spots.Count)];
                return w[..k] + (w[k] == 'ı' ? 'i' : 'ı') + w[(k + 1)..];
            }
        }
        return null;
    }
}
