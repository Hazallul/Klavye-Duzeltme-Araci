using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Duzeltici.UI;

/// <summary>Ortak temel: kendi çizer, klavye ile odaklanır, odaktayken çerçeve gösterir.</summary>
internal abstract class FlatControl : Control
{
    protected readonly Palette P;
    protected bool Hover;

    protected FlatControl(Palette palette)
    {
        P = palette;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        BackColor = palette.Back;
    }

    protected int S(float v) => (int)Math.Round(v * DeviceDpi / 96f);

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected void DrawFocus(Graphics g, Rectangle r, float radius)
    {
        if (!Focused || !ShowFocusCues) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), radius);
        using var pen = new Pen(P.Text, S(1.5f));
        g.DrawPath(pen, path);
        g.SmoothingMode = SmoothingMode.None;
    }
}

/// <summary>Başlık + açıklama + anahtar; satırın her yerine tıklanabilir.</summary>
internal sealed class SettingRow : FlatControl
{
    readonly string _title;
    readonly string? _description;
    readonly Font _titleFont, _descFont;
    bool _checked;

    public event EventHandler? CheckedChanged;

    public SettingRow(Palette palette, string title, string? description, Font titleFont, Font descFont) : base(palette)
    {
        (_title, _description, _titleFont, _descFont) = (title, description, titleFont, descFont);
        AccessibleRole = AccessibleRole.CheckButton;
        AccessibleName = title;
        AccessibleDescription = description;
        Height = S(description == null ? 40 : 54);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Toggle();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) Toggle();
    }

    void Toggle()
    {
        Checked = !Checked;
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (Hover)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.Rounded(r, S(6));
            using var b = new SolidBrush(P.Hover);
            g.FillPath(b, path);
            g.SmoothingMode = SmoothingMode.None;
        }

        int pad = S(10), sw = S(40), sh = S(20);
        int textWidth = r.Width - pad * 3 - sw;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        int titleH = TextRenderer.MeasureText(g, _title, _titleFont, Size.Empty, flags).Height;
        int descH = _description == null ? 0 : TextRenderer.MeasureText(g, _description, _descFont, Size.Empty, flags).Height;
        int gap = _description == null ? 0 : S(2);
        int y = (r.Height - titleH - gap - descH) / 2;
        TextRenderer.DrawText(g, _title, _titleFont, new Rectangle(pad, y, textWidth, titleH), P.Text, flags);
        if (_description != null)
            TextRenderer.DrawText(g, _description, _descFont, new Rectangle(pad, y + titleH + gap, textWidth, descH), P.SubText, flags);

        Theme.DrawSwitch(g, new Rectangle(r.Right - pad - sw, (r.Height - sh) / 2, sw, sh), _checked, true, P);
        DrawFocus(g, r, S(6));
    }
}

/// <summary>Yalnızca anahtar (başlıktaki ana aç/kapat için).</summary>
internal sealed class Switch : FlatControl
{
    bool _checked;
    public event EventHandler? CheckedChanged;

    public Switch(Palette palette, string name) : base(palette)
    {
        AccessibleRole = AccessibleRole.CheckButton;
        AccessibleName = name;
        Size = new Size(S(44), S(24));
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set { _checked = value; Invalidate(); }
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Toggle(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) Toggle(); }

    void Toggle()
    {
        Checked = !Checked;
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.DrawSwitch(e.Graphics, new Rectangle(S(2), S(2), Width - S(4), Height - S(4)), _checked, true, P);
        DrawFocus(e.Graphics, ClientRectangle, Height / 2f);
    }
}

/// <summary>Yan yana seçenekler (Akıllı | Türkçe | English).</summary>
internal sealed class Segmented : FlatControl
{
    readonly string[] _items;
    readonly Font _font;
    int _selected, _hover = -1;

    public event EventHandler? SelectionChanged;

    public Segmented(Palette palette, string name, string[] items, Font font) : base(palette)
    {
        (_items, _font) = (items, font);
        AccessibleRole = AccessibleRole.PageTabList;
        AccessibleName = name;
        Height = S(32);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
            AccessibleDescription = _items[value];
        }
    }

    void Choose(int i)
    {
        if (i < 0 || i >= _items.Length || i == _selected) return;
        SelectedIndex = i;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    int IndexAt(int x) => Math.Clamp(x * _items.Length / Math.Max(Width, 1), 0, _items.Length - 1);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.X);
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); Choose(IndexAt(e.X)); }
    protected override bool IsInputKey(Keys key) => key is Keys.Left or Keys.Right || base.IsInputKey(key);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) Choose(_selected - 1);
        if (e.KeyCode == Keys.Right) Choose(_selected + 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Theme.Rounded(outer, S(6)))
        using (var fill = new SolidBrush(P.Surface))
        using (var pen = new Pen(P.Border))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        float w = (Width - S(6)) / (float)_items.Length;
        for (int i = 0; i < _items.Length; i++)
        {
            var cell = new RectangleF(S(3) + i * w, S(3), w, Height - S(6));
            bool sel = i == _selected;
            if (sel || i == _hover)
            {
                using var path = Theme.Rounded(cell, S(4));
                using var b = new SolidBrush(sel ? P.Accent : P.Hover);
                g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, _items[i], _font, Rectangle.Round(cell), sel ? P.OnAccent : P.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        g.SmoothingMode = SmoothingMode.None;
        DrawFocus(g, ClientRectangle, S(6));
    }
}

/// <summary>Metin bağlantısı gibi görünen küçük düğme.</summary>
internal sealed class TextButton : FlatControl
{
    readonly Font _font;

    public TextButton(Palette palette, string text, Font font) : base(palette)
    {
        _font = font;
        Text = text;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = text;
        Size = TextRenderer.MeasureText(text, font) + new Size(S(8), S(8));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = Text;
        Size = TextRenderer.MeasureText(Text, _font) + new Size(S(8), S(8));
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) OnClick(EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Hover)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.Rounded(ClientRectangle, S(4));
            using var b = new SolidBrush(P.Hover);
            e.Graphics.FillPath(b, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, _font, ClientRectangle, P.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        DrawFocus(e.Graphics, ClientRectangle, S(4));
    }
}
