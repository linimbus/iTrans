using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;

namespace Itrans.Interop;

public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private sealed class HotkeyWindow : NativeWindow
    {
        public int HotkeyId;
        public Action? Callback;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams { Caption = "Itrans.HotkeyWindow" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt64() == HotkeyId)
            {
                Callback?.Invoke();
                return;
            }
            base.WndProc(ref m);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HotkeyWindow? _window;
    private int _currentId;
    private int _nextId;
    private bool _disposed;

    public bool Register(string hotkeyText, Action callback, out string error)
    {
        error = "";
        if (_disposed)
        {
            error = "快捷键管理器已释放";
            return false;
        }

        UnregisterCurrent();
        _window ??= new HotkeyWindow();
        _window.Callback = callback;

        if (!TryParse(hotkeyText, out var modifiers, out var virtualKey, out var parseError))
        {
            error = parseError;
            return false;
        }

        var id = NextId();
        if (!RegisterHotKey(_window.Handle, id, modifiers | ModNoRepeat, virtualKey))
        {
            var code = Marshal.GetLastWin32Error();
            error = code == ErrorHotkeyAlreadyRegistered
                ? $"快捷键 {hotkeyText} 已被其他程序占用"
                : $"注册快捷键失败（错误码 {code}）";
            return false;
        }

        _currentId = id;
        _window.HotkeyId = id;
        return true;
    }

    public void UnregisterCurrent()
    {
        if (_window != null && _window.Handle != IntPtr.Zero && _currentId != 0)
        {
            UnregisterHotKey(_window.Handle, _currentId);
            _currentId = 0;
        }
    }

    private int NextId()
    {
        _nextId++;
        if (_nextId > 0xBFFF) _nextId = 1;
        return _nextId;
    }

    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey, out string error)
    {
        modifiers = 0;
        virtualKey = 0;
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "快捷键为空";
            return false;
        }

        string? keyName = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModWin;
                    break;
                default:
                    keyName = raw;
                    break;
            }
        }

        if (keyName == null)
        {
            error = "快捷键缺少主键（例如 Q）";
            return false;
        }
        if (modifiers == 0)
        {
            error = "快捷键至少需要一个修饰键（Ctrl/Alt/Shift/Win）";
            return false;
        }

        if (!Enum.TryParse<Key>(keyName, true, out var key) || key == Key.None)
        {
            if (keyName.Length == 1 && char.IsDigit(keyName[0]))
            {
                key = (Key)((int)Key.D0 + (keyName[0] - '0'));
            }
            else
            {
                error = $"无法识别的按键：{keyName}";
                return false;
            }
        }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0)
        {
            error = $"无法识别的按键：{keyName}";
            return false;
        }

        virtualKey = (uint)vk;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterCurrent();
        if (_window != null)
        {
            if (_window.Handle != IntPtr.Zero) _window.DestroyHandle();
            _window = null;
        }
    }
}
