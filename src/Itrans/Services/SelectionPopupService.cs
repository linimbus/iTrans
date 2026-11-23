using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Itrans.Controls;

namespace Itrans.Services;

public sealed class SelectionPopupService : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int CursorShowing = 0x00000001;

    private readonly LowLevelMouseProc _proc;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _doubleClickTimer;
    private IntPtr _hook;
    private SelectionPopupWindow? _popup;
    private bool _mouseDown;
    private bool _dragging;
    private bool _downInPopup;
    private int _downX;
    private int _downY;
    private int _doubleClickX;
    private int _doubleClickY;
    private long _lastClickTicks;
    private int _lastClickX;
    private int _lastClickY;
    private bool _disposed;

    public SelectionPopupService()
    {
        _proc = HookCallback;
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (_, _) => HidePopup();
        _doubleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _doubleClickTimer.Tick += (_, _) =>
        {
            _doubleClickTimer.Stop();
            ShowPopupAfterDoubleClick();
        };
    }

    public void UpdateEnabled(bool enabled)
    {
        if (enabled) Start();
        else Stop();
    }

    public void Start()
    {
        if (_disposed || _hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        HidePopup();
        _doubleClickTimer.Stop();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var message = wParam.ToInt32();
                if (message is WmLButtonDown or WmMouseMove or WmLButtonUp)
                {
                    var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
                    var x = data.pt.X;
                    var y = data.pt.Y;

                    if (message == WmLButtonDown) OnMouseDown(x, y);
                    else if (message == WmMouseMove) OnMouseMove(x, y);
                    else OnMouseUp(x, y);
                }
            }
            catch
            {
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void OnMouseDown(int x, int y)
    {
        _doubleClickTimer.Stop();
        _mouseDown = true;
        _dragging = false;
        _downX = x;
        _downY = y;
        _downInPopup = IsPointInPopup(x, y);

        if (_popup != null && !_downInPopup)
        {
            Application.Current.Dispatcher.BeginInvoke((Action)HidePopup);
        }
    }

    private void OnMouseMove(int x, int y)
    {
        if (!_mouseDown || _downInPopup) return;
        if (Math.Abs(x - _downX) <= 6 && Math.Abs(y - _downY) <= 6) return;
        _doubleClickTimer.Stop();
        _dragging = true;
    }

    private void OnMouseUp(int x, int y)
    {
        var wasDragging = _dragging;
        if (!wasDragging && _mouseDown && !_downInPopup
            && (Math.Abs(x - _downX) > 6 || Math.Abs(y - _downY) > 6))
        {
            wasDragging = true;
        }
        var downInPopup = _downInPopup;
        _mouseDown = false;
        _dragging = false;
        _downInPopup = false;

        if (downInPopup)
        {
            _lastClickTicks = 0;
            return;
        }

        if (!wasDragging)
        {
            var now = Environment.TickCount64;
            var isDoubleClick = _lastClickTicks != 0
                && now - _lastClickTicks <= GetDoubleClickTime()
                && Math.Abs(x - _lastClickX) <= 4
                && Math.Abs(y - _lastClickY) <= 4;

            if (isDoubleClick)
            {
                _lastClickTicks = 0;
            }
            else
            {
                _lastClickTicks = now;
                _lastClickX = x;
                _lastClickY = y;
            }

            if (!isDoubleClick) return;

            if (!App.Instance.Settings.SelectionPopupEnabled) return;
            if (!IsTextSelectionCursorNow()) return;

            var clickForeground = GetForegroundWindow();
            if (clickForeground == IntPtr.Zero) return;

            GetWindowThreadProcessId(clickForeground, out var clickProcessId);
            if (clickProcessId == (uint)Environment.ProcessId) return;

            _doubleClickX = x;
            _doubleClickY = y;
            _doubleClickTimer.Stop();
            _doubleClickTimer.Start();
            return;
        }

        _lastClickTicks = 0;
        if (!App.Instance.Settings.SelectionPopupEnabled) return;
        if (!IsTextSelectionCursorNow()) return;

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return;

        GetWindowThreadProcessId(foreground, out var processId);
        if (processId == (uint)Environment.ProcessId) return;

        Application.Current.Dispatcher.BeginInvoke((Action)(() => ShowPopup(x, y)));
    }

    private void ShowPopupAfterDoubleClick()
    {
        if (!App.Instance.Settings.SelectionPopupEnabled) return;
        if (!IsTextSelectionCursorNow()) return;

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return;

        GetWindowThreadProcessId(foreground, out var processId);
        if (processId == (uint)Environment.ProcessId) return;

        ShowPopup(_doubleClickX, _doubleClickY);
    }

    private void ShowPopup(int x, int y)
    {
        try
        {
            HidePopup();

            var popup = new SelectionPopupWindow();
            popup.TranslateRequested += CaptureAndTranslate;
            popup.ShowAt(x, y);
            _popup = popup;
            _hideTimer.Start();
        }
        catch
        {
            HidePopup();
        }
    }

    private void CaptureAndTranslate()
    {
        HidePopup();
        App.Instance.Home?.TriggerSelectionTranslation(allowUnchangedClipboard: true);
    }

    private void HidePopup()
    {
        _hideTimer.Stop();
        var popup = _popup;
        _popup = null;
        if (popup == null) return;

        try
        {
            popup.Close();
        }
        catch
        {
        }
    }

    private bool IsPointInPopup(int x, int y)
    {
        if (_popup == null) return false;

        var hwnd = new WindowInteropHelper(_popup).Handle;
        if (hwnd == IntPtr.Zero) return false;
        if (!GetWindowRect(hwnd, out var rect)) return false;

        return x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
    }

    private static bool IsTextSelectionCursor(IntPtr cursor, IntPtr iBeam, IntPtr cross)
    {
        return cursor == iBeam || cursor == cross;
    }

    private static bool IsTextSelectionCursorNow()
    {
        var cursor = GetCursorHandle();
        var iBeam = LoadCursor(IntPtr.Zero, (IntPtr)32513);
        var cross = LoadCursor(IntPtr.Zero, (IntPtr)32515);
        return IsTextSelectionCursor(cursor, iBeam, cross);
    }

    private static IntPtr GetCursorHandle()
    {
        try
        {
            var info = new CursorInfo { cbSize = Marshal.SizeOf<CursorInfo>() };
            return GetCursorInfo(ref info) && info.flags == CursorShowing ? info.hCursor : IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public PointStruct pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public PointStruct ptScreenPos;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo pci);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}
