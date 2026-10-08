// Ham frekans listelerini (hermitdave/FrequencyWords, OpenSubtitles 2018) uygulamanın
// yüklediği küçük sözlüklere dönüştürür. Kullanım:
//   node tools/build-dict.mjs <tr_full.txt> <en_50k.txt>
// Çıktı: src/Duzeltici/Data/tr.txt ve en.txt ("kelime sayı" satırları).
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const [trSrc, enSrc] = process.argv.slice(2);
if (!trSrc || !enSrc) {
  console.error("kullanım: node tools/build-dict.mjs <tr_full.txt> <en_50k.txt>");
  process.exit(1);
}

function build(src, dest, { locale, minCount, pattern, maxLength }) {
  // Kaynak listede büyük "İ" küçültülmemiş ("İstanbul 1861" ile "istanbul 61" ayrı satır):
  // dilin kurallarıyla küçültüp aynı kelimelerin sayılarını topluyoruz.
  const counts = new Map();
  for (const line of readFileSync(src, "utf8").split("\n")) {
    const [raw, countText] = line.trim().split(" ");
    const count = Number(countText);
    if (!raw || !(count > 0)) continue;
    const word = raw.toLocaleLowerCase(locale);
    if (word.length > maxLength || !pattern.test(word)) continue;
    counts.set(word, (counts.get(word) ?? 0) + count);
  }
  const out = [...counts]
    .filter(([, count]) => count >= minCount)
    .sort((a, b) => b[1] - a[1])
    .map(([word, count]) => `${word} ${count}`);
  writeFileSync(join(root, "src/Duzeltici/Data", dest), out.join("\n") + "\n");
  console.log(`${dest}: ${out.length} kelime`);
}

// Türkçe listede 5'ten az geçen kelimeler çoğunlukla yazım hatası; onları almıyoruz.
// Kalan hatalar (ör. "temam" 16) düşük sayılarıyla kalıyor, düzeltici bunu kullanıyor.
build(trSrc, "tr.txt", { locale: "tr-TR", minCount: 5, pattern: /^[a-zçğıöşüâîû]+$/u, maxLength: 40 });
build(enSrc, "en.txt", { locale: "en-US", minCount: 1, pattern: /^[a-z]+$/, maxLength: 40 });
