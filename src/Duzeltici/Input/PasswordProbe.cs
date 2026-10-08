using System.Runtime.InteropServices;

namespace Duzeltici.Input;

/// <summary>
/// Odaktaki alan parola mı? Klasik Windows kutularını ES_PASSWORD ile anlarız; tarayıcılar,
/// Electron ve XAML uygulamaları içinse UI Automation'a sorulur (UIA_IsPasswordPropertyId).
///
/// UIA çağrısı başka bir sürece gider ve o süreç meşgulse uzun sürebilir; klavye kancasının
/// içinde yapılamaz. Bu yüzden kendi iş parçacığında çalışır ve cevabı kancaya ileti olarak
/// gönderir. Yalnızca bir düzeltme yapılmak üzereyken sorulur, her tuşta değil.
/// </summary>
internal static class PasswordProbe
{
    const int UIA_IsPasswordPropertyId = 30019;

    static readonly AutoResetEvent s_signal = new(false);
    static volatile int s_request;
    static uint s_replyThread, s_replyMessage;

    /// <summary>DUZELTICI_TANI=1 ise her sorgunun süresi %APPDATA%\Duzeltici\tani.log'a yazılır.</summary>
    static readonly bool s_trace = Environment.GetEnvironmentVariable("DUZELTICI_TANI") == "1";

    public static void Start(uint replyThread, uint replyMessage)
    {
        (s_replyThread, s_replyMessage) = (replyThread, replyMessage);
        var thread = new Thread(Loop) { IsBackground = true, Name = "Parola denetimi" };
        thread.SetApartmentState(ApartmentState.MTA); // UIA istemcileri için önerilen
        thread.Start();
    }

    /// <summary>Cevap, wParam = istek numarası, lParam = 1 (güvenli) / 0 (parola) olarak gelir.</summary>
    public static void Ask(int request)
    {
        s_request = request;
        s_signal.Set();
    }

    static void Loop()
    {
        IUIAutomation? uia = null;
        try
        {
            uia = (IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E"))!)!;
        }
        catch (Exception ex)
        {
            Log.Error(ex); // UIA yoksa yalnızca ES_PASSWORD denetimi kalır
        }

        while (true)
        {
            s_signal.WaitOne();
            int request = s_request;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            bool safe = uia == null || !IsPassword(uia);
            if (s_trace) Log.Trace($"parola sorgusu #{request}: {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms, güvenli={safe}");
            Native.PostThreadMessageW(s_replyThread, s_replyMessage, request, safe ? 1 : 0);
        }
    }

    static bool IsPassword(IUIAutomation uia)
    {
        IUIAutomationElement? element = null;
        try
        {
            if (uia.GetFocusedElement(out element) != 0 || element == null) return false;
            return element.GetCurrentPropertyValue(UIA_IsPasswordPropertyId, out object? value) == 0 && value is true;
        }
        catch (COMException)
        {
            return false; // UIA desteklemeyen uygulama: ES_PASSWORD denetimi zaten yapıldı
        }
        finally
        {
            if (element != null) Marshal.ReleaseComObject(element);
        }
    }

    // UIAutomationClient.h'den yalnızca gereken yöntemler. Sıra önemli (vtable): kullanılmayan
    // yöntemler yer tutucu olarak duruyor.
    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUIAutomation
    {
        void CompareElements();
        void CompareRuntimeIds();
        void GetRootElement();
        void ElementFromHandle();
        void ElementFromPoint();
        [PreserveSig] int GetFocusedElement(out IUIAutomationElement? element);
    }

    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUIAutomationElement
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        void FindAll();
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();
        [PreserveSig] int GetCurrentPropertyValue(int propertyId, out object? value);
    }
}
