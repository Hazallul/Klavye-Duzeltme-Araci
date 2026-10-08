using Microsoft.Win32;

namespace Duzeltici;

/// <summary>"Windows ile başlat": HKCU\...\Run altına kayıt. Yönetici izni gerekmez.</summary>
internal static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Duzeltici";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(Name, Command);
            else key.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    /// <summary>"dotnet Duzeltici.dll" ile çalışıyorsak (Akıllı Uygulama Denetimi imzasız exe'yi
    /// engellediğinde) aynı biçimde başlatılmalı; konsol penceresi açılmasın diye conhost --headless ile.</summary>
    public static string Command
    {
        get
        {
            string host = Environment.ProcessPath!;
            if (!Path.GetFileName(host).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return $"\"{host}\"";
            string conhost = Path.Combine(Environment.SystemDirectory, "conhost.exe");
            return $"\"{conhost}\" --headless \"{host}\" \"{Path.Combine(AppContext.BaseDirectory, "Duzeltici.dll")}\"";
        }
    }
}
