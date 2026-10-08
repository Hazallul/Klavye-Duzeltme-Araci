namespace Duzeltici.Engine;

public enum Lang { Tr = 0, En = 1 }

/// <summary>
/// Kelimeler UTF-16 metin yerine 1..35 arası harf kodlarıyla tutulur: bellek yarıya iner
/// ve aday üretimi yığın (stack) üzerindeki tamponlarda, hiç bellek ayırmadan yapılır.
/// </summary>
public static class Alphabet
{
    // Kod = bu dizgideki sıra. 0 kullanılmaz.
    const string Letters = "\0abcçdefgğhıijklmnoöprsştuüvyzqwxâîû";
    public const int Size = 36;
    public const int MaxWord = 40;

    static readonly byte[] s_code = new byte[0x180];
    static readonly bool[] s_adjacent = new bool[Size * Size];
    static readonly byte[] s_turkishTwin = new byte[Size]; // c → ç
    static readonly byte[] s_asciiTwin = new byte[Size];   // ç → c

    /// <summary>Ekleme/değiştirme denemelerinde kullanılan harfler.</summary>
    public static readonly byte[] TrEdits;
    public static readonly byte[] EnEdits;
    /// <summary>İki hatalık aramada ikinci hata olarak denenen, en sık atlanan harfler.</summary>
    public static readonly byte[] TrCommonInserts, EnCommonInserts;

    static Alphabet()
    {
        for (int i = 1; i < Size; i++) s_code[Letters[i]] = (byte)i;

        foreach (var (ascii, turkish) in new[] { ('c', 'ç'), ('g', 'ğ'), ('i', 'ı'), ('o', 'ö'), ('s', 'ş'), ('u', 'ü') })
        {
            s_turkishTwin[s_code[ascii]] = s_code[turkish];
            s_asciiTwin[s_code[turkish]] = s_code[ascii];
        }

        TrEdits = Encode("abcçdefgğhıijklmnoöprsştuüvyzqwx");
        EnEdits = Encode("abcdefghijklmnopqrstuvwxyz");
        TrCommonInserts = Encode("aeıioöuürnlkmyd");
        EnCommonInserts = Encode("aeioulnrst");

        // Türkçe Q klavye. Satırlar yarım tuş kaydırılmış; yan yana ya da çapraz komşu
        // tuşlar "yakın" sayılır (parmak kayması hatası).
        string[] rows = ["qwertyuıopğü", "asdfghjklşi", "zxcvbnmöç"];
        float[] shift = [0f, 0.25f, 0.75f];
        for (int r1 = 0; r1 < rows.Length; r1++)
        for (int i1 = 0; i1 < rows[r1].Length; i1++)
        for (int r2 = 0; r2 < rows.Length; r2++)
        for (int i2 = 0; i2 < rows[r2].Length; i2++)
        {
            float dx = Math.Abs(i1 + shift[r1] - (i2 + shift[r2]));
            bool near = r1 == r2 ? dx is > 0 and < 1.01f : Math.Abs(r1 - r2) == 1 && dx < 0.8f;
            if (near) s_adjacent[s_code[rows[r1][i1]] * Size + s_code[rows[r2][i2]]] = true;
        }
    }

    public static byte[] Encode(string s) => s.Select(c => s_code[c]).ToArray();

    public static char Char(byte code) => Letters[code];

    public static bool Adjacent(byte a, byte b) => s_adjacent[a * Size + b];
    public static bool IsVowel(byte code) => Letters[code] is 'a' or 'e' or 'ı' or 'i' or 'o' or 'ö' or 'u' or 'ü' or 'â' or 'î' or 'û';
    public static byte TurkishTwin(byte code) => s_turkishTwin[code];
    public static byte AsciiTwin(byte code) => s_asciiTwin[code];

    /// <summary>Dilin kurallarıyla küçük harfe çevirip kodlar (Türkçede I → ı, İ → i).
    /// Kelimede o dile ait olmayan bir karakter varsa false döner.</summary>
    public static bool TryEncode(ReadOnlySpan<char> word, Lang lang, Span<byte> dest)
    {
        if (word.Length > dest.Length) return false;
        for (int i = 0; i < word.Length; i++)
        {
            char c = ToLower(word[i], lang);
            byte code = c < s_code.Length ? s_code[c] : (byte)0;
            if (code == 0 || (lang == Lang.En && c is not (>= 'a' and <= 'z'))) return false;
            dest[i] = code;
        }
        return true;
    }

    public static char ToLower(char c, Lang lang) => (lang, c) switch
    {
        (Lang.Tr, 'I') => 'ı',
        (Lang.Tr, 'İ') => 'i',
        _ => char.ToLowerInvariant(c),
    };

    public static char ToUpper(char c, Lang lang) => (lang, c) switch
    {
        (Lang.Tr, 'i') => 'İ',
        (Lang.Tr, 'ı') => 'I',
        _ => char.ToUpperInvariant(c),
    };
}
