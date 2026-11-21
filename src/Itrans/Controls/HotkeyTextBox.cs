using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;

namespace Itrans.Controls;

public sealed class HotkeyTextBox : TextBox
{
    public HotkeyTextBox()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        ContextMenu = null;
        InputMethod.SetIsInputMethodEnabled(this, false);
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += (_, e) => e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed) key = e.ImeProcessedKey;

        if (IsModifier(key))
        {
            e.Handled = true;
            return;
        }

        if (key == Key.Tab
            && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
        {
            return;
        }

        if ((key is Key.Back or Key.Delete or Key.Escape) && Keyboard.Modifiers == ModifierKeys.None)
        {
            Text = "";
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            return;
        }

        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(GetKeyName(key));

        Text = string.Join("+", parts);
        CaretIndex = Text.Length;
        e.Handled = true;
    }

    private static bool IsModifier(Key key)
        => key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;

    private static string GetKeyName(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        return key.ToString();
    }
}
