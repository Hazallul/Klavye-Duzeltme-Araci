using Duzeltici.Engine;
using Duzeltici.Input;

namespace Duzeltici.UI;

/// <summary>
/// Tepsi simgesine tıklayınca sağ altta açılan küçük panel. Her açılışta yeniden oluşturulur
/// ve kapanınca tamamen atılır: panel kapalıyken bellekte arayüz nesnesi kalmaz.
/// </summary>
internal sealed unsafe class SettingsFlyout : Form
{
    static readonly string[] LanguageHints =
    [
        "Kelimenin diline bakar: Türkçeyse Türkçe, İngilizceyse İngilizce düzeltir.",
        "Yalnızca Türkçe sözlüğü kullanır.",
        "Yalnızca İngilizce sözlüğü kullanır.",
    ];

    static readonly string[] StrengthHints =
    [
        "Yalnızca çok emin olduğunda düzeltir.",
        "Telefonlardaki gibi; çoğu kişi için en iyisi.",
        "Daha çok düzeltir, iki harflik hataları da dener.",
    ];

    readonly TrayApp _app;
    readonly Palette _p;
    readonly Font _titleFont, _bodyFont, _strongFont, _smallFont;
    readonly Label _title, _status, _languageLabel, _languageHint, _strengthLabel, _strengthHint;
    readonly Label _excludedLabel, _excludedInfo, _last;
    readonly Switch _master;
    readonly Segmented _language, _strength;
    readonly SettingRow _turkish, _undo, _enter, _startup;
    readonly TextButton _addLast, _editExcluded, _quit;
    readonly TextBox _excludedBox;
    readonly List<int> _dividers = [];
    bool _wasActive;
    // Form gizliyken alt denetimlerin Visible değeri hep false döner; yerleşim bu alanlara bakar.
    bool _showAddLast, _editorOpen;

    public SettingsFlyout(TrayApp app)
    {
        _app = app;
        _p = Theme.Current();
        var s = app.Settings;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = _p.Back;
        ForeColor = _p.Text;
        Text = "Düzeltici ayarları";

        _titleFont = new Font("Segoe UI Semibold", S(16), GraphicsUnit.Pixel);
        _bodyFont = new Font("Segoe UI", S(14), GraphicsUnit.Pixel);
        _strongFont = new Font("Segoe UI Semibold", S(13), GraphicsUnit.Pixel);
        _smallFont = new Font("Segoe UI", S(12), GraphicsUnit.Pixel);

        _title = MakeLabel("Düzeltici", _titleFont, _p.Text);
        _status = MakeLabel("", _smallFont, _p.SubText);
        _master = new Switch(_p, "Düzeltici açık");
        _master.CheckedChanged += (_, _) => _app.SetEnabled(_master.Checked);

        _languageLabel = MakeLabel("Dil", _strongFont, _p.Text);
        _language = new Segmented(_p, "Dil", ["Akıllı", "Türkçe", "English"], _bodyFont) { SelectedIndex = (int)s.Language };
        _languageHint = MakeHint();
        _language.SelectionChanged += (_, _) =>
        {
            s.Language = (LanguageMode)_language.SelectedIndex;
            _app.ApplySettings();
            RefreshState();
        };

        _strengthLabel = MakeLabel("Düzeltme gücü", _strongFont, _p.Text);
        _strength = new Segmented(_p, "Düzeltme gücü", ["Temkinli", "Dengeli", "Cesur"], _bodyFont) { SelectedIndex = (int)s.Sensitivity };
        _strengthHint = MakeHint();
        _strength.SelectionChanged += (_, _) =>
        {
            s.Sensitivity = (Sensitivity)_strength.SelectedIndex;
            _app.ApplySettings();
            RefreshState();
        };

        _turkish = Row("Türkçe karakterleri tamamla", "calisiyorum → çalışıyorum", s.FixTurkishChars,
            on => s.FixTurkishChars = on);
        _undo = Row("Backspace ile geri al", "Düzeltmeden hemen sonra basınca geri döner", s.UndoWithBackspace,
            on => s.UndoWithBackspace = on);
        _enter = Row("Enter ile de düzelt", "Kapalıyken parola alanlarında daha güvenli", s.CorrectOnEnter,
            on => s.CorrectOnEnter = on);
        _startup = Row("Windows ile başlat", null, Startup.IsEnabled, on => Startup.Set(on), save: false);

        _excludedLabel = MakeLabel("Hariç uygulamalar", _strongFont, _p.Text);
        _editExcluded = new TextButton(_p, "Düzenle", _smallFont);
        _editExcluded.Click += (_, _) => ToggleExcludedEditor();
        _excludedInfo = MakeHint();
        _addLast = new TextButton(_p, "", _smallFont);
        _addLast.Click += (_, _) => AddLastApp();
        _excludedBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _p.Surface,
            ForeColor = _p.Text,
            Font = _smallFont,
            Visible = false,
            AccessibleName = "Hariç uygulamalar, her satıra bir exe adı",
        };

