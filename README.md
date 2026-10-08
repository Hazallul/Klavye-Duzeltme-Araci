# Düzeltici

[![CI](https://github.com/Hazallul/Klavye-Duzeltme-Araci/actions/workflows/ci.yml/badge.svg)](https://github.com/Hazallul/Klavye-Duzeltme-Araci/actions/workflows/ci.yml)

Windows'ta arka planda çalışan, yazarken yazım hatalarını telefonlardaki gibi anında düzelten küçük bir araç.
Türkçe ve İngilizce bilir; akıllı modda kelimenin diline göre düzeltir.

```
temam␣       → tamam␣
calisiyorum␣ → çalışıyorum␣
Merhab.      → Merhaba.
recieve␣     → receive␣
işlvli␣      → işlevli␣
düzltmiyo␣   → düzeltmiyo␣     (günlük dil korunur)
herşey␣      → her şey␣        (TDK yazım kuralları)
bunuda␣      → bunu da␣
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
  adresli bir hash tablosunda durur; `Dictionary<string,int>` ile ~45 MB tutacak veri ~11 MB tutar.
- **Puanlama:** aday = ln(sıklık) − hata maliyeti; yazılan = ln(sıklık) + önyargı. Komşu tuş, yer
  değiştirme ve çift harf gibi olası hatalar ucuz, ş → s gibi bilerek basılmış Türkçe harfi bozmak pahalı.
  Altyazı listesinde hatalı biçimler de bulunduğu için ("temam" 16 kez geçiyor) sadece "sözlükte var mı?"
  diye bakılmıyor; çok daha sık geçen bir komşusu varsa düzeltiliyor. Sözlükte hiç olmayan kelimeler için
  eşik daha düşük ve iki hatalık arama da yapılıyor (süre sınırlı: Dengeli'de en çok 1 ms).
- **Günlük dil:** `geliyo`, `yapıyom` gibi biçimler sözlükte yok ama `geliyor`, `yapıyorum` var; sıklıkları
  oradan alınır ve üslup korunur (`düzltmiyo` → `düzeltmiyo`).
- **Yazım kuralları:** sıklıkla bulunamayan TDK yanlışları. Altyazılarda `herşey` 25 bin kez geçtiği için
  sözlükte doğru görünür; kural listesi düzeltir (`herşey` → `her şey`, `diyip` → `deyip`, `yanlız` → `yalnız`).
  Bağlaç `de/da` ve `ki` ayrı yazılır (`bunuda` → `bunu da`) ama `evde`, `belki` gibi bitişik olanlara dokunulmaz:
  kök bitişik halinden en az 150 kat sık değilse ayrılmaz.
- **Akıllı dil:** "olduğu gibi bırak" ve "her dildeki en iyi düzeltme" seçenekleri dil boyutuna göre
  normalleştirilip son kelimelerin diline yakınlık eklenerek yarışır.
- **Dokunulmayanlar:** KISALTMA, camelCase, rakamlı kelimeler, adres ve e-postalar (`site.com`, `ad@posta`),
  parola alanları, yönetici olarak çalışan uygulamalar, hariç listesindeki uygulamalar (terminal, kod
  editörü, parola yöneticisi, uzak masaüstü).
- **Parola alanları:** düzeltme gönderilmeden hemen önce sorulur. Klasik Windows kutuları için `ES_PASSWORD`,
  tarayıcı, Electron ve XAML uygulamaları için UI Automation (`IsPassword`). Soru kelimenin ilk harfinde, ayrı
  bir iş parçacığında sorulur; kelime bitince cevap çoğu zaman hazırdır (Chromium bir süre sorgu gelmezse
  erişilebilirliği kapatır, ilk sorgu 200 ms'yi bulabilir). Kelime bittiğinde cevap hâlâ yoksa en çok 200 ms
  beklenir, gelmezse güvenli tarafta kalınır ve düzeltme yapılmaz.
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
| Kelime başına süre (Dengeli, sözlüğe yakın kelime) | ~40 µs |
| Kelime başına süre (iki hatalık arama gerekince) | en çok ~1 ms (Dengeli), ~4 ms (Cesur) |
| Boştayken işlemci | 0 (fare kancası yalnızca yarım kelime varken takılır) |
| Bellek | ~25 MB özel (sözlükler 13 MB, 1 MB'lık hızlı ret süzgeçleri dahil) |
| Sözlük yükleme | ~220 ms, arka planda |

## Geliştirme

```powershell
dotnet build src/Duzeltici -c Release
dotnet build tests/Duzeltici.Tests -c Release
dotnet tests\Duzeltici.Tests\bin\Release\net10.0-windows\Duzeltici.Tests.dll          # motor testleri + hız
dotnet tests\Duzeltici.Tests\bin\Release\net10.0-windows\Duzeltici.Tests.dll --e2e    # çalışan uygulamayla gerçek tuşlarla
```

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
`tani.log`'a yazılır (yazılan kelimeler yazılmaz). Bir kelime beklendiği halde düzeltilmiyorsa ilk bakılacak yer.

## Bilinen sınırlar

- UI Automation sorgusu Chromium tabanlı tarayıcılarda erişilebilirlik ağacını açar; bu, tarayıcıda küçük bir
  ek yük demektir (yalnızca bir düzeltme yapılacağı an sorulur).
- Klavye komşulukları Türkçe Q düzenine göre (F klavyede "komşu tuş" hataları daha az isabetli).
- Simge ve uygulama imzalı değil; Akıllı Uygulama Denetimi açık bilgisayarlarda `tools\kur.ps1` ile kurulmalı.
