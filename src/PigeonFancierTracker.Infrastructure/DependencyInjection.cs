using System.Net;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.AutoBid;
using PigeonFancierTracker.Infrastructure.Http;
using PigeonFancierTracker.Infrastructure.Management;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;
using PigeonFancierTracker.Infrastructure.Sync;

namespace PigeonFancierTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPigeonFancierTrackerCore(
        this IServiceCollection services,
        string databasePath)
    {
        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<RawSnapshotStore>();
        services.AddSingleton<ITrackerDataReader, TrackerDataReader>();
        services.AddSingleton<IPigeonHistoryReader, PigeonHistoryReader>();
        services.AddSingleton<IPigeonOverviewReader, PigeonOverviewReader>();
        services.AddSingleton<ITransferDataReader, TransferDataReader>();
        services.AddSingleton<ISessionStateService, SessionStateService>();
        services.AddSingleton<CookieContainer>();
        services.AddSingleton<HttpClientReadTransport>();
        services.AddSingleton<AuthenticatedReadTransportProxy>();
        services.AddSingleton<IAuthenticatedReadTransport>(sp => sp.GetRequiredService<AuthenticatedReadTransportProxy>());
        services.AddSingleton<IAuthenticatedWriteTransport, HttpClientWriteTransport>();
        services.AddSingleton<IAutoBidService, AutoBidService>();
        services.AddSingleton<ILoginService, LoginService>();
        services.AddSingleton<PigeonFancierApiClient>();
        services.AddSingleton<ISyncCoordinator, SyncCoordinator>();
        services.AddSingleton<FlightResultIngester>();
        services.AddSingleton<FoodDistributionIngester>();
        services.AddSingleton<SponsorIngester>();
        services.AddSingleton<ISponsorDataReader, SponsorDataReader>();
        services.AddSingleton<IFlightResultsReader, FlightResultsReader>();
        services.AddSingleton<IDataExporter, DataExporter>();
        services.AddSingleton<IDataImporter, DataImporter>();
        services.AddSingleton<IDataResetter, DataResetter>();
        services.AddSingleton<PedigreeDataFetcher>();
        services.AddSingleton<IBreedingDataReader, BreedingDataReader>();
        services.AddSingleton<IFinanceGuard, FinanceGuard>();
        services.AddSingleton<IFlightManager, FlightManager>();
        services.AddSingleton<IFoodManager, FoodManager>();
        services.AddSingleton<ITrainingManager, TrainingManager>();
        services.AddSingleton<ILoftManager, LoftManager>();
        services.AddSingleton<IBreedingManager, BreedingManager>();
        services.AddSingleton<IMarketAnalysisReader, MarketAnalysisReader>();
        services.AddSingleton<IRankingDataReader, RankingDataReader>();
        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddPigeonFancierTrackerWindows(
        this IServiceCollection services,
        string appDataDirectory)
    {
        services.AddSingleton<ICredentialStore>(_ => new CredentialStore(appDataDirectory));
        services.AddSingleton<ISettingsService>(_ => new SettingsService(appDataDirectory));
        services.AddSingleton<StartupManager>();
        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddPigeonFancierTrackerInfrastructure(
        this IServiceCollection services,
        string databasePath,
        string appDataDirectory)
    {
        services.AddPigeonFancierTrackerCore(databasePath);
        services.AddPigeonFancierTrackerWindows(appDataDirectory);
        return services;
    }
}
