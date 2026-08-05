using System.Runtime.Versioning;
using FluentAssertions;
using Microsoft.Win32;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class StartupManagerTests : IDisposable
{
    private readonly StartupManager manager = new();

    public void Dispose()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
        key?.DeleteValue("PigeonFancierTracker", throwOnMissingValue: false);
    }

    [Fact]
    public void IsEnabled_returns_false_when_not_registered()
    {
        Dispose();

        manager.IsEnabled().Should().BeFalse();
    }

    [Fact]
    public void Enable_then_IsEnabled_returns_true()
    {
        manager.Enable();

        manager.IsEnabled().Should().BeTrue();
    }

    [Fact]
    public void Disable_removes_the_registry_entry()
    {
        manager.Enable();
        manager.Disable();

        manager.IsEnabled().Should().BeFalse();
    }

    [Fact]
    public void RefreshExePath_only_updates_when_already_enabled()
    {
        manager.RefreshExePath();

        manager.IsEnabled().Should().BeFalse();
    }
}
