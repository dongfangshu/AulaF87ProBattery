using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AulaBattery.Windows;

internal static class TrayIconRenderer
{
    internal static Icon Create(int? percent, bool externalPower, bool cached = false)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        var background = cached ? Color.FromArgb(85, 90, 100) : externalPower ? Color.FromArgb(25, 101, 190) : percent switch
        {
            <= 20 => Color.FromArgb(184, 40, 40),
            <= 40 => Color.FromArgb(153, 94, 12),
            not null => Color.FromArgb(25, 112, 65),
            _ => Color.FromArgb(85, 90, 100)
        };
        using var brush = new SolidBrush(background);
        graphics.FillRoundedRectangle(brush, new RectangleF(0, 0, 32, 32), new SizeF(6, 6));
        if (externalPower)
        {
            Point[] bolt = [new(18, 3), new(7, 18), new(14, 18), new(12, 29), new(25, 12), new(18, 12)];
            graphics.FillPolygon(Brushes.White, bolt);
        }
        else
        {
            using var font = new Font("Segoe UI", percent == 100 ? 17 : 22, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };
            graphics.DrawString(percent?.ToString() ?? "?", font, Brushes.White,
                new RectangleF(0, 0, 32, 32), format);
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
