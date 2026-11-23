using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace Itrans.Services;

public static class SelectedTextProvider
{
    public static string? TryGetSelectedText(bool allowUnchangedClipboard = false)
    {
        var text = TryGetFromAutomation();
        if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        return TryGetFromClipboard(allowUnchangedClipboard);
    }

    public static async Task<string?> TryGetSelectedTextAsync(bool allowUnchangedClipboard, TimeSpan timeout)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(TryGetSelectedText(allowUnchangedClipboard));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var delay = Task.Delay(timeout);
        var finished = await Task.WhenAny(completion.Task, delay);
        if (finished != completion.Task) return null;

        try
        {
            return await completion.Task;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetFromAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;

            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject)
                || patternObject is not TextPattern textPattern)
            {
                return null;
            }

            var ranges = textPattern.GetSelection();
            var builder = new StringBuilder();
            foreach (var range in ranges)
            {
                var value = range.GetText(-1);
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (builder.Length > 0) builder.AppendLine();
                builder.Append(value.Trim());
            }

            return builder.Length > 0 ? builder.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    private const byte VkControl = 0x11;
    private const byte VkC = 0x43;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static string? TryGetFromClipboard(bool allowUnchangedClipboard)
    {
        var previous = ReadClipboardText();

        try
        {
            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            keybd_event(VkC, 0, 0, UIntPtr.Zero);
            keybd_event(VkC, 0, KeyEventKeyUp, UIntPtr.Zero);
            keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);

            string? latest = null;
            for (var i = 0; i < 8; i++)
            {
                Thread.Sleep(50);
                latest = ReadClipboardText();
                if (!string.IsNullOrWhiteSpace(latest)
                    && (!string.Equals(latest, previous, StringComparison.Ordinal) || allowUnchangedClipboard))
                {
                    return latest.Trim();
                }
            }

            if (allowUnchangedClipboard && !string.IsNullOrWhiteSpace(latest)) return latest.Trim();
        }
        catch
        {
        }

        return null;
    }

    private static string? ReadClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            return null;
        }
    }
}
