using FluentAssertions;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), $"pft-test-{Guid.NewGuid():N}");

    public SettingsServiceTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public void Defaults_are_false_and_null_when_no_file_exists()
    {
        var svc = new SettingsService(tempDir);

        svc.LaunchOnStartup.Should().BeFalse();
        svc.AutoDailySync.Should().BeFalse();
        svc.LastAutoSyncDate.Should().BeNull();
    }

    [Fact]
    public void Save_and_reload_round_trips_all_properties()
    {
        var svc = new SettingsService(tempDir);
        svc.LaunchOnStartup = true;
        svc.AutoDailySync = true;
        svc.LastAutoSyncDate = new DateOnly(2025, 7, 15);
        svc.Save();

        var reloaded = new SettingsService(tempDir);

        reloaded.LaunchOnStartup.Should().BeTrue();
        reloaded.AutoDailySync.Should().BeTrue();
        reloaded.LastAutoSyncDate.Should().Be(new DateOnly(2025, 7, 15));
    }

    [Fact]
    public void Corrupt_file_resets_to_defaults()
    {
        File.WriteAllText(Path.Combine(tempDir, "settings.json"), "{{not valid json!!");

        var svc = new SettingsService(tempDir);

        svc.LaunchOnStartup.Should().BeFalse();
        svc.AutoDailySync.Should().BeFalse();
        svc.LastAutoSyncDate.Should().BeNull();
    }

    [Fact]
    public void Null_date_round_trips_correctly()
    {
        var svc = new SettingsService(tempDir);
        svc.AutoDailySync = true;
        svc.LastAutoSyncDate = null;
        svc.Save();

        var reloaded = new SettingsService(tempDir);

        reloaded.LastAutoSyncDate.Should().BeNull();
    }
}
