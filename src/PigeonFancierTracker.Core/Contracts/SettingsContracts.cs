namespace PigeonFancierTracker.Core.Contracts;

public interface ISettingsService
{
    bool LaunchOnStartup { get; set; }
    bool AutoDailySync { get; set; }
    bool DarkMode { get; set; }
    DateOnly? LastAutoSyncDate { get; set; }
    void Save();
}
