using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Itrans.Controls;

internal static class TrayIconFactory
{
    private const int Size = 32;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon CreateIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/itrans.ico"));
            if (resource?.Stream != null)
            {
                using var stream = resource.Stream;
                using var icon = new Icon(stream, SystemInformation.SmallIconSize);
                return (Icon)icon.Clone();
            }
        }
        catch
        {
        }

        return CreateFallbackIcon();
    }

    private static Icon CreateFallbackIcon()
    {
        using var bitmap = new Bitmap(Size, Size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
            using var path = RoundedRectangle(new Rectangle(1, 1, Size - 2, Size - 2), 8);
            using var brush = new LinearGradientBrush(
                new Point(0, 0),
                new Point(Size, Size),
                Color.FromArgb(37, 99, 235),
                Color.FromArgb(124, 58, 237));
            graphics.FillPath(brush, path);

            using var font = new Font("Microsoft YaHei", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var textBrush = new SolidBrush(Color.White);
            graphics.DrawString("译", font, textBrush, new RectangleF(0, 0, Size, Size), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