        _last = MakeLabel("", _smallFont, _p.SubText);
        _last.AutoSize = false;
        _quit = new TextButton(_p, "Çıkış", _smallFont);
        _quit.Click += (_, _) => _app.Quit();

        Controls.AddRange([
            _title, _status, _master, _languageLabel, _language, _languageHint, _strengthLabel, _strength,
            _strengthHint, _turkish, _undo, _enter, _startup, _excludedLabel, _editExcluded, _excludedInfo,
            _addLast, _excludedBox, _last, _quit,
        ]);
        RefreshState();
    }

    int S(float v) => (int)Math.Round(v * DeviceDpi / 96f);

    Label MakeLabel(string text, Font font, Color color) =>
        new() { Text = text, Font = font, ForeColor = color, BackColor = _p.Back, AutoSize = true, UseMnemonic = false };

    Label MakeHint()
    {
        var label = MakeLabel("", _smallFont, _p.SubText);
        label.AutoSize = false;
        return label;
    }

    SettingRow Row(string title, string? description, bool value, Action<bool> set, bool save = true)
    {
        var row = new SettingRow(_p, title, description, _bodyFont, _smallFont) { Checked = value };
        row.CheckedChanged += (_, _) =>
        {
            set(row.Checked);
            if (save) _app.ApplySettings();
        };
        return row;
    }

    /// <summary>Uygulamanın durumu değişince (düzeltme oldu, sözlük yüklendi...) çağrılır.</summary>
    public void RefreshState()
    {
        var s = _app.Settings;
        _master.Checked = s.Enabled;
        _status.Text = _app.Loading ? "Sözlük yükleniyor…"
            : !s.Enabled ? "Kapalı — hiçbir şey düzeltilmiyor"
            : _app.CorrectionCount == 0 ? "Açık — yazarken düzeltiyor"
            : $"Açık — bu oturumda {_app.CorrectionCount} düzeltme";
        _languageHint.Text = LanguageHints[_language.SelectedIndex];
        _strengthHint.Text = StrengthHints[_strength.SelectedIndex];
        _excludedInfo.Text = $"{s.ExcludedApps.Count} uygulamada çalışmaz (terminal, kod editörü, parola yöneticisi…).";
        _last.Text = _app.LastCorrection is { } last ? $"Son: {last}" : "Henüz düzeltme yok";

        string? app = ForegroundApp.LastApp;
        bool canAdd = app != null && !s.ExcludedApps.Contains(app, StringComparer.OrdinalIgnoreCase);
        _showAddLast = _addLast.Visible = canAdd;
        if (canAdd) _addLast.Text = $"+ {app} uygulamasını da ekle";
        LayoutAll();
    }

    void LayoutAll()
    {
        int width = S(340), pad = S(16), inner = width - pad * 2;
        int rowX = S(6), rowWidth = width - rowX * 2;
        int y = pad;
        _dividers.Clear();

        _title.Location = new Point(pad - S(1), y);
        _master.Location = new Point(width - pad - _master.Width + S(2), y);
        y += _title.Height;
        _status.Location = new Point(pad, y);
        y += _status.Height + S(16);

        y = Section(_languageLabel, _language, _languageHint, y);
        y = Section(_strengthLabel, _strength, _strengthHint, y);

        y = Divider(y - S(4));
        foreach (var row in new[] { _turkish, _undo, _enter, _startup })
        {
            row.SetBounds(rowX, y, rowWidth, row.Height);
            y += row.Height;
        }

        y = Divider(y + S(4));
        _excludedLabel.Location = new Point(pad, y + S(3));
        _editExcluded.Location = new Point(width - pad - _editExcluded.Width + S(4), y);
        _editExcluded.Text = _editorOpen ? "Bitti" : "Düzenle";
        y += _editExcluded.Height + S(2);
        y = Hint(_excludedInfo, y, inner);
        if (_showAddLast)
        {
            _addLast.Location = new Point(pad - S(4), y);
            y += _addLast.Height;
        }
        if (_editorOpen)
        {
            _excludedBox.SetBounds(pad, y + S(4), inner, S(110));
            y += S(118);
        }

        y = Divider(y + S(8));
        _quit.Location = new Point(width - pad - _quit.Width + S(4), y);
        _last.SetBounds(pad, y, inner - _quit.Width, _quit.Height);
        _last.TextAlign = ContentAlignment.MiddleLeft;
        y += _quit.Height + S(10);

        int bottom = Bottom;
        bool placed = Visible;
        ClientSize = new Size(width, y);
        if (placed) Top = bottom - Height;
        Invalidate();

        int Section(Label label, Control control, Label hint, int top)
        {
            label.Location = new Point(pad, top);
            top += label.Height + S(6);
            control.SetBounds(pad, top, inner, control.Height);
            top += control.Height + S(6);
            return Hint(hint, top, inner) + S(14);
        }

        int Hint(Label hint, int top, int w)
        {
            int height = hint.GetPreferredSize(new Size(w, 0)).Height;
            hint.SetBounds(pad, top, w, height);
            return top + height;
        }

        int Divider(int top)
        {
            _dividers.Add(top);
            return top + S(9);
        }
    }

    void ToggleExcludedEditor()
    {
        if (_editorOpen)
        {
            SaveExcluded();
            _editorOpen = _excludedBox.Visible = false;
        }
        else
        {
            _excludedBox.Text = string.Join(Environment.NewLine, _app.Settings.ExcludedApps);
            _editorOpen = _excludedBox.Visible = true;
            _excludedBox.Focus();
        }
        RefreshState();
    }

    void SaveExcluded()
    {
        if (!_editorOpen) return;
        _app.Settings.ExcludedApps = _excludedBox.Lines
            .Select(l => l.Trim().ToLowerInvariant())
            .Where(l => l.Length > 0)
            .Distinct()
            .ToList();
        _app.ApplySettings();
    }

    void AddLastApp()
    {
        if (ForegroundApp.LastApp is not { } app) return;
        SaveExcluded();
        _app.Settings.ExcludedApps.Add(app);
        if (_editorOpen) _excludedBox.Text = string.Join(Environment.NewLine, _app.Settings.ExcludedApps);
        _app.ApplySettings();
        RefreshState();
    }

    public void ShowNearTray()
    {
        LayoutAll();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - S(12), area.Bottom - Height - S(12));
        Show();
        Activate();
        _master.Focus();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: Alt+Tab listesinde görünmesin
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11: yuvarlak köşe ve ince kenarlık (eski Windows'ta sessizce yok sayılır).
        int round = 2;
        Native.DwmSetWindowAttribute(Handle, 33, &round, sizeof(int));
        int border = ColorTranslator.ToWin32(_p.Border);
        Native.DwmSetWindowAttribute(Handle, 34, &border, sizeof(int));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(_p.Border);
        foreach (int y in _dividers) e.Graphics.DrawLine(pen, 0, y, Width, y);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _wasActive = true;
    }

    /// <summary>Başka yere tıklanınca kapan; ama Windows odak vermediyse (arka plandan açıldıysa)
    /// hemen kapanma, kullanıcı panele tıklayana ya da simgeye basana kadar açık kalsın.</summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_wasActive) Close();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveExcluded();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _titleFont.Dispose();
        _bodyFont.Dispose();
        _strongFont.Dispose();
        _smallFont.Dispose();
    }
}
