using System.Runtime.InteropServices;

namespace Duzeltici.Engine;

/// <summary>
/// "Sözlükte yok ama doğru olabilir mi?" sorusunun cevabı.
///
/// Türkçe sondan eklemeli: "numunelerimi", "tanıştırıldık" gibi doğru kelimelerin çoğu hiçbir listede
/// yoktur. Bunları yazım hatası sanıp en yakın sık kelimeye çevirmek, düzelticinin en kötü hatasıdır.
/// Elle ek listesi yazmak yerine ek bilgisini sözlüğün kendisinden çıkarırız: sözlükteki her kelime
/// için "kök + son" ayrımlarına bakılır (kök de sözlükte olmalı); her son için kaç FARKLI kökte, hangi
/// tür köke eklendiği sayılır. Kök türü ünlü uyumunu ve ünsüz benzeşmesini taşır (son ünlü, ünlüyle mi
/// bitiyor, sert ünsüzle mi), böylece "gel + iyorum" geçerli, "gel + yorum" geçersiz çıkar.
///
/// Bilinmeyen bir kelime, bilinen bir köke yeterince çok kökte görülmüş bir son eklenerek elde
/// edilebiliyorsa "muhtemelen doğru" sayılır. İngilizce için de aynı yöntem (-s, -ed, -ing...) çalışır.
/// </summary>
public sealed class Morphology
{
    const int MaxEnding = 8;
    const int MinStemLength = 2;
    static readonly int MinStemQ = (int)(MathF.Log(20) * 16); // kök en az ~20 kez geçmeli
    const int MinStems = 4;              // son en az bu kadar farklı kökte görülmeli
    const int MinStemsOneLetter = 25;    // tek harflik sonlar daha kolay rastlantı olur

    readonly HashSet<ulong> _endings;

    Morphology(HashSet<ulong> endings) => _endings = endings;

    public int Count => _endings.Count;

    /// <param name="oneLetterEndings">Tek harflik son olabilecek harfler. Sözlükte "deney" = "dene" + "y" gibi
    /// rastlantılar çoktur; gerçek tek harflik ekler azdır.</param>
    public static Morphology Build(WordTable table, string oneLetterEndings)
    {
        var oneLetter = new HashSet<byte>(oneLetterEndings.Select(c => Alphabet.Encode(c.ToString())[0]));
        var counts = new Dictionary<ulong, int>();
        table.ForEach((word, frequency) =>
        {
            for (int k = 1; k <= MaxEnding && word.Length - k >= MinStemLength; k++)
            {
                if (k == 1 && !oneLetter.Contains(word[^1])) continue;
                var stem = word[..^k];
                if (table.Find(stem) < MinStemQ) continue;
                ref int n = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, Key(word[^k..], Class(stem)), out _);
                n++;
            }
        });

        var endings = new HashSet<ulong>();
        foreach (var (key, n) in counts)
            if (n >= (EndingLength(key) == 1 ? MinStemsOneLetter : MinStems)) endings.Add(key);
        return new Morphology(endings);
    }

    /// <summary>Kelime, sözlükteki bir köke sık görülen bir son eklenerek elde edilebiliyor mu?</summary>
    /// <param name="table">Kökün aranacağı sözlük. Türkçe sonları İngilizce köklerle de denemek için
    /// ("windowsda") İngilizce sözlük verilebilir.</param>
    /// <param name="ignoreVoicing">Yabancı köklerde sert ünsüz kuralı tutarsız uygulanır ("windowsda" /
    /// "windowsta"); ikisi de kabul edilsin.</param>
    public bool Plausible(ReadOnlySpan<byte> word, WordTable table, int minStemLength = MinStemLength, bool ignoreVoicing = false)
    {
        for (int k = 1; k <= MaxEnding && word.Length - k >= minStemLength; k++)
        {
            var stem = word[..^k];
            int cls = Class(stem);
            bool fits = _endings.Contains(Key(word[^k..], cls)) || (ignoreVoicing && _endings.Contains(Key(word[^k..], cls ^ 1)));
            if (fits && table.Find(stem) >= MinStemQ) return true;
        }
        return false;
    }

    /// <summary>Kökün eki belirleyen özellikleri: son ünlüsü (8), ünlüyle mi bittiği, sert ünsüzle mi bittiği.</summary>
    static int Class(ReadOnlySpan<byte> stem)
    {
        int lastVowel = 0;
        for (int i = stem.Length - 1; i >= 0; i--)
            if (Alphabet.IsVowel(stem[i])) { lastVowel = stem[i]; break; }
        byte last = stem[^1];
        int endsVowel = Alphabet.IsVowel(last) ? 1 : 0;
        int voiceless = Alphabet.IsVoiceless(last) ? 1 : 0;
        return (lastVowel << 2) | (endsVowel << 1) | voiceless;
    }

    // Anahtar: 8 harf × 6 bit + uzunluk (4 bit) + kök türü (8 bit) = 60 bit
    static ulong Key(ReadOnlySpan<byte> ending, int stemClass)
    {
        ulong key = 0;
        foreach (byte b in ending) key = (key << 6) | b;
        return (key << 12) | ((ulong)ending.Length << 8) | (uint)stemClass;
    }

    static int EndingLength(ulong key) => (int)((key >> 8) & 0xF);
}
