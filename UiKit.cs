using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TDNControl;

/// <summary>Màu sắc, font và hàm dựng control dùng chung cho giao diện tối của TDN Control.</summary>
internal static class UiKit
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#0D121C");
    public static readonly Color Card = ColorTranslator.FromHtml("#171F2D");
    public static readonly Color CardHover = ColorTranslator.FromHtml("#202C3D");
    public static readonly Color Border = ColorTranslator.FromHtml("#2E3C50");
    public static readonly Color Text = ColorTranslator.FromHtml("#F8FAFC");
    public static readonly Color Muted = ColorTranslator.FromHtml("#A6B5CC");
    public static readonly Color Gold = ColorTranslator.FromHtml("#F4C76B");
    public static readonly Color Green = ColorTranslator.FromHtml("#68DDB0");
    public static readonly Color Orange = ColorTranslator.FromHtml("#FB923C");
    public static readonly Color Red = ColorTranslator.FromHtml("#EF4444");
    public static readonly Color Banner = ColorTranslator.FromHtml("#3A2F12");

    public static Font Font(float size = 9.5f, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    public static Label MakeLabel(string text, Color? color = null, float size = 9.5f, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text, ForeColor = color ?? Text, BackColor = Color.Transparent, Font = Font(size, style),
        AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
    };

    public static Button MakeButton(string text, bool primary = false, int width = 100, int height = 32)
    {
        return new DashboardButton(primary)
        {
            Text = text, Width = width, Height = height,
            Font = Font(9.5f, primary ? FontStyle.Bold : FontStyle.Regular),
            Margin = new Padding(4, 0, 0, 0), AccessibleName = text
        };
    }


    public static Image? LoadImage(string resourceName)
    {
        try
        {
            using var s = typeof(UiKit).Assembly.GetManifestResourceStream(resourceName);
            if (s == null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch { return null; }
    }

    public static Icon? LoadIcon(string resourceName)
    {
        try
        {
            using var s = typeof(UiKit).Assembly.GetManifestResourceStream(resourceName);
            return s == null ? null : new Icon(s);
        }
        catch { return null; }
    }

    public static Bitmap? Resize(Image? src, int size)
    {
        if (src == null) return null;
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(src, 0, 0, size, size);
        return bmp;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Thanh tiêu đề tối (Windows 10 2004+ / 11); lỗi thì bỏ qua.</summary>
    public static void DarkTitleBar(IntPtr handle)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));
        }
        catch { }
    }
}
