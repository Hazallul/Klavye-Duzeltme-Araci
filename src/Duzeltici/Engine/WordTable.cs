using System.Numerics;
using System.Text;

namespace Duzeltici.Engine;

/// <summary>
/// Kelime → sıklık tablosu. Yarım milyon kelime için Dictionary&lt;string,int&gt; ~45 MB
/// tutarken bu yapı ~11 MB tutar: tüm kelimeler tek bir byte dizisinde
/// [uzunluk][log-sıklık][harf kodları] olarak art arda durur, açık adresli hash tablosu
/// yalnızca bu dizideki konumları saklar. Arama hiç bellek ayırmaz.
/// </summary>
public sealed class WordTable
{
    readonly byte[] _blob;
    readonly int[] _slots; // konum + 1, 0 = boş
    readonly int _mask;

    public int Count { get; }
    /// <summary>ln(toplam sıklık); diller arası karşılaştırmada sıklıkları normalleştirir.</summary>
    public float LogTotal { get; }

    WordTable(byte[] blob, int[] slots, int count, float logTotal)
    {
        _blob = blob;
        _slots = slots;
        _mask = slots.Length - 1;
        Count = count;
        LogTotal = logTotal;
    }

    /// <summary>Sıklığın doğal logaritması ×16 olarak (0..255), kelime yoksa -1.</summary>
    public int Find(ReadOnlySpan<byte> word)
    {
        int i = (int)Hash(word) & _mask;
        while (true)
        {
            int slot = _slots[i];
            if (slot == 0) return -1;
            int at = slot - 1;
            if (_blob[at] == word.Length && _blob.AsSpan(at + 2, word.Length).SequenceEqual(word))
                return _blob[at + 1];
            i = (i + 1) & _mask;
        }
    }

    public static WordTable Load(string path, Lang lang)
    {
        byte[] text = File.ReadAllBytes(path);
        int lines = text.AsSpan().Count((byte)'\n') + 1;
        int capacity = (int)BitOperations.RoundUpToPowerOf2((uint)(lines * 5 / 3));
        var slots = new int[capacity];
        var blob = new byte[text.Length + 2 * lines];
        int blobLen = 0, count = 0, mask = capacity - 1;
        double total = 0;

        Span<char> chars = stackalloc char[Alphabet.MaxWord * 2];
        Span<byte> codes = stackalloc byte[Alphabet.MaxWord];

        foreach (var range in text.AsSpan().Split((byte)'\n'))
        {
            var line = text.AsSpan(range);
            int space = line.IndexOf((byte)' ');
            if (space <= 0 || space > chars.Length) continue;
            int n = Encoding.UTF8.GetChars(line[..space], chars);
            if (n > Alphabet.MaxWord || !Alphabet.TryEncode(chars[..n], lang, codes)) continue;
            if (!long.TryParse(line[(space + 1)..].TrimEnd((byte)'\r'), out long freq) || freq <= 0) continue;

            total += freq;
            var word = codes[..n];
            byte q = (byte)Math.Clamp(Math.Round(Math.Log(freq) * 16), 0, 255);

            int i = (int)Hash(word) & mask;
            while (slots[i] != 0)
            {
                int at = slots[i] - 1;
                if (blob[at] == n && blob.AsSpan(at + 2, n).SequenceEqual(word)) break;
                i = (i + 1) & mask;
            }
            if (slots[i] != 0)
            {
                ref byte existing = ref blob[slots[i]]; // konum + 1 = sıklık baytı
                existing = Math.Max(existing, q);
                continue;
            }
            slots[i] = blobLen + 1;
            blob[blobLen] = (byte)n;
            blob[blobLen + 1] = q;
            word.CopyTo(blob.AsSpan(blobLen + 2));
            blobLen += n + 2;
            count++;
        }

        Array.Resize(ref blob, blobLen);
        return new WordTable(blob, slots, count, (float)Math.Log(Math.Max(total, 1)));
    }

    static uint Hash(ReadOnlySpan<byte> s)
    {
        uint h = 2166136261;
        foreach (byte b in s) h = (h ^ b) * 16777619;
        return h ^ (h >> 15);
    }
}
