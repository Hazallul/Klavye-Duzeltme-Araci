# Sözlük kaynağı

`tr.txt` ve `en.txt`, Hermit Dave'in [FrequencyWords](https://github.com/hermitdave/FrequencyWords)
projesindeki OpenSubtitles 2018 frekans listelerinden (`content/2018/tr/tr_full.txt`, `content/2018/en/en_50k.txt`)
`tools/build-dict.mjs` ile üretildi. Kaynak lisans: [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/).
Bu türetilmiş dosyalar da aynı lisansla paylaşılır.

Yapılan değişiklikler: dilin kurallarıyla küçük harfe çevirme ("İstanbul" → "istanbul") ve aynı kelimelerin
sayılarını toplama; harf dışı karakter içeren satırları ve Türkçede 5'ten az geçen kelimeleri çıkarma.
