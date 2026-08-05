using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PigeonFancierTracker.Infrastructure.Persistence;

[SupportedOSPlatform("windows")]
public sealed class StartupManager
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PigeonFancierTracker";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    public void Enable()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is null) return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.SetValue(ValueName, $"\"{exePath}\"");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public void RefreshExePath()
    {
        if (!IsEnabled()) return;
        Enable();
    }
}
