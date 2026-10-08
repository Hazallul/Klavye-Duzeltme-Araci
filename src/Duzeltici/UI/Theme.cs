using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace Duzeltici.UI;

internal sealed record Palette(
    bool Dark, Color Back, Color Surface, Color Hover, Color Border, Color Stroke,
    Color Text, Color SubText, Color Accent, Color OnAccent);

/// <summary>Windows 11 açılır panellerinin renkleri; sistem açık/koyu temasını ve vurgu rengini izler.</summary>
internal static class Theme
{
    const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Görev çubuğu ve tepsi menüleri "uygulama" değil "sistem" temasını kullanır.</summary>
    public static bool SystemDark => Registry.GetValue(Personalize, "SystemUsesLightTheme", 1) is 0;

    public static Palette Current()
    {
        bool dark = SystemDark;
        var accent = Accent(dark);
        return dark
            ? new(true, Hex(0x242424), Hex(0x2D2D2D), Hex(0x353535), Hex(0x3B3B3B), Hex(0x9E9E9E),
                Hex(0xFFFFFF), Hex(0xC2C2C2), accent, Hex(0x000000))
            : new(false, Hex(0xF3F3F3), Hex(0xFBFBFB), Hex(0xE9E9E9), Hex(0xDCDCDC), Hex(0x868686),
                Hex(0x1B1B1B), Hex(0x5C5C5C), accent, Hex(0xFFFFFF));
    }

    /// <summary>Kullanıcının vurgu rengi. Windows koyu temada açık, açık temada koyu tonunu kullanır.</summary>
    static Color Accent(bool dark)
    {
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent",
                    "AccentPalette", null) is byte[] { Length: >= 32 } palette)
            {
                int i = (dark ? 1 : 4) * 4; // 1 = açık ton 2, 4 = koyu ton 1
                return Color.FromArgb(palette[i], palette[i + 1], palette[i + 2]);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        return dark ? Hex(0x4CC2FF) : Hex(0x005FB8);
    }

    static Color Hex(int rgb) => Color.FromArgb(rgb >> 16 & 0xFF, rgb >> 8 & 0xFF, rgb & 0xFF);

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Windows 11 tarzı aç/kapat anahtarı.</summary>
    public static void DrawSwitch(Graphics g, Rectangle r, bool on, bool enabled, Palette p)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float h = r.Height;
        using var track = Rounded(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, h - 1), (h - 1) / 2);
        var accent = enabled ? p.Accent : p.Stroke;
        if (on)
        {
            using var fill = new SolidBrush(accent);
            g.FillPath(fill, track);
            float k = h * 0.6f;
            using var knob = new SolidBrush(p.OnAccent);
            g.FillEllipse(knob, r.Right - h / 2 - k / 2, r.Y + (h - k) / 2, k, k);
        }
        else
        {
            using var pen = new Pen(p.Stroke, 1f);
            g.DrawPath(pen, track);
            float k = h * 0.5f;
            using var knob = new SolidBrush(p.Stroke);
            g.FillEllipse(knob, r.X + h / 2 - k / 2, r.Y + (h - k) / 2, k, k);
        }
        g.SmoothingMode = SmoothingMode.None;
    }
}
