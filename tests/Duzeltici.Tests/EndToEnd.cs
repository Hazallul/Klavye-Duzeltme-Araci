using System.Runtime.InteropServices;

/// <summary>
/// Çalışan Düzeltici'yi gerçek tuş basışlarıyla dener: bir metin kutusu açar, içine yazar ve
/// sonucu okur. Tuşlar yalnızca kendi penceremiz öndeyken gönderilir; başka pencere öne
/// geçerse test durur (kullanıcının başka bir yerine yazmamak için).
/// Kullanım: dotnet Duzeltici.Tests.dll --e2e   (Düzeltici açıkken)
/// </summary>
static class EndToEnd
{
    record Step(string Name, Func<Task> Act, string Expected);

    public static int Run()
    {
        Application.EnableVisualStyles();
        var form = new Form { Text = "Düzeltici uçtan uca test", Width = 720, Height = 140, TopMost = true, StartPosition = FormStartPosition.CenterScreen };
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, Font = new Font("Segoe UI", 16) };
        form.Controls.Add(box);
        int failed = 0;

        var steps = new Step[]
        {
            new("yazım hatası", () => Type("temam "), "tamam "),
            new("Türkçe karakter", () => Type("nasilsin "), "tamam nasılsın "),
            new("noktalama", () => Type("Merhab. "), "tamam nasılsın Merhaba. "),
            new("hızlı yazım (tek seferde)", () => { Burst("gelyorum okulda "); return Task.CompletedTask; }, "tamam nasılsın Merhaba. geliyorum okulda "),
            new("Backspace ile geri alma", async () => { await Type("okuldaa "); Press(0x08); }, "tamam nasılsın Merhaba. geliyorum okulda okuldaa"),
            new("geri alınan kelime oturumda düzeltilmez", () => Type(" okuldaa "), "tamam nasılsın Merhaba. geliyorum okulda okuldaa okuldaa "),
            new("adres/e-posta", () => Type("site.temam/x temam@posta "), "tamam nasılsın Merhaba. geliyorum okulda okuldaa okuldaa site.temam/x temam@posta "),
            new("parola kutusuna dokunmaz", () => { box.Clear(); box.UseSystemPasswordChar = true; box.Focus(); return Type("temam "); }, "temam "),
        };

        form.Shown += async (_, _) =>
        {
            ActivateKeyboardLayout(s_layout, 0);
            s_window = form.Handle;
            BringToFront(form.Handle);
            form.Activate();
            box.Focus();
            await Task.Delay(400);
            foreach (var step in steps)
            {
                try
                {
                    await step.Act();
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"DURDU: {ex.Message}");
                    failed++;
                    break;
                }
                await Task.Delay(250);
                bool ok = box.Text == step.Expected;
                if (!ok) failed++;
                Console.WriteLine($"{(ok ? "  ok " : "HATA")} {step.Name}{(ok ? "" : $"\n       beklenen: \"{step.Expected}\"\n       çıkan:    \"{box.Text}\"")}");
            }
            form.Close();
        };
        Application.Run(form);
        Console.WriteLine(failed == 0 ? "Uçtan uca testler geçti." : $"{failed} uçtan uca test başarısız.");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>İnsan hızında (karakter başına 30 ms) yazar. Beklerken arayüz iş parçacığı serbest
    /// kalmalı: Düzeltici parola denetimi için bu pencereye UI Automation ile soru soruyor.</summary>
    static async Task Type(string text)
    {
        foreach (char c in text)
        {
            Send(KeysFor(c));
            await Task.Delay(30);
        }
    }

    /// <summary>Tüm metni tek SendInput çağrısıyla gönderir: tuşlar düzeltme işlenirken gelir.</summary>
    static void Burst(string text) => Send(text.SelectMany(KeysFor).ToArray());

    static void Press(ushort vk) => Send([Key(vk, false), Key(vk, true)]);

    static nint s_window;

    /// <summary>Windows arka plandaki bir sürecin pencereyi öne almasını engeller; öndeki pencerenin
    /// giriş kuyruğuna kısa süre bağlanınca izin verir. Tuş göndermez.</summary>
    static void BringToFront(nint window)
    {
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _), me = GetCurrentThreadId();
        bool attached = fgThread != me && AttachThreadInput(me, fgThread, true);
        SetForegroundWindow(window);
        BringWindowToTop(window);
        if (attached) AttachThreadInput(me, fgThread, false);
    }

    /// <summary>Türkçe Q düzeni (yüklüyse); tuş ↔ karakter eşlemesi buna göre yapılır.</summary>
    static readonly nint s_layout = TurkishLayout();

    static nint TurkishLayout()
    {
        var layouts = new nint[16];
        int n = GetKeyboardLayoutList(layouts.Length, layouts);
        return layouts.Take(n).FirstOrDefault(h => (h & 0xFFFF) == 0x041F, GetKeyboardLayout(0));
    }

    static INPUT[] KeysFor(char c)
    {
        short scan = VkKeyScanExW(c, s_layout);
        if (scan == -1) throw new InvalidOperationException($"'{c}' klavye düzeninde yok");
        ushort vk = (ushort)(scan & 0xFF);
        bool shift = (scan & 0x100) != 0, altGr = (scan & 0x600) == 0x600;
        var list = new List<INPUT>();
        if (altGr) { list.Add(Key(0x11, false)); list.Add(Key(0xA5, false, extended: true)); } // AltGr = Ctrl + sağ Alt
        if (shift) list.Add(Key(0x10, false));
        list.Add(Key(vk, false));
        list.Add(Key(vk, true));
        if (shift) list.Add(Key(0x10, true));
        if (altGr) { list.Add(Key(0xA5, true, extended: true)); list.Add(Key(0x11, true)); }
        return list.ToArray();
    }

    static INPUT Key(ushort vk, bool up, bool extended = false) => new()
    {
        type = 1,
        ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKeyW(vk, 0), dwFlags = (up ? 2u : 0u) | (extended ? 1u : 0u) },
    };

    /// <summary>Yalnızca test penceresi öndeyken gönderir; değilse durur.</summary>
    static void Send(INPUT[] inputs)
    {
        nint fg = GetForegroundWindow();
        if (fg != s_window)
        {
            GetWindowThreadProcessId(fg, out uint pid);
            string owner = pid == 0 ? "?" : System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            throw new InvalidOperationException($"test penceresi önde değil (önde: {owner}), tuş gönderilmedi.");
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nuint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nuint dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        InputUnion u;
        public KEYBDINPUT ki { set => u.ki = value; }
    }

    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern short VkKeyScanExW(char c, nint layout);
    [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int count, nint[] layouts);
    [DllImport("user32.dll")] static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] static extern nint ActivateKeyboardLayout(nint layout, uint flags);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyW(uint code, uint type);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(nint hwnd);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
