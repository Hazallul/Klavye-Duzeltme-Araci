using Duzeltici.UI;

namespace Duzeltici;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Tek kopya: ikinci başlatma yalnızca açık olana "ayarları göster" der.
        using var single = new Mutex(true, @"Local\Duzeltici", out bool first);
        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Duzeltici.Goster");
        if (!first)
        {
            // Kullanıcının başlattığı bu kopya öne pencere getirebilir; açık olana bu hakkı
            // devretmezsek panel odak alamaz ve hemen kapanır.
            AllowSetForegroundWindow(-1);
            showRequest.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.ThreadException += (_, e) => Log.Error(e.Exception);

        using var app = new TrayApp(showRequest, openSettings: args.Contains("--ayarlar"));
        Application.Run(app);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}
