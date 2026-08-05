using System.Text.Json;
using System.Text.Json.Serialization;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed partial class SettingsService : ISettingsService
{
    private readonly string filePath;

    public SettingsService(string appDataDirectory)
    {
        filePath = Path.Combine(appDataDirectory, "settings.json");
        Load();
    }

    public bool LaunchOnStartup { get; set; }
    public bool AutoDailySync { get; set; }
    public bool DarkMode { get; set; }
    public DateOnly? LastAutoSyncDate { get; set; }

    public void Save()
    {
        var data = new SettingsData
        {
            LaunchOnStartup = LaunchOnStartup,
            AutoDailySync = AutoDailySync,
            DarkMode = DarkMode,
            LastAutoSyncDate = LastAutoSyncDate?.ToString("yyyy-MM-dd"),
        };

        var json = JsonSerializer.Serialize(data, SettingsJsonContext.Default.SettingsData);
        var tempPath = filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(filePath)) return;

            var json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsData);
            if (data is null) return;

            LaunchOnStartup = data.LaunchOnStartup;
            AutoDailySync = data.AutoDailySync;
            DarkMode = data.DarkMode;
            if (DateOnly.TryParse(data.LastAutoSyncDate, out var date))
                LastAutoSyncDate = date;
        }
        catch (JsonException)
        {
            // Corrupt file — reset to defaults silently.
        }
    }

    private sealed class SettingsData
    {
        public bool LaunchOnStartup { get; set; }
        public bool AutoDailySync { get; set; }
        public bool DarkMode { get; set; }
        public string? LastAutoSyncDate { get; set; }
    }

    [JsonSerializable(typeof(SettingsData))]
    private sealed partial class SettingsJsonContext : JsonSerializerContext;
}
