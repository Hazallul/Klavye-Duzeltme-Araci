using System.Diagnostics;
using Duzeltici.Engine;

// Kullanım: dotnet Duzeltici.Tests.dll          motor testleri ve hız ölçümü
//           dotnet Duzeltici.Tests.dll --e2e    çalışan uygulamayla uçtan uca deneme
if (args.Contains("--e2e"))
{
    int code = 1;
    var ui = new Thread(() => code = EndToEnd.Run());
    ui.SetApartmentState(ApartmentState.STA);
    ui.Start();
    ui.Join();
    return code;
}

var data = Path.Combine(AppContext.BaseDirectory, "Data");
if (args.Contains("--olcum")) return Evaluation.Run(data, verbose: args.Contains("-v"));
if (args.Length >= 2 && args[0] == "--metin")
{
    // Geliştirme aracı: bir metni klavye kancasının göreceği gibi kelime kelime düzelticiden geçirir ve
    // değişecek kelimeleri listeler. dotnet Duzeltici.Tests.dll --metin dosya.txt
    var c = new Corrector();
    c.SetTable(Lang.Tr, WordTable.Load(Path.Combine(data, "tr.txt"), Lang.Tr));
    c.SetTable(Lang.En, WordTable.Load(Path.Combine(data, "en.txt"), Lang.En));
    c.LoadProtected(Path.Combine(data, "korunan.txt"));
    string text = File.ReadAllText(args[1]);
    int changes = 0, wordCount = 0;
    bool sentenceStart = true;
    for (int i = 0; i < text.Length;)
    {
        if (!char.IsLetter(text[i]))
        {
            if (text[i] is '.' or '!' or '?' or '\n') sentenceStart = true;
            i++;
            continue;
        }
        int start = i;
        while (i < text.Length && char.IsLetter(text[i])) i++;
        // Kanca rakam/sembolle bitişik kelimelere dokunmaz (adres, e-posta, kod); burada da öyle.
        bool joined = (start > 0 && !char.IsWhiteSpace(text[start - 1]) && text[start - 1] is not ('(' or '"'))
            || (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('.' or ',' or '!' or '?' or ';' or ':' or ')' or '"' or '\''));
        string word = text[start..i];
        wordCount++;
        string? fix = joined || word.Length < 3 ? null : c.Suggest(word, sentenceStart);
        if (fix != null) { changes++; Console.WriteLine($"  {word,-16} → {fix}"); }
        sentenceStart = false;
    }
    Console.WriteLine($"{wordCount} kelime, {changes} değişiklik");
    return 0;
}
if (args.Length >= 2 && args[0] == "--neden")
{
    // Geliştirme aracı: dotnet Duzeltici.Tests.dll --neden kelime [kelime...] [--ascii]
    var c = new Corrector();
    c.SetTable(Lang.Tr, WordTable.Load(Path.Combine(data, "tr.txt"), Lang.Tr));
    c.SetTable(Lang.En, WordTable.Load(Path.Combine(data, "en.txt"), Lang.En));
    c.LoadProtected(Path.Combine(data, "korunan.txt"));
    foreach (var w in args.Skip(1).Where(a => !a.StartsWith("--")))
    {
        c.ResetContext();
        if (!args.Contains("--ascii")) c.AssumeTurkishKeyboard();
        Console.WriteLine($"── {w}");
        Console.WriteLine(c.Explain(w));
    }
    return 0;
}

// ── Motor testleri: tek tek örnekler. Binlerce kelimelik isabet ölçümü en sonda (Evaluation).
long before = GC.GetTotalMemory(true);
var sw = Stopwatch.StartNew();
var tr = WordTable.Load(Path.Combine(data, "tr.txt"), Lang.Tr);
var en = WordTable.Load(Path.Combine(data, "en.txt"), Lang.En);
long loadMs = sw.ElapsedMilliseconds;
sw.Restart();
var trMorphology = Corrector.BuildMorphology(Lang.Tr, tr);
var enMorphology = Corrector.BuildMorphology(Lang.En, en);
long morphologyMs = sw.ElapsedMilliseconds;
long memory = GC.GetTotalMemory(true) - before;
Console.WriteLine($"Sözlük: {tr.Count:N0} Türkçe + {en.Count:N0} İngilizce kelime, yükleme {loadMs} ms, " +
    $"ek istatistiği {morphologyMs} ms ({trMorphology.Count + enMorphology.Count:N0} son), toplam {memory / 1024.0 / 1024:F1} MB");

var corrector = new Corrector();
corrector.SetTable(Lang.Tr, tr, trMorphology);
corrector.SetTable(Lang.En, en, enMorphology);
corrector.LoadProtected(Path.Combine(data, "korunan.txt"));

int failed = 0;
// Varsayılan kullanıcı Türkçe klavyeyle yazar; ascii: hiç Türkçe karakter kullanmayan biri.
void Expect(string typed, string? want, LanguageMode mode = LanguageMode.Smart, bool ascii = false, bool sentenceStart = true)
{
    corrector.Mode = mode;
    corrector.ResetContext();
    if (!ascii) corrector.AssumeTurkishKeyboard();
    string? got = corrector.Suggest(typed, sentenceStart);
    bool ok = got == want;
    if (!ok) failed++;
    Console.WriteLine($"{(ok ? "  ok " : "HATA")} {typed,-14} → {got ?? "(dokunma)",-14} {(ok ? "" : $"beklenen: {want ?? "(dokunma)"}")}");
}

Console.WriteLine("\nTürkçe yazım hataları");
Expect("temam", "tamam");
Expect("Temam", "Tamam");
Expect("merhab", "merhaba");
Expect("gelyorum", "geliyorum");
Expect("okuldaa", "okulda");
Expect("bilmiyorm", "bilmiyorum");
Expect("teşekürler", "teşekkürler");
Expect("şımdı", "şimdi");
Expect("oldü", "oldu");

