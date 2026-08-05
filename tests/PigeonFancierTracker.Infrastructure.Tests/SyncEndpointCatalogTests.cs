using FluentAssertions;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.Sync;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class SyncEndpointCatalogTests
{
    [Fact]
    public void Quick_profile_returns_12_endpoints()
    {
        var endpoints = SyncEndpointCatalog.ForProfile(SyncProfile.Quick, 42);

        endpoints.Should().HaveCount(13);
        endpoints.Should().Contain(e => e.Path == "/api/user");
        endpoints.Should().Contain(e => e.Path == "/api/transfer"
            && e.Query != null && e.Query.ContainsKey("processed") && e.Query["processed"] == "false");
        endpoints.Should().Contain(e => e.Path == "/api/transfer"
            && e.Query != null && e.Query.ContainsKey("processed") && e.Query["processed"] == "true");
    }

    [Fact]
    public void Standard_profile_returns_19_endpoints()
    {
        var endpoints = SyncEndpointCatalog.ForProfile(SyncProfile.Standard, 42);

        endpoints.Should().HaveCount(19);
        endpoints.Should().Contain(e => e.Path == "/api/weather");
        endpoints.Should().Contain(e => e.Path == "/api/ranking");
        endpoints.Should().Contain(e => e.Path == "/api/flight");
    }

    [Fact]
    public void Standard_profile_marks_flight_endpoints_optional()
    {
        var endpoints = SyncEndpointCatalog.ForProfile(SyncProfile.Standard, 42);

        var optionalEndpoints = endpoints.Where(e => e.Optional).ToList();
        optionalEndpoints.Should().HaveCount(2);
        optionalEndpoints.Should().Contain(e => e.Path == "/api/flight");
        optionalEndpoints.Should().Contain(e => e.Path == "/api/flight/live");
    }

    [Fact]
    public void Fancier_id_is_interpolated_into_paths_and_queries()
    {
        var endpoints = SyncEndpointCatalog.ForProfile(SyncProfile.Standard, 99);

        endpoints.Should().Contain(e => e.Path == "/api/fancier/99");
        endpoints.Should().Contain(e => e.Path == "/api/fancier/99/pigeons");
        endpoints.Should().Contain(e =>
            e.Query != null && e.Query.ContainsKey("fancierId") && e.Query["fancierId"] == "99");
    }
}
