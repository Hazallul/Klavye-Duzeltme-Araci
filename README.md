# Düzeltici

[![CI](https://github.com/Hazallul/Klavye-Duzeltme-Araci/actions/workflows/ci.yml/badge.svg)](https://github.com/Hazallul/Klavye-Duzeltme-Araci/actions/workflows/ci.yml)

Windows'ta arka planda çalışan, yazarken yazım hatalarını telefonlardaki gibi anında düzelten küçük bir araç.
Türkçe ve İngilizce bilir; akıllı modda kelimenin diline göre düzeltir.

```
temam␣       → tamam␣
calisiyorum␣ → çalışıyorum␣     (Türkçe karakter kullanmayanlar için)
Merhab.      → Merhaba.
işlvli␣      → işlevli␣
düzltmiyo␣   → düzeltmiyo␣      (günlük dil korunur)
olmıcak␣     → olmıycak␣        ("olıcak" değil: silinen m olumsuzluk ekidir)
herşey␣      → her şey␣         (TDK yazım kuralları)
bunuda␣      → bunu da␣         (ama kelimede, evde, şimdiki bitişik kalır)
recieve␣     → receive␣

exe, npm, Hazal, windowsda, numunelerimi, yapıyosun → dokunulmaz
```

Simgesi sağ alttaki `^` okunun içindedir. Tıklayınca açılan panelden açıp kapatabilir, dil (Akıllı / Türkçe /
English), düzeltme gücü (Temkinli / Dengeli / Cesur), Türkçe karakter tamamlama, yazım kuralları, Backspace ile
geri alma, Enter'da düzeltme, Windows ile başlatma, hariç tutulan uygulamalar ve öğrenilen kelimeleri ayarlayabilirsin.

## Kurulum

[.NET 10 SDK](https://dotnet.microsoft.com/download) gerekir.

```powershell
powershell -ExecutionPolicy Bypass -File tools\kur.ps1
```

Betik projeyi derler, Başlat menüsüne **Düzeltici** kısayolunu ekler ve uygulamayı başlatır. Kısayol
`Duzeltici.exe` yerine Microsoft imzalı `dotnet.exe` ile `Duzeltici.dll`'i çalıştırır. Windows'un Akıllı
Uygulama Denetimi yeni derlenmiş imzasız exe'leri zaman zaman engellediği için böyle yapılıyor.
`conhost --headless` ile başlatıldığı için konsol penceresi de açılmaz.

## İlke: yanlış düzeltme, düzeltmemekten kötüdür

Bir kelimeyi başka bir kelimeye çevirmek (`exe` → `eve`), hatayı olduğu gibi bırakmaktan çok daha can sıkıcıdır.
Her karar buna göre verilir ve [binlerce kelimeyle ölçülür](#kalite-ölçümü):

- **Sık kelimeler** (sözlükte 50+ kez) yalnızca en ucuz hatayla (çift harf) ve doğru biçim 100 kat daha sıksa
  düzeltilir (`teşekürler` → `teşekkürler`).
- **Türkçe ek yapısı:** `numunelerimi`, `tanıştırıldık` gibi doğru kelimelerin çoğu hiçbir listede yoktur. Ek
  bilgisi sözlüğün kendisinden çıkarılır: her "kök + son" için sonun kaç farklı kökte, hangi tür köke
  (son ünlü, ünlüyle/sert ünsüzle bitiş) eklendiği sayılır. Bilinen bir köke sık görülen bir son eklenerek elde
  edilebilen kelime "muhtemelen doğru" sayılır (`gel + iyorum` evet, `gel + yorum` hayır).
- **Doğru görünen kelimelerde** ucuz yazım hataları (komşu tuş, yer değiştirme, çift harf, Türkçe karakter)
  serbest; pahalı bir değişiklik ancak doğru biçim 1000 kat sıksa yapılır (`temam` → `tamam` evet;
  `osur` → `olur`, `acak` → `ancak` hayır: bunlar başka kelimeler).
- **Türkçe ses yapısına aykırı kelimeler** (üç ünsüz yan yana: `işlvli`, `düzltmiyo`) neredeyse kesin hatadır;
  nadir de olsa doğru biçime düzeltilir.
- **Kısa bilinmeyen kelimeler** (`exe`, `dk`, `ing`): komşuluk çok yoğun; yalnızca yer değiştirme, çift harf,
  Türkçe karakter. `q/w/x` içeren kelimeler yabancıdır; Türkçe ek almışsa (`windowsda`) olduğu gibi kalır.
- **İsimler:** cümle ortasında büyük harfle başlayan bilinmeyen kelimede (`Hazal`, `Burak`) yalnızca ucuz
  düzeltme (`Merhab` → `Merhaba` olur, `Hazal` → `Hayal` olmaz).
- **Klavye alışkanlığı:** son 20 kelimede ş/ı/ü görülmediyse kullanıcı Türkçe karakter kullanmıyordur;
  `calisiyorum` → `çalışıyorum` serbest. Türkçe klavyeyle yazan biri için ise ı/i, o/ö, u/ü karışması orta
  olasılıklı bir hatadır (`oldü` → `oldu`, `şımdı` → `şimdi`) ve sık bir kelimedeki `ş`'ye dokunulmaz (`şen`).
- **Günlük dil:** `diyo`, `yapıyom`, `gelicem`, `olmıycak` sözlükte yok ya da az; sıklıkları standart biçimden
  (`diyor`, `yapıyorum`...) alınır, üslup korunur.
- **Korunan kelimeler:** [`Data/korunan.txt`](src/Duzeltici/Data/korunan.txt) — dosya uzantıları, teknik
  terimler, kısaltmalar, sohbet dili. Bunlar sözlüğe sahte sayıyla eklenmez; yalnızca "dokunma" listesidir.
- **Yazım kuralları:** TDK'nin sıklıkla bulunamayan yanlışları (`herşey`, `diyip`, `yanlız`). Bağlaç `de/da`
  yalnızca dilbilgisi olarak gelebileceği yerde ayrılır: zamir/zarflardan (`bunu da`, `sonra da`) ve çekimli
  fiillerden sonra (`olmuyo da`); `kelimede`, `evde` bulunma ekidir, bitişik kalır.

## Kalite ölçümü

```powershell
dotnet tests\Duzeltici.Tests\bin\Release\net10.0-windows\Duzeltici.Tests.dll --olcum
```

| Küme | Ölçülen | Sınır (CI) |
|---|---|---|
| Sözlükteki doğru kelimeler (4000) — yanlışlıkla değiştirilen | %0,1 | ≤ %0,3 |
| Sözlükte olmayan doğru kelimeler (~1950) — yanlışlıkla değiştirilen | %3,6 | ≤ %4 |
| Elle seçilmiş dokunulmayacaklar (teknik, isim, günlük dil) | 0 | 0 |
| Gerçek yazım hataları — doğru düzeltilen | %100 | ≥ %95 |
| Üretilmiş hatalar (~3000) — doğru düzeltilen / yanlış kelimeye çevrilen | %77,6 / %4,6 | ≥ %60 / ≤ %5 |

"Sözlükte olmayan doğru kelime" taklit edilerek ölçülür: nadir kelimeler sözlükten çıkarılıp yüklenir, düzeltici
onlara dokunmamalıdır. Üretilmiş hatalar Türkçe Q klavyede gerçekçi hata türleridir (komşu tuş, eksik/fazla harf,
yer değiştirme, çift harf, eksik ünlü, Türkçe karaktersiz, ı/i karışması). Sınırlar aşılırsa test ve CI başarısız
olur; bir kelimeyi düzelten değişikliğin başka yerde ne bozduğu böylece hemen görülür.

## Nasıl çalışır

```
klavye ─► WH_KEYBOARD_LL kancası (kendi iş parçacığında)
            │ harfler o anki kelimeye eklenir (yalnızca bellekte, yalnızca tek kelime)
            ▼
          boşluk / noktalama
            │
            ▼
          Corrector.Suggest ── düzeltme yok ──► tuş olduğu gibi geçer
            │ düzeltme var
            ▼
          ayraç tuşu yutulur ─► odaktaki alan parola mı? (kelimenin ilk harfinde sorulmuştu)
            │ değil                                 │ parola / cevap gelmedi
            ▼                                       ▼
          SendInput: ⌫ × fark + doğru harfler + ayraç     yalnızca ayraç geri verilir
          (bu arada basılan tuşlar da yutulup paketin arkasından gönderilir)
```

- **Sözlük:** OpenSubtitles 2018 frekans listeleri ([hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords),
  CC-BY-SA 4.0). 553 bin Türkçe ve 47 bin İngilizce kelime. Tüm kelimeler tek bir byte dizisinde ve açık
  adresli bir hash tablosunda durur; `Dictionary<string,int>` ile ~45 MB tutacak veri ~11 MB tutar. Adayların
  çoğu sözlükte olmayan dizgiler olduğu için önce 1 MB'lık bir bit süzgecine bakılır.
- **Puanlama:** aday = ln(sıklık) − hata maliyeti; yazılan = ln(sıklık) + önyargı. Komşu tuş, yer
  değiştirme ve çift harf ucuz; uzak bir tuş pahalı (yazım hatasından çok başka bir kelime).
- **Akıllı dil:** "olduğu gibi bırak" ve "her dildeki en iyi düzeltme" seçenekleri dil boyutuna göre
  normalleştirilip son kelimelerin diline yakınlık eklenerek yarışır.
- **Dokunulmayanlar:** KISALTMA, camelCase, rakamlı kelimeler, adres ve e-postalar (`site.com`, `ad@posta`),
  parola alanları, yönetici olarak çalışan uygulamalar, hariç listesindeki uygulamalar (terminal, kod
  editörü, parola yöneticisi, uzak masaüstü).
- **Parola alanları:** klasik Windows kutuları için `ES_PASSWORD`, tarayıcı, Electron ve XAML uygulamaları için
  UI Automation (`IsPassword`). Soru her kelimenin ilk harfinde ayrı bir iş parçacığında sorulur; kelime bitince
  cevap çoğu zaman hazırdır (Chromium bir süre sorgu gelmezse erişilebilirliği kapatır, ilk sorgu 200 ms'yi
  bulabilir). Cevap yoksa en çok 200 ms beklenir, gelmezse güvenli tarafta kalınır ve düzeltme yapılmaz.
- **Geri alma ve öğrenme:** düzeltmeden hemen sonra Backspace eski hali geri getirir; kelime o oturum boyunca
  rahat bırakılır. Aynı kelime ikinci kez geri alınırsa kalıcı öğrenilir. Öğrenilenler panelde görünür ve
  tek tıkla temizlenir.
- **Tuş sırası:** `SendInput` tuşları giriş kuyruğunun sonuna ekler. Bu yüzden düzeltme paketi uygulamaya
  ulaşana kadar (paketin son tuşu kancadan geçene kadar) kullanıcının bastığı tuşlar yutulur ve arkasından
  gönderilir. Hızlı yazımda harf kaybolmaz; 500 ms güvenlik süresi klavyenin asla kilitli kalmamasını sağlar.
- **Bekçi:** Windows zaman aşımına uğrayan bir kancayı haber vermeden kaldırabilir. 30 saniyede bir, sistemde
  giriş olduğu halde kancanın çağrılmadığı fark edilirse kanca yeniden kurulur.

### Performans

| | |
|---|---|
| Kelime başına süre | çoğu kelimede 0,05–0,3 ms; en çok ~1 ms (Dengeli), ~4 ms (Cesur) |
| Boştayken işlemci | 0 (fare kancası yalnızca yarım kelime varken takılır) |
| Bellek | ~25 MB özel (sözlükler ve ek bilgisi ~14,5 MB) |
| Açılış | sözlük ~220 ms + ek bilgisi ~350 ms, arka planda |

## Geliştirme

```powershell
dotnet build src/Duzeltici -c Release
dotnet build tests/Duzeltici.Tests -c Release
$t = "tests\Duzeltici.Tests\bin\Release\net10.0-windows\Duzeltici.Tests.dll"
dotnet $t                          # motor testleri + hız + kalite ölçümü (CI bunu çalıştırır)
dotnet $t --olcum -v               # yalnızca kalite ölçümü, tüm örneklerle
dotnet $t --neden olmıcak exe      # bir kelime için her dildeki adaylar ve kararın nedeni
dotnet $t --metin yazi.txt         # bir metni kanca gibi kelime kelime geçirir, değişecekleri listeler
dotnet $t --e2e                    # çalışan uygulamayla gerçek tuşlarla
```

Bir kelime yanlış düzeltiliyor ya da düzeltilmiyorsa önce `--neden` ile sebebine bak, düzeltmeyi yap, sonra
`--olcum` ile bütün tabloya etkisini kontrol et. Tek bir örneği kapatmak için sözlüğe sahte sayı eklemek ya da
maliyetleri o kelimeye göre ayarlamak başka kelimeleri bozar; ölçüm bunu gösterir.

Uçtan uca test bir pencere açıp içine tuş gönderir. Tuşlar başka bir yere gitmesin diye her tuştan önce
kendi penceresinin önde olduğunu kontrol eder. Çalışırken klavyeye dokunma: pencere öne geçtiği için
bastığın tuşlar test kutusuna düşer. Geri alma adımındaki `okuldaa` yalnızca o oturumda rahat bırakılır,
kalıcı öğrenilmez.

Simgeyi yeniden üretmek için `tools\make-icon.ps1`. Sözlükleri yeniden üretmek için ham listeleri indirip şunu çalıştır:

```powershell
node tools/build-dict.mjs tr_full.txt en_50k.txt
```

Ayarlar `%APPDATA%\Duzeltici\ayarlar.json`, öğrenilen kelimeler `ogrenilen.txt`, hatalar `hata.log`
dosyasında. Yazılan metin hiçbir yere kaydedilmez.

`DUZELTICI_TANI=1` ortam değişkeniyle başlatılırsa parola sorgularının süreleri ve düzeltme kararları
`tani.log`'a yazılır (yazılan kelimeler yazılmaz).

## Bilinen sınırlar

- Sözlük altyazılardan geldiği için resmi/teknik metinlerdeki nadir kelimeler az temsil edilir; bunlar çoğu
  zaman ek yapısı sayesinde korunur ama düzeltme önerisi olarak da nadiren seçilir.
- Belirsiz hatalar bilerek düzeltilmez: `kırak` hem `kırmak` hem `bırak` olabilir.
- UI Automation sorgusu Chromium tabanlı tarayıcılarda erişilebilirlik ağacını açar; tarayıcıda küçük bir ek yük
  demektir.
- Klavye komşulukları Türkçe Q düzenine göre (F klavyede "komşu tuş" hataları daha az isabetli).
- Uygulama imzalı değil; Akıllı Uygulama Denetimi açık bilgisayarlarda `tools\kur.ps1` ile kurulmalı.
