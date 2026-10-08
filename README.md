# Düzeltici

[![CI](https://github.com/Hazallul/Klavye-Duzeltme-Araci/actions/workflows/ci.yml/badge.svg)](https://github.com/Hazallul/Klavye-Duzeltme-Araci/actions/workflows/ci.yml)

Windows'ta arka planda çalışan, yazarken yazım hatalarını telefonlardaki gibi anında düzelten küçük bir araç.
Türkçe ve İngilizce bilir; akıllı modda kelimenin diline göre düzeltir.

```
temam␣       → tamam␣
calisiyorum␣ → çalışıyorum␣
Merhab.      → Merhaba.
recieve␣     → receive␣
```

Simgesi sağ alttaki `^` okunun içindedir. Tıklayınca açılan panelden açıp kapatabilir, dil (Akıllı / Türkçe /
English), düzeltme gücü (Temkinli / Dengeli / Cesur), Türkçe karakter tamamlama, Backspace ile geri alma,
Enter'da düzeltme, Windows ile başlatma ve hariç tutulan uygulamaları ayarlayabilirsin.

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
          ayraç tuşu yutulur ─► odaktaki alan parola mı? (ayrı iş parçacığında, en çok 150 ms)
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
  diye bakılmıyor; çok daha sık geçen bir komşusu varsa düzeltiliyor.
- **Akıllı dil:** "olduğu gibi bırak" ve "her dildeki en iyi düzeltme" seçenekleri dil boyutuna göre
  normalleştirilip son kelimelerin diline yakınlık eklenerek yarışır.
- **Dokunulmayanlar:** KISALTMA, camelCase, rakamlı kelimeler, adres ve e-postalar (`site.com`, `ad@posta`),
  parola alanları, yönetici olarak çalışan uygulamalar, hariç listesindeki uygulamalar (terminal, kod
  editörü, parola yöneticisi, uzak masaüstü).
- **Parola alanları:** düzeltme gönderilmeden hemen önce sorulur. Klasik Windows kutuları için `ES_PASSWORD`,
  tarayıcı, Electron ve XAML uygulamaları için UI Automation (`IsPassword`). Soru yalnızca bir düzeltme
  yapılacakken sorulur; cevap 150 ms içinde gelmezse güvenli tarafta kalınır ve düzeltme yapılmaz.
- **Tuş sırası:** `SendInput` tuşları giriş kuyruğunun sonuna ekler. Bu yüzden düzeltme paketi uygulamaya
  ulaşana kadar (paketin son tuşu kancadan geçene kadar) kullanıcının bastığı tuşlar yutulur ve arkasından
  gönderilir. Hızlı yazımda harf kaybolmaz; 500 ms güvenlik süresi klavyenin asla kilitli kalmamasını sağlar.
- **Bekçi:** Windows zaman aşımına uğrayan bir kancayı haber vermeden kaldırabilir. 30 saniyede bir, sistemde
  giriş olduğu halde kancanın çağrılmadığı fark edilirse kanca yeniden kurulur.

### Performans

| | |
|---|---|
| Kelime başına süre (Dengeli) | ~40 µs |
| Kelime başına süre (Cesur, iki hatalık arama) | ~1–2 ms |
| Boştayken işlemci | 0 (fare kancası yalnızca yarım kelime varken takılır) |
| Bellek | ~24 MB özel |
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
bastığın tuşlar test kutusuna düşer. Test, geri alma adımında `okuldaa` kelimesini öğretir;
`%APPDATA%\Duzeltici\ogrenilen.txt` dosyasından silebilirsin.

Simgeyi yeniden üretmek için `tools\make-icon.ps1`. Sözlükleri yeniden üretmek için ham listeleri indirip şunu çalıştır:

```powershell
node tools/build-dict.mjs tr_full.txt en_50k.txt
```

Ayarlar `%APPDATA%\Duzeltici\ayarlar.json`, öğrenilen kelimeler `ogrenilen.txt`, hatalar `hata.log`
dosyasında. Yazılan metin hiçbir yere kaydedilmez.

## Bilinen sınırlar

- UI Automation sorgusu Chromium tabanlı tarayıcılarda erişilebilirlik ağacını açar; bu, tarayıcıda küçük bir
  ek yük demektir (yalnızca bir düzeltme yapılacağı an sorulur).
- Klavye komşulukları Türkçe Q düzenine göre (F klavyede "komşu tuş" hataları daha az isabetli).
- Simge ve uygulama imzalı değil; Akıllı Uygulama Denetimi açık bilgisayarlarda `tools\kur.ps1` ile kurulmalı.
