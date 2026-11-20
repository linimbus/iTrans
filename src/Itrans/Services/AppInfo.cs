using System.Reflection;
using Itrans.Localization;

namespace Itrans.Services;

public static class AppInfo
{
    public static string Name => LocalizationService.Instance.T("app.name");

    public static string Version { get; } = ResolveVersion();

    public static string Title => $"{Name} v{Version}";

    private static string ResolveVersion()
    {
        var assembly = typeof(AppInfo).Assembly;

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var metadataIndex = informational.IndexOf('+');
            var value = metadataIndex >= 0 ? informational[..metadataIndex] : informational;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        var version = assembly.GetName().Version;
        if (version == null) return "1.0.0";
        return version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}";
    }
}
