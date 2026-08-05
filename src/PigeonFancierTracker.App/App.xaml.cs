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
				services.AddTransient<FlightResultsView>();
				services.AddTransient<DataView>();
				services.AddSingleton<MainWindow>();
			})
			.Build();

		host.Start();
		host.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();

		var settings = host.Services.GetRequiredService<ISettingsService>();
		if (settings.DarkMode)
			ApplyTheme(dark: true);

		var window = host.Services.GetRequiredService<MainWindow>();
		MainWindow = window;
		window.Show();
	}

	public void ApplyTheme(bool dark)
	{
		var dictionaries = Resources.MergedDictionaries;
		dictionaries.Clear();
		var themeUri = dark
			? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
			: new Uri("Themes/LightTheme.xaml", UriKind.Relative);
		dictionaries.Add(new ResourceDictionary { Source = themeUri });
	}

	protected override void OnExit(ExitEventArgs e)
	{
		host?.StopAsync().GetAwaiter().GetResult();
		host?.Dispose();
		base.OnExit(e);
	}
}
