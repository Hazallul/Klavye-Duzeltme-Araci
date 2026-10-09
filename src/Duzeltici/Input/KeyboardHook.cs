using System.Runtime.InteropServices;
using Duzeltici.Engine;
using static Duzeltici.Input.Native;

namespace Duzeltici.Input;

/// <summary>
/// Sistem genelinde klavyeyi dinler, o an yazılan kelimeyi tutar ve kelime bitince
/// (boşluk, noktalama) gerekiyorsa düzeltir: hatalı kısmı Backspace ile silip doğrusunu yazar.
///
/// Kendi iş parçacığında çalışır; Windows her tuşta bu iş parçacığını bekler, bu yüzden
/// arayüz ne yaparsa yapsın yazma gecikmez. Kancalar dışındaki her şey (ayarlar, olaylar)
/// tek yönlü akar: arayüz alanları yazar, kanca okur; kanca olayları arayüze gönderir.
/// </summary>
internal static unsafe class KeyboardHook
{
    const nuint Marker = 0x445A4C54;    // "DZLT": kendi gönderdiğimiz tuşları tanımak için
    const nuint EndMarker = 0x44450000; // gönderdiğimiz paketin son tuşu; alt 16 bit paket numarası
    const uint FlushTimeoutMs = 500;
    const uint VerdictTimeoutMs = 200; // kelime bitince cevap hâlâ yoksa en çok bu kadar beklenir
    const uint WatchdogMs = 30_000;
    const int MaxWord = 40;
    const uint MsgStart = WM_APP + 1, MsgStop = WM_APP + 2, MsgFlush = WM_APP + 3, MsgMouse = WM_APP + 4, MsgVerdict = WM_APP + 5;

    // --- Arayüzün ayarladığı değerler ---
    public static Corrector? Corrector;
    public static volatile bool UndoWithBackspace = true;
    public static volatile bool CorrectOnEnter;
    public static volatile int MinWordLength = 3;
    public static volatile HashSet<string> Excluded = new();

    /// <summary>(yazılan, düzeltilen) — arayüz iş parçacığında çağrılır.</summary>
    public static event Action<string, string>? Corrected;
    /// <summary>(geri alınan kelime, kalıcı öğrenildi mi) — arayüz iş parçacığında çağrılır.</summary>
    public static event Action<string, bool>? Undone;

    static SynchronizationContext s_ui = null!;
    static uint s_threadId;
    static nint s_keyboard, s_mouse;

    // O an yazılan kelime. Yalnızca bellekte, yalnızca tek kelime; hiçbir yere yazılmaz.
    static readonly char[] s_word = new char[MaxWord];
    static int s_len;
    static bool s_dirty;  // kelimede rakam/sembol var ya da başı bilinmiyor → dokunma
    // Cümle ortasında büyük harfle başlayan bilinmeyen kelime bir isimdir (Hazal); düzeltilmez.
    // Odak değişince (yeni bir alan) cümle başı sayılır.
    static bool s_sentenceStart = true, s_wordAtSentenceStart = true;
    static bool s_joined; // boşluksuz bir ayraçtan sonra başladı: site.com, ad@posta, Ankara'ya
    static nint s_window;
    static bool s_skipWindow;
    static bool s_capsOn, s_capsHeld;

    // Backspace ile geri alınabilecek son düzeltme
    static string? s_undoTyped, s_undoFixed;

    // Gönderilecek tuşlar. Düzeltme uygulamaya ulaşana kadar kullanıcının bastığı tuşlar da
    // sıra bozulmasın diye yutulup bu listenin sonuna eklenir (s_pending).
    //
    // Neden "gönderdim" değil de "ulaştı" bekleniyor: SendInput tuşları sistemin giriş
    // kuyruğunun SONUNA ekler. O an kuyrukta bekleyen gerçek tuşlar bizimkilerden önce
    // işlenir; onları da yutup paketin arkasından göndermek gerekir. Paketin son tuşu
    // EndMarker taşır; kanca onu görünce paket uygulamaya ulaşmış demektir.
    static readonly List<INPUT> s_queue = new(64);
    static bool s_pending;
    static nuint s_timer, s_watchdog;
    static int s_lastCall; // kancanın son çağrıldığı an (Environment.TickCount)

