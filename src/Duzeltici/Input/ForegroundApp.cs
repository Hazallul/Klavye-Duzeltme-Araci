namespace Duzeltici.Input;

/// <summary>Öndeki pencerenin hangi uygulamaya ait olduğunu bulur. Yalnızca pencere
/// değiştiğinde çağrılır, her tuşta değil.</summary>
internal static unsafe class ForegroundApp
{
    static readonly uint s_ownPid = (uint)Environment.ProcessId;
    static readonly bool s_selfElevated = IsElevated(-1); // -1 = bu sürecin sözde tutamacı

    /// <summary>Son yazı yazılan uygulamanın exe adı (ayarlarda "hariç tut" önerisi için).</summary>
    public static volatile string? LastApp;

    /// <summary>Bu pencerede düzeltme yapılmamalı mı? (hariç listesi, kendi penceremiz,
    /// yönetici olarak çalışan uygulamalar: Windows bunlara tuş göndermemize izin vermez.)</summary>
    public static bool ShouldSkip(nint window, HashSet<string> excluded)
    {
        uint pid;
        Native.GetWindowThreadProcessId(window, &pid);
        if (pid == 0 || pid == s_ownPid) return true;

        nint process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return true;
        try
        {
            char* path = stackalloc char[1024];
            uint size = 1024;
            if (!Native.QueryFullProcessImageName(process, 0, path, ref size)) return true;
            string name = Path.GetFileName(new string(path, 0, (int)size)).ToLowerInvariant();
            LastApp = name;
            return excluded.Contains(name) || (!s_selfElevated && IsElevated(process));
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    /// <summary>Odaktaki denetim klasik bir Windows parola kutusu mu (ES_PASSWORD)? Yalnızca
    /// düzeltme yapılacağı an çağrılır. Tarayıcıların ve modern (XAML) uygulamaların parola
    /// alanlarını bu yöntem göremez; onlar için Enter'da düzeltmemek ve hariç listesi var.</summary>
    public static bool FocusIsPassword(nint window)
    {
        var info = new Native.GUITHREADINFO { cbSize = (uint)sizeof(Native.GUITHREADINFO) };
        if (!Native.GetGUIThreadInfo(Native.GetWindowThreadProcessId(window, null), &info) || info.hwndFocus == 0)
            return false;
        if ((Native.GetWindowLongPtrW(info.hwndFocus, Native.GWL_STYLE) & Native.ES_PASSWORD) == 0) return false;

        // ES_PASSWORD yalnızca düzenleme kutularında bu anlama gelir (Edit, RichEdit, WindowsForms...EDIT).
        char* name = stackalloc char[64];
        int n = Native.GetClassNameW(info.hwndFocus, name, 64);
        return new ReadOnlySpan<char>(name, n).Contains("edit", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsElevated(nint process)
    {
        if (!Native.OpenProcessToken(process, Native.TOKEN_QUERY, out nint token)) return true;
        try
        {
            int elevated = 0;
            return !Native.GetTokenInformation(token, Native.TokenElevation, &elevated, sizeof(int), out _) || elevated != 0;
        }
        finally
        {
            Native.CloseHandle(token);
        }
    }
}
