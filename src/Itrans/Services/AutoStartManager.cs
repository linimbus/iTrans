using System;
using Itrans.Localization;
using Microsoft.Win32;

namespace Itrans.Services;

public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Itrans";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(value)) return false;
            return string.Equals(value, GetCommand(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
                        ?? throw new InvalidOperationException(LocalizationService.Instance.T("error.autostartOpenRegistry"));

        if (enabled)
            key.SetValue(ValueName, GetCommand(), RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, false);
    }

    private static string GetCommand()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(LocalizationService.Instance.T("error.autostartAppPath"));
        return "\"" + path + "\"";
    }
}
