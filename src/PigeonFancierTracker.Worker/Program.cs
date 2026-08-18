using PigeonFancierTracker.Infrastructure;
using PigeonFancierTracker.Infrastructure.Http;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Worker;

var builder = Host.CreateApplicationBuilder(args);

var appDataDirectory = builder.Configuration["AppDataDirectory"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PigeonFancierTracker");
Directory.CreateDirectory(appDataDirectory);

var databasePath = Path.Combine(appDataDirectory, "tracker.db");

builder.Services.AddPigeonFancierTrackerCore(databasePath);
builder.Services.AddSingleton<ICredentialStore, EnvironmentCredentialStore>();
builder.Services.AddSingleton<ISettingsService>(_ => new SettingsService(appDataDirectory));

builder.Services.Configure<WorkerOptions>(opts =>
{
    builder.Configuration.GetSection("Worker").Bind(opts);
    opts.AppDataDirectory = appDataDirectory;
});
builder.Services.AddHostedService<FancierWorkerService>();

var host = builder.Build();

await host.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();

await host.RunAsync();
