using System.Text.Json;
using System.Text.Json.Serialization;
using Duzeltici.Engine;

namespace Duzeltici;

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public LanguageMode Language { get; set; } = LanguageMode.Smart;
    public Sensitivity Sensitivity { get; set; } = Sensitivity.Balanced;
    public bool FixTurkishChars { get; set; } = true;
    public bool UndoWithBackspace { get; set; } = true;
    public bool CorrectOnEnter { get; set; }
    public int MinWordLength { get; set; } = 3;

    /// <summary>Düzeltme yapılmayacak uygulamalar: terminaller, kod editörleri, parola yöneticileri,
    /// uzak masaüstü. Hepsi kelime olmayan metinlerle ya da gizli alanlarla dolu.</summary>
    public List<string> ExcludedApps { get; set; } =
    [
        "windowsterminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "conhost.exe", "wsl.exe",
        "code.exe", "cursor.exe", "devenv.exe", "studio64.exe", "idea64.exe", "rider64.exe", "pycharm64.exe",
        "webstorm64.exe", "goland64.exe", "clion64.exe", "phpstorm64.exe", "datagrip64.exe", "sublime_text.exe",
        "keepass.exe", "keepassxc.exe", "1password.exe", "bitwarden.exe",
        "mstsc.exe", "vmconnect.exe",
    ];

    static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Duzeltici");
    static string SettingsPath => Path.Combine(Folder, "ayarlar.json");
    public static string LearnedPath => Path.Combine(Folder, "ogrenilen.txt");
    public static string LogPath => Path.Combine(Folder, "hata.log");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), SettingsJson.Default.AppSettings) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    public static IEnumerable<string> LoadLearned()
    {
        try
        {
            return File.Exists(LearnedPath) ? File.ReadAllLines(LearnedPath).Where(w => w.Length > 0) : [];
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return [];
        }
    }

    public static void AppendLearned(string word)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllLines(LearnedPath, [word]);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJson : JsonSerializerContext;

internal static class Log
{
    static readonly Lock s_lock = new();

    public static void Error(Exception ex)
    {
        try
        {
            lock (s_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppSettings.LogPath)!);
                File.AppendAllText(AppSettings.LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}\n");
            }
        }
        catch
        {
            // Günlük yazılamıyorsa yapacak bir şey yok; uygulama çalışmaya devam etmeli.
        }
    }
}
