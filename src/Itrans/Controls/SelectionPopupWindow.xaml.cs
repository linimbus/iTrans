using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Itrans.Controls;

public partial class SelectionPopupWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    public event Action? TranslateRequested;

    public SelectionPopupWindow()
    {
        InitializeComponent();
    }

    public void ShowAt(int screenX, int screenY)
    {
        var scale = GetScale(screenX, screenY);
        var workArea = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(screenX, screenY)).WorkingArea;
        var width = (int)Math.Round(Width * scale.X);
        var height = (int)Math.Round(Height * scale.Y);

        var x = screenX + 12;
        var y = screenY + 12;
        if (x + width > workArea.Right) x = screenX - width - 12;
        if (y + height > workArea.Bottom) y = screenY - height - 12;
        if (x < workArea.Left) x = workArea.Left;
        if (y < workArea.Top) y = workArea.Top;

        Left = x / scale.X;
        Top = y / scale.Y;
        ShowNoActivate();
        Show();
    }

    private void ShowNoActivate()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    private void Translate_Click(object sender, RoutedEventArgs e)
        => TranslateRequested?.Invoke();

    private static (double X, double Y) GetScale(int screenX, int screenY)
    {
        try
        {
            var point = new PointStruct { X = screenX, Y = screenY };
            var monitor = MonitorFromPoint(point, 2);
            if (monitor != IntPtr.Zero
                && GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0
                && dpiX > 0 && dpiY > 0)
            {
                return (dpiX / 96.0, dpiY / 96.0);
            }
        }
        catch
        {
        }

        return (1.0, 1.0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(PointStruct pt, uint dwFlags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
