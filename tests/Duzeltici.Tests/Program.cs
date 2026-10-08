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
long before = GC.GetTotalMemory(true);
var sw = Stopwatch.StartNew();
var tr = WordTable.Load(Path.Combine(data, "tr.txt"), Lang.Tr);
var en = WordTable.Load(Path.Combine(data, "en.txt"), Lang.En);
sw.Stop();
long tables = GC.GetTotalMemory(true) - before;
Console.WriteLine($"Sözlük: {tr.Count:N0} Türkçe + {en.Count:N0} İngilizce kelime, {sw.ElapsedMilliseconds} ms, {tables / 1024.0 / 1024:F1} MB");

var corrector = new Corrector();
corrector.SetTable(Lang.Tr, tr);
corrector.SetTable(Lang.En, en);

int failed = 0;
void Expect(string typed, string? want, LanguageMode mode = LanguageMode.Smart, Sensitivity s = Sensitivity.Balanced)
{
    corrector.Mode = mode;
    corrector.Sensitivity = s;
    string? got = corrector.Suggest(typed);
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
Expect("yapıyosun", "yapıyorsun");
Expect("bilmiyorm", "bilmiyorum");
Expect("teşekürler", "teşekkürler");

Console.WriteLine("\nKullanıcının bildirdikleri");
Expect("tımam", "tamam");
Expect("işlvli", "işlevli");
Expect("düzltmiyo", "düzeltmiyo"); // günlük dil "-yo" korunur
Expect("düzeltmey", "düzeltmeye");
Expect("futuristik", null);
Expect("düzeltmiyo", null);        // günlük dil biçimi tanınır
Expect("yapıyom", null);

Console.WriteLine("\nYazım kuralları (TDK)");
Expect("diyip", "deyip");
Expect("bunuda", "bunu da");
Expect("dediki", "dedi ki");
Expect("Herşey", "Her şey");
Expect("herşeyi", "her şeyi");
Expect("yanlızca", "yalnızca");
Expect("yada", "ya da");
Expect("evde", null);
Expect("sende", null);
Expect("belki", null);
Expect("mademki", null);

Console.WriteLine("\nTürkçe karakter tamamlama");
Expect("calisiyorum", "çalışıyorum");
Expect("nasilsin", "nasılsın");
Expect("gorusuruz", "görüşürüz");
Expect("Istanbul", "İstanbul", LanguageMode.Turkish); // akıllı modda İngilizce "Istanbul" da doğru
Expect("Aksam", "Akşam");

Console.WriteLine("\nDokunulmaması gerekenler");
Expect("tamam", null);
Expect("sen", null);
Expect("şen", null);
Expect("evlerimizden", null);
Expect("Ankara", null);
Expect("NASA", null);
Expect("camelCase", null);
Expect("the", null);
Expect("hello", null);

Console.WriteLine("\nİngilizce");
Expect("teh", "the");
Expect("recieve", "receive");
Expect("becuase", "because");
Expect("thier", "their");
Expect("helo", "hello"); // Türkçe altyazılarda 129 kez geçse de

Console.WriteLine("\nDil modları");
Expect("calisiyorum", null, LanguageMode.English);
Expect("temam", "team", LanguageMode.English); // yalnızca İngilizce: İngilizce en yakın kelime
Expect("recieve", null, LanguageMode.Turkish);

Console.WriteLine("\nÖğrenme");
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

Console.WriteLine(failed == 0 ? "\nTüm testler geçti." : $"\n{failed} test başarısız.");
return failed == 0 ? 0 : 1;
