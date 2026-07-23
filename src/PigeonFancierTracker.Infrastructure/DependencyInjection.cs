using System.Net;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Http;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;
using PigeonFancierTracker.Infrastructure.Sync;

namespace PigeonFancierTracker.Infrastructure;

public static class DependencyInjection
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddPigeonFancierTrackerInfrastructure(
        this IServiceCollection services,
        string databasePath,
        string appDataDirectory)
    {
        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<RawSnapshotStore>();
        services.AddSingleton<ITrackerDataReader, TrackerDataReader>();
        services.AddSingleton<IPigeonHistoryReader, PigeonHistoryReader>();
        services.AddSingleton<ITransferDataReader, TransferDataReader>();
        services.AddSingleton<ISessionStateService, SessionStateService>();
        services.AddSingleton<CookieContainer>();
        services.AddSingleton<HttpClientReadTransport>();
        services.AddSingleton<AuthenticatedReadTransportProxy>();
        services.AddSingleton<IAuthenticatedReadTransport>(sp => sp.GetRequiredService<AuthenticatedReadTransportProxy>());
        services.AddSingleton<ILoginService, LoginService>();
        services.AddSingleton<ICredentialStore>(_ => new CredentialStore(appDataDirectory));
        services.AddSingleton<PigeonFancierApiClient>();
        services.AddSingleton<ISyncCoordinator, SyncCoordinator>();
        services.AddSingleton<IDataExporter, DataExporter>();
        services.AddSingleton<IDataImporter, DataImporter>();
        services.AddSingleton<IDataResetter, DataResetter>();
        return services;
    }
}
