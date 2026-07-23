using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure;
using PigeonFancierTracker.Infrastructure.Http;
using PigeonFancierTracker.Infrastructure.Persistence;
using System.IO;
using System.Windows;

namespace PigeonFancierTracker.App;

public partial class App : Application
{
	private IHost? host;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		var appDataDirectory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"PigeonFancierTracker");
		Directory.CreateDirectory(appDataDirectory);

		host = Host.CreateDefaultBuilder()
			.ConfigureServices(services =>
			{
				services.AddPigeonFancierTrackerInfrastructure(
					Path.Combine(appDataDirectory, "tracker.db"),
					appDataDirectory);
				services.AddTransient<ConnectionView>();
				services.AddTransient<PigeonHistoryView>();
				services.AddTransient<TransferView>();
				services.AddTransient<DataView>();
				services.AddSingleton<MainWindow>();
			})
			.Build();

		host.Start();
		host.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();

		var window = host.Services.GetRequiredService<MainWindow>();
		MainWindow = window;
		window.Show();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		host?.StopAsync().GetAwaiter().GetResult();
		host?.Dispose();
		base.OnExit(e);
	}
}