    // Parola denetimi beklenirken düzeltme tuşları burada durur; cevap "güvenli" gelirse kuyruğun
    // başına eklenir, gelmezse atılır ve yalnızca yutulan ayraç tuşu geri verilir.
    static readonly List<INPUT> s_fix = new(32);
    static int s_check, s_verdictFor;
    // Parola sorusu kelimenin ilk harfinde sorulur; kelime bitene kadar cevap çoğu zaman hazırdır.
    // (Chromium bir süre sorgu gelmezse erişilebilirliği kapatır; ilk sorgu 200 ms'yi bulabilir.)
    static int s_wordProbe, s_probeAnswered;
    static bool s_probeSafe;
    static int s_deferredDowns;
    static string? s_fixTyped, s_fixWord;
    static bool s_fixCanUndo;
    static ushort s_batch; // geç gelen eski bir paket işareti yenisini serbest bırakmasın
    static Action? s_afterFlush;

    public static void Init(SynchronizationContext ui)
    {
        s_ui = ui;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "Klavye", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        ready.Wait();
    }

    public static void SetEnabled(bool on) => PostThreadMessageW(s_threadId, on ? MsgStart : MsgStop, 0, 0);

    static void Loop(ManualResetEventSlim ready)
    {
        MSG msg;
        s_threadId = GetCurrentThreadId();
        PeekMessageW(&msg, 0, 0, 0, PM_NOREMOVE); // ileti kuyruğunu oluştur
        PasswordProbe.Start(s_threadId, MsgVerdict);
        ready.Set();

        while (GetMessageW(&msg, 0, 0, 0) > 0)
        {
            try
            {
                switch (msg.message)
                {
                    case MsgStart: Install(); break;
                    case MsgStop: Uninstall(); break;
                    case MsgFlush: Flush(); break;
                    case MsgMouse: UpdateMouseHook(); break;
                    case MsgVerdict: ProbeAnswered((int)msg.wParam, msg.lParam != 0); break;
                    case WM_TIMER when msg.wParam == (nint)s_watchdog: Watchdog(); break;
                    case WM_TIMER when s_verdictFor != 0: OnVerdict(s_verdictFor, safe: false); break;
                    case WM_TIMER: FlushTimedOut(); break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }

    static void Install()
    {
        if (s_keyboard != 0) return;
        s_capsOn = (GetKeyState(VK_CAPITAL) & 1) != 0;
        s_window = 0;
        s_keyboard = SetWindowsHookExW(WH_KEYBOARD_LL, &KeyboardProc, GetModuleHandleW(null), 0);
        if (s_keyboard == 0) Log.Error(new System.ComponentModel.Win32Exception());
        s_lastCall = Environment.TickCount;
        s_watchdog = SetTimer(0, 0, WatchdogMs, 0);
    }

    /// <summary>
    /// Windows, zaman aşımına uğrayan bir kancayı haber vermeden kaldırabilir; o zaman uygulama
    /// açık görünür ama hiçbir şey düzeltmez. Son 30 saniyede sistemde giriş olmuş ama kancamız
    /// 5 saniyedir hiç çağrılmamışsa kancayı yeniden kurar. (Yalnızca fare kullanıldığında da
    /// yeniden kurar; zararsız ve ucuzdur.) 30 saniyede bir uyanır, başka yükü yoktur.
    /// </summary>
    static void Watchdog()
    {
        if (s_keyboard == 0) return;
        var info = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        if (!GetLastInputInfo(&info)) return;
        int now = Environment.TickCount, input = (int)info.dwTime;
        if (now - input < (int)WatchdogMs && input - s_lastCall > 5_000)
        {
            UnhookWindowsHookEx(s_keyboard);
            s_keyboard = SetWindowsHookExW(WH_KEYBOARD_LL, &KeyboardProc, GetModuleHandleW(null), 0);
            s_lastCall = now;
        }
    }

    static void Uninstall()
    {
        if (s_verdictFor != 0) OnVerdict(s_verdictFor, safe: false);
        FlushTimedOut(); // yutulmuş tuş kalmasın
        if (s_watchdog != 0) KillTimer(0, s_watchdog);
        s_watchdog = 0;
        if (s_keyboard != 0) UnhookWindowsHookEx(s_keyboard);
        s_keyboard = 0;
        Reset();
    }

    [UnmanagedCallersOnly]
    static nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            s_lastCall = Environment.TickCount;
            var k = (KBDLLHOOKSTRUCT*)lParam;
            if ((k->dwExtraInfo & 0xFFFF0000) == EndMarker)
            {
                if ((ushort)k->dwExtraInfo == s_batch) BatchArrived();
            }
            else if (k->dwExtraInfo != Marker)
            {
                try
                {
                    if (OnKey(k, wParam is WM_KEYDOWN or WM_SYSKEYDOWN)) return 1;
                }
                catch (Exception ex)
                {
                    Reset();
                    Log.Error(ex);
                }
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    /// <returns>true: tuşu yut (yerine biz göndereceğiz).</returns>
    static bool OnKey(KBDLLHOOKSTRUCT* k, bool down)
    {
        if (k->vkCode == VK_CAPITAL)
        {
            if (down && !s_capsHeld) s_capsOn = !s_capsOn;
            s_capsHeld = down;
        }

        if (s_pending)
        {
            AddRaw(k, up: !down);
            if (down)
            {
                s_deferredDowns++;
                Track(k, allowFix: false);
            }
            return true;
        }
        return down && Track(k, allowFix: true);
    }

    static bool Track(KBDLLHOOKSTRUCT* k, bool allowFix)
    {
        int vk = (int)k->vkCode;
        if (vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_CAPITAL or (>= VK_LSHIFT and <= VK_RMENU)) return false;

        nint window = GetForegroundWindow();
        if (window != s_window)
        {
            Reset();
            s_window = window;
            s_skipWindow = ForegroundApp.ShouldSkip(window, Excluded);
        }
        var corrector = Corrector;
        if (s_skipWindow || corrector == null) return false;

        bool ctrl = Pressed(VK_CONTROL), alt = Pressed(VK_MENU);
        bool altGr = ctrl && Pressed(VK_RMENU);
        if (Pressed(VK_LWIN) || Pressed(VK_RWIN) || ((ctrl || alt) && !altGr))
        {
            Reset(); // kısayol: Ctrl+V, Alt+Tab... metin ya da imleç değişmiş olabilir
            return false;
        }

        if (vk == VK_BACK)
        {
            if (allowFix && s_undoTyped != null && UndoWithBackspace)
            {
                QueueUndo();
                return true;
            }
            s_undoTyped = null;
            if (s_len > 0) s_len--;
            else s_dirty = true; // önceki metne geri dönüldü; kelimenin başını bilmiyoruz
            UpdateMouseHook();
            return false;
        }

        char ch = vk == VK_RETURN ? '\n' : Translate(k, window, Pressed(VK_SHIFT), altGr);
        if (ch == '\uFFFF')
        {
            s_dirty = true; // ölü tuş (^, ¨): sonraki harf birleşik olacak, bilemeyiz
            return false;
        }
        if (ch < ' ' && ch != '\n')
        {
            Reset(); // ok tuşları, Home, Esc, Tab, F tuşları: imleç yer değiştirdi
            return false;
        }

        if (char.IsLetter(ch))
        {
            s_undoTyped = null;
            if (s_len == 0)
            {
                StartProbe();
                s_wordAtSentenceStart = s_sentenceStart;
            }
            if (s_len < MaxWord) s_word[s_len++] = ch;
            else s_dirty = true;
            UpdateMouseHook();
            return false;
        }

        bool boundary = ch is ' ' or '\n' or '.' or ',' or '!' or '?' or ';' or ':' or ')' or '(' or '"' or '\'';
        if (!boundary)
        {
            s_undoTyped = null;
            s_dirty = true; // rakam, @, /, - ...: e-posta, adres, kod
            UpdateMouseHook();
            return false;
        }

        bool swallow = false;
        s_undoTyped = null;
        if (allowFix && ch != '(' && (ch != '\n' || CorrectOnEnter) && s_len >= MinWordLength && !s_dirty && !s_joined)
        {
            string typed = new(s_word, 0, s_len);
            string? fix = corrector.Suggest(typed, s_wordAtSentenceStart);
            if (fix != null && !ForegroundApp.FocusIsPassword(window)) // yalnızca düzeltme varken sor
            {
                QueueFix(typed, fix, k, canUndo: ch != '\n');
                swallow = true;
            }
        }

        // Cümle sonu (. ! ? Enter) sonraki kelimeyi cümle başı yapar; boşluk durumu değiştirmez
        // ("Merhaba. Nasılsın": nokta, sonra boşluk, sonra cümle başı).
        bool sentenceEnd = ch is '.' or '!' or '?' or '\n';
        if (s_len > 0 || sentenceEnd) s_sentenceStart = sentenceEnd;

        s_len = 0;
        s_wordProbe = 0;
        s_dirty = false;
        s_joined = ch is not (' ' or '\n' or '(' or '"');
        UpdateMouseHook();
        return swallow;
    }

    static char Translate(KBDLLHOOKSTRUCT* k, nint window, bool shift, bool altGr)
    {
        // Ekran klavyesi ve bazı giriş yöntemleri karakteri doğrudan gönderir.
        if (k->vkCode == VK_PACKET) return (char)k->scanCode;

        byte* state = stackalloc byte[256];
        new Span<byte>(state, 256).Clear();
        if (shift) state[VK_SHIFT] = 0x80;
        if (s_capsOn) state[VK_CAPITAL] = 0x01;
        if (altGr) state[VK_CONTROL] = state[VK_MENU] = 0x80;

        char* buffer = stackalloc char[4];
        nint layout = GetKeyboardLayout(GetWindowThreadProcessId(window, null));
        // 0x4: klavye durumunu değiştirme, yoksa ölü tuşlar (^ + a = â) bozulur. Windows 10 1607+.
        int n = ToUnicodeEx(k->vkCode, k->scanCode, state, buffer, 4, 0x4, layout);
        return n == 1 ? buffer[0] : n == 0 ? '\0' : '\uFFFF';
    }

    static bool Pressed(int vk) => GetAsyncKeyState(vk) < 0;

    /// <summary>Düzeltmeyi hazırlar ama göndermeden önce odaktaki alanın parola olmadığını sorar.</summary>
    static void QueueFix(string typed, string fix, KBDLLHOOKSTRUCT* boundary, bool canUndo)
    {
        int same = CommonPrefix(typed, fix);
        s_fix.Clear();
        for (int i = same; i < typed.Length; i++) AddBackspace(s_fix);
        for (int i = same; i < fix.Length; i++) AddChar(s_fix, fix[i]);
        AddRaw(boundary, up: false); // yuttuğumuz boşluk/noktalama tuşu; her durumda geri verilir

        (s_fixTyped, s_fixWord, s_fixCanUndo) = (typed, fix, canUndo);
        s_deferredDowns = 0;
        s_pending = true;
        if (s_wordProbe != 0 && s_probeAnswered == s_wordProbe)
        {
            // Cevap hazır; yine de kancanın içinde göndermemek için kendimize ileti olarak yolla.
            s_verdictFor = s_wordProbe;
            PostThreadMessageW(s_threadId, MsgVerdict, s_wordProbe, s_probeSafe ? 1 : 0);
            return;
        }
        if (s_wordProbe == 0) PasswordProbe.Ask(s_wordProbe = ++s_check);
        s_verdictFor = s_wordProbe;
        s_timer = SetTimer(0, s_timer, VerdictTimeoutMs, 0);
    }

    static void StartProbe()
    {
        if (s_verdictFor != 0) return; // önceki düzeltmenin cevabı bekleniyor; onu ezmeyelim
        PasswordProbe.Ask(s_wordProbe = ++s_check);
    }

    static void ProbeAnswered(int request, bool safe)
    {
        (s_probeAnswered, s_probeSafe) = (request, safe);
        if (request == s_verdictFor) OnVerdict(request, safe);
    }

    static readonly bool s_trace = Environment.GetEnvironmentVariable("DUZELTICI_TANI") == "1";

    static void OnVerdict(int request, bool safe)
    {
        if (request != s_verdictFor) return; // süresi geçmiş eski cevap
        s_verdictFor = 0;
        if (s_trace) Log.Trace($"karar #{request}: {(safe ? "düzeltildi" : "atlandı (parola ya da zaman aşımı)")}");
        if (safe)
        {
            s_queue.InsertRange(0, s_fix);
            string typed = s_fixTyped!, fix = s_fixWord!;
            // Beklerken başka tuşa basıldıysa Backspace artık bu düzeltmeyi geri almamalı.
            if (s_fixCanUndo && s_deferredDowns == 0) (s_undoTyped, s_undoFixed) = (typed, fix);
            s_afterFlush = () => Corrected?.Invoke(typed, fix);
            UpdateMouseHook();
        }
        s_fix.Clear();
        s_fixTyped = s_fixWord = null;
        Flush();
    }

    /// <summary>Düzeltmeden hemen sonra Backspace: "tamam␣" → "temam". Aynı kelime ikinci kez geri
    /// alınırsa kalıcı öğrenilir (bkz. Corrector.Reject).</summary>
    static void QueueUndo()
    {
        string typed = s_undoTyped!, fix = s_undoFixed!;
        s_undoTyped = s_undoFixed = null;

        int same = CommonPrefix(typed, fix);
        AddBackspace(s_queue); // ayraç
        for (int i = same; i < fix.Length; i++) AddBackspace(s_queue);
        for (int i = same; i < typed.Length; i++) AddChar(s_queue, typed[i]);

        bool learned = Corrector?.Reject(typed) ?? false;
        typed.AsSpan().CopyTo(s_word);
        s_len = typed.Length;
        s_dirty = true; // kullanıcı bu kelimeyi böyle istiyor; devam ederse de dokunma
        Schedule(() => Undone?.Invoke(typed, learned));
    }

    static void Schedule(Action notify)
    {
        s_pending = true;
        s_afterFlush = notify;
        PostThreadMessageW(s_threadId, MsgFlush, 0, 0);
    }

    /// <summary>Kuyruktakileri tek paket olarak gönderir ve paketin ulaşmasını bekler.</summary>
    static void Flush()
    {
        if (s_queue.Count == 0)
        {
            Release();
            return;
        }
        // SendInput sırasında kanca yeniden çağrılıp kuyruğa yeni tuş ekleyebilir; bu yüzden
        // göndermeden önce kopyalayıp boşaltıyoruz.
        var batch = s_queue.ToArray();
        s_queue.Clear();
        batch[^1].SetExtraInfo(EndMarker | ++s_batch);
        s_timer = SetTimer(0, s_timer, FlushTimeoutMs, 0);
        fixed (INPUT* p = batch)
        {
            if (SendInput((uint)batch.Length, p, sizeof(INPUT)) != batch.Length)
            {
                Log.Error(new System.ComponentModel.Win32Exception());
                FlushTimedOut();
            }
        }
    }

    /// <summary>Paketimizin son tuşu kancadan geçti: bu arada yutulan tuş varsa onları gönder.</summary>
    static void BatchArrived()
    {
        if (!s_pending) return;
        if (s_queue.Count > 0) PostThreadMessageW(s_threadId, MsgFlush, 0, 0);
        else Release();
    }

    /// <summary>Paket hiç ulaşmadı (başka bir program engellemiş olabilir). Klavye asla kilitli
    /// kalmamalı: bekleyen tuşları beklemeden gönder ve bırak.</summary>
    static void FlushTimedOut()
    {
        if (!s_pending) return;
        if (s_queue.Count > 0)
        {
            var rest = s_queue.ToArray();
            s_queue.Clear();
            fixed (INPUT* p = rest) SendInput((uint)rest.Length, p, sizeof(INPUT));
        }
        Release();
    }

    static void Release()
    {
        if (s_timer != 0) KillTimer(0, s_timer);
        s_timer = 0;
        s_pending = false;

        var notify = s_afterFlush;
        s_afterFlush = null;
        if (notify != null) s_ui.Post(static n => ((Action)n!)(), notify);
    }

    static void AddRaw(KBDLLHOOKSTRUCT* k, bool up)
    {
        var ki = k->vkCode == VK_PACKET
            ? new KEYBDINPUT { wScan = (ushort)k->scanCode, dwFlags = KEYEVENTF_UNICODE }
            : new KEYBDINPUT
            {
                wVk = (ushort)k->vkCode,
                wScan = (ushort)k->scanCode,
                dwFlags = (k->flags & LLKHF_EXTENDED) != 0 ? KEYEVENTF_EXTENDEDKEY : 0,
            };
        if (up) ki.dwFlags |= KEYEVENTF_KEYUP;
        ki.dwExtraInfo = Marker;
        s_queue.Add(INPUT.Key(ki));
    }

    static void AddBackspace(List<INPUT> list)
    {
        var ki = new KEYBDINPUT { wVk = VK_BACK, wScan = 0x0E, dwExtraInfo = Marker };
        list.Add(INPUT.Key(ki));
        ki.dwFlags = KEYEVENTF_KEYUP;
        list.Add(INPUT.Key(ki));
    }

    static void AddChar(List<INPUT> list, char c)
    {
        var ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE, dwExtraInfo = Marker };
        list.Add(INPUT.Key(ki));
        ki.dwFlags |= KEYEVENTF_KEYUP;
        list.Add(INPUT.Key(ki));
    }

    static int CommonPrefix(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return i;
    }

    static void Reset(bool updateMouseHook = true)
    {
        s_len = 0;
        s_wordProbe = 0; // odak değişmiş olabilir; cevap artık geçersiz
        s_sentenceStart = true;
        s_dirty = s_joined = false;
        s_undoTyped = s_undoFixed = null;
        if (updateMouseHook) UpdateMouseHook();
    }

    /// <summary>
    /// Fare tıklaması imleci başka yere taşır; o anki kelimeyi bırakmamız gerekir. Ama fare
    /// kancası her fare hareketinde çağrılır, bu yüzden yalnızca yarım bir kelime (ya da geri
    /// alınabilir bir düzeltme) varken takılır. Boşta beklerken sıfır yük.
    /// </summary>
    static void UpdateMouseHook()
    {
        bool need = s_keyboard != 0 && (s_len > 0 || s_dirty || s_joined || s_undoTyped != null);
        if (need && s_mouse == 0)
            s_mouse = SetWindowsHookExW(WH_MOUSE_LL, &MouseProc, GetModuleHandleW(null), 0);
        else if (!need && s_mouse != 0)
        {
            UnhookWindowsHookEx(s_mouse);
            s_mouse = 0;
        }
    }

    [UnmanagedCallersOnly]
    static nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN)
        {
            Reset(updateMouseHook: false); // kancayı kendi içinden kaldırmayalım
            PostThreadMessageW(s_threadId, MsgMouse, 0, 0);
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }
}