Console.WriteLine("\nKullanıcının bildirdikleri");
Expect("tımam", "tamam");
Expect("işlvli", "işlevli");
Expect("düzltmiyo", "düzeltmiyo");  // günlük dil "-yo" korunur
Expect("düzeltmey", "düzeltmeye");
Expect("olmıcak", "olmıycak");      // "olıcak" değil: silinen m olumsuzluk ekiydi
Expect("olmuyoda", "olmuyo da");
Expect("powerr", "power");

Console.WriteLine("\nDokunulmaması gerekenler (kullanıcının yazdıkları)");
foreach (var w in new[] { "exe", "futuristik", "düzeltmiyo", "yapıyom", "yapıyosun", "diyo", "eğitiyo", "antigravitye",
             "pushlasana", "windowsda", "yapmalık", "kelimede", "ing", "keza", "çalışmıyo", "olıcak", "yazıcam" })
    Expect(w, null);
Expect("Hazal", null, sentenceStart: false);      // cümle ortasında büyük harf: isim
Expect("Antigravity", null, sentenceStart: false);
Expect("Burak", null, sentenceStart: false);
Expect("Merhab", "Merhaba", sentenceStart: false); // büyük harfli yazım hatası: ucuz düzeltme

Console.WriteLine("\nYazım kuralları (TDK)");
Expect("diyip", "deyip");
Expect("bunuda", "bunu da");
Expect("dediki", "dedi ki");
Expect("Herşey", "Her şey");
Expect("herşeyi", "her şeyi");
Expect("yanlızca", "yalnızca");
Expect("yada", "ya da");
foreach (var w in new[] { "evde", "sende", "bende", "belki", "mademki", "şimdiki", "akşamki", "kelimede" }) Expect(w, null);

Console.WriteLine("\nTürkçe karakter tamamlama (Türkçe karakter kullanmayan kullanıcı)");
Expect("calisiyorum", "çalışıyorum", ascii: true);
Expect("nasilsin", "nasılsın", ascii: true);
Expect("gorusuruz", "görüşürüz", ascii: true);
Expect("guzeldi", "güzeldi", ascii: true);
Expect("Aksam", "Akşam", ascii: true);
Expect("Istanbul", "İstanbul", LanguageMode.Turkish, ascii: true); // akıllı modda İngilizce "Istanbul" da doğru

Console.WriteLine("\nDokunulmaması gerekenler");
foreach (var w in new[] { "tamam", "sen", "şen", "evlerimizden", "Ankara", "NASA", "camelCase", "the", "hello", "aksama" }) Expect(w, null);

Console.WriteLine("\nİngilizce");
Expect("teh", "the");
Expect("recieve", "receive");
Expect("becuase", "because");
Expect("thier", "their");

Console.WriteLine("\nDil modları");
Expect("calisiyorum", null, LanguageMode.English, ascii: true);
Expect("recieve", null, LanguageMode.Turkish);

Console.WriteLine("\nÖğrenme");
corrector.ResetContext();
corrector.Reject("merhab");             // ilk geri alma: yalnızca bu oturum
Expect("merhab", null);
Console.WriteLine(corrector.LearnedWords(["merhab"]).Length == 0 ? "  ok  ilk geri almada kalıcı öğrenilmedi" : "HATA ilk geri almada öğrenildi");
if (corrector.LearnedWords(["merhab"]).Length != 0) failed++;
corrector.Reject("merhab");             // ikinci kez: kalıcı
if (corrector.LearnedWords(["merhab"]).Length != 1) { failed++; Console.WriteLine("HATA ikinci geri almada öğrenilmedi"); } else Console.WriteLine("  ok  ikinci geri almada öğrenildi");
corrector.ClearLearned();
Expect("merhab", "merhaba");

// Hız: kelime bitince (boşluk) yapılan tek arama ne kadar sürüyor?
string[] words = ["temam", "calisiyorum", "gelyorum", "evlerimizden", "recieve", "merhaba", "bilgisayarlarımızdan", "xqzvbn"];
foreach (var s in new[] { Sensitivity.Balanced, Sensitivity.Bold })
{
    corrector.Mode = LanguageMode.Smart;
    corrector.Sensitivity = s;
    for (int i = 0; i < 200; i++) foreach (var w in words) corrector.Suggest(w); // ısınma
    const int rounds = 2000;
    long alloc = GC.GetAllocatedBytesForCurrentThread();
    sw.Restart();
    for (int i = 0; i < rounds; i++) foreach (var w in words) corrector.Suggest(w);
    sw.Stop();
    double perWord = sw.Elapsed.TotalMicroseconds / (rounds * words.Length);
    double bytes = (GC.GetAllocatedBytesForCurrentThread() - alloc) / (double)(rounds * words.Length);
    Console.WriteLine($"\nHız ({s}): kelime başına ortalama {perWord:F1} µs, {bytes:F0} bayt ayırma");

    double worst = 0;
    foreach (var w in words)
    {
        sw.Restart();
        corrector.Suggest(w);
        worst = Math.Max(worst, sw.Elapsed.TotalMicroseconds);
    }
    Console.WriteLine($"En yavaş tek kelime: {worst:F0} µs");
}

Console.WriteLine(failed == 0 ? "\nTüm motor testleri geçti." : $"\n{failed} motor testi başarısız.");

// İsabet ölçümü (binlerce kelime): CI'da da çalışır, sınırlar aşılırsa başarısız olur.
int measurement = Evaluation.Run(data, verbose: false);
return failed == 0 && measurement == 0 ? 0 : 1;
