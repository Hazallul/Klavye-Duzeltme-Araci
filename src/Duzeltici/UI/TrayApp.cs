using System.Drawing.Drawing2D;
using System.Runtime;
using Duzeltici.Engine;
using Duzeltici.Input;
using Microsoft.Win32;

namespace Duzeltici.UI;

/// <summary>Bildirim alanındaki simge; uygulamanın tüm durumunu tutar.</summary>
internal sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly SynchronizationContext _ui;
    readonly RegisteredWaitHandle _showWait;
    readonly Corrector _corrector = new();
    Icon? _icon;
    SettingsFlyout? _flyout;
    long _flyoutClosedAt;
    bool _loading;

    public AppSettings Settings { get; }
    public int CorrectionCount { get; private set; }
    public string? LastCorrection { get; private set; }
    public bool Loading => _loading;

    public TrayApp(EventWaitHandle showRequest, bool openSettings)
    {
        _ui = SynchronizationContext.Current!;
        Settings = AppSettings.Load();
        foreach (var word in AppSettings.LoadLearned()) _corrector.Learn(word);

        KeyboardHook.Corrector = _corrector;
        KeyboardHook.Corrected += OnCorrected;
        KeyboardHook.Undone += OnUndone;
        KeyboardHook.Init(_ui);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Ayarlar", null, (_, _) => ShowFlyout());
        menu.Items.Add("Aç / Kapat", null, (_, _) => SetEnabled(!Settings.Enabled));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => ExitThread());

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleFlyout(); };
        UpdateTray();
        ApplySettings(save: false); // sözlükleri yükler, sonra kancayı açar
        SystemEvents.UserPreferenceChanged += OnThemeChanged;

        // İkinci kez başlatılırsa (Başlat menüsünden tekrar tıklama) ayarları aç.
        _showWait = ThreadPool.RegisterWaitForSingleObject(showRequest,
            (_, _) => _ui.Post(_ => ShowFlyout(), null), null, Timeout.Infinite, executeOnlyOnce: false);

        if (openSettings) ShowFlyout();
    }

    /// <summary>Ayarları kancaya ve düzelticiye uygular, gerekirse kaydeder.</summary>
    public void ApplySettings(bool save = true)
    {
        var s = Settings;
        _corrector.Mode = s.Language;
        _corrector.Sensitivity = s.Sensitivity;
        _corrector.FixTurkishChars = s.FixTurkishChars;
        KeyboardHook.UndoWithBackspace = s.UndoWithBackspace;
        KeyboardHook.CorrectOnEnter = s.CorrectOnEnter;
        KeyboardHook.MinWordLength = Math.Clamp(s.MinWordLength, 2, 10);
        KeyboardHook.Excluded = new HashSet<string>(s.ExcludedApps.Select(a => a.Trim().ToLowerInvariant()));
        if (save) s.Save();
        LoadDictionaries();
        UpdateHook();
    }

    public void SetEnabled(bool on)
    {
        Settings.Enabled = on;
        Settings.Save();
        UpdateHook();
        UpdateTray();
        _flyout?.RefreshState();
    }

    void UpdateHook() => KeyboardHook.SetEnabled(Settings.Enabled && !_loading);

    /// <summary>Seçili dil için gereken sözlükleri arka planda yükler (yalnızca eksik olanları).</summary>
    void LoadDictionaries()
    {
        var needed = new List<Lang>();
        if (Settings.Language != LanguageMode.English && !_corrector.HasTable(Lang.Tr)) needed.Add(Lang.Tr);
        if (Settings.Language != LanguageMode.Turkish && !_corrector.HasTable(Lang.En)) needed.Add(Lang.En);
        if (needed.Count == 0 || _loading) return;

        _loading = true;
        UpdateTray();
        Task.Run(() =>
        {
            var tables = new List<(Lang, WordTable)>();
            try
            {
                foreach (var lang in needed)
                {
                    string file = Path.Combine(AppContext.BaseDirectory, "Data", lang == Lang.Tr ? "tr.txt" : "en.txt");
                    tables.Add((lang, WordTable.Load(file, lang)));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            // Dosya okuma tamponlarını (~8 MB) hemen geri ver; uygulama günlerce açık kalacak.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            _ui.Post(_ =>
            {
                foreach (var (lang, table) in tables) _corrector.SetTable(lang, table);
                _loading = false;
                UpdateHook();
                UpdateTray();
                _flyout?.RefreshState();
                LoadDictionaries(); // bu sırada dil değiştiyse
            }, null);
        });
    }

    void OnCorrected(string typed, string fix)
    {
        CorrectionCount++;
        LastCorrection = $"{typed} → {fix}";
        _flyout?.RefreshState();
    }

    void OnUndone(string word)
    {
        CorrectionCount = Math.Max(0, CorrectionCount - 1);
        LastCorrection = $"“{word}” öğrenildi";
        AppSettings.AppendLearned(word);
        _flyout?.RefreshState();
    }

    void ToggleFlyout()
    {
        if (_flyout != null) { _flyout.Close(); return; }
        // Panel açıkken simgeye tıklamak önce paneli kapatır (odak kaybı); hemen yeniden açmayalım.
        if (Environment.TickCount64 - _flyoutClosedAt < 300) return;
        ShowFlyout();
    }

    void ShowFlyout()
    {
        if (_flyout != null)
        {
            _flyout.Activate();
            return;
        }
        _flyout = new SettingsFlyout(this);
        _flyout.FormClosed += (_, _) =>
        {
            _flyout = null;
            _flyoutClosedAt = Environment.TickCount64;
        };
        _flyout.ShowNearTray();
    }

    void OnThemeChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color) UpdateTray();
    }

    void UpdateTray()
    {
        bool on = Settings.Enabled;
        _tray.Text = _loading ? "Düzeltici — sözlük yükleniyor" : on ? "Düzeltici — açık" : "Düzeltici — kapalı";
        var old = _icon;
        _icon = DrawIcon(on && !_loading, Theme.SystemDark);
        _tray.Icon = _icon;
        old?.Dispose();
    }

    /// <summary>Tuş kapağı içinde onay işareti; kapalıyken soluk ve işaretsiz.
    /// Görev çubuğunun temasına göre beyaz ya da koyu çizilir.</summary>
    static Icon DrawIcon(bool on, bool darkTaskbar)
    {
        int size = SystemInformation.SmallIconSize.Width;
        float u = size / 16f;
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var ink = darkTaskbar ? Color.White : Color.FromArgb(0x1B, 0x1B, 0x1B);
            if (!on) ink = Color.FromArgb(120, ink);
            using var pen = new Pen(ink, 1.4f * u) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            using (var key = Theme.Rounded(new RectangleF(1.2f * u, 1.7f * u, 13.6f * u, 12.6f * u), 3f * u))
                g.DrawPath(pen, key);
            if (on)
                g.DrawLines(pen, new[] { new PointF(4.6f * u, 8.2f * u), new PointF(7f * u, 10.6f * u), new PointF(11.4f * u, 5.6f * u) });
            else
                g.DrawLine(pen, 5f * u, 8f * u, 11f * u, 8f * u);
        }
        nint handle = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    public void Quit() => ExitThread();

    protected override void ExitThreadCore()
    {
        KeyboardHook.SetEnabled(false);
        SystemEvents.UserPreferenceChanged -= OnThemeChanged;
        _showWait.Unregister(null);
        _flyout?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _icon?.Dispose();
        base.ExitThreadCore();
    }
}
