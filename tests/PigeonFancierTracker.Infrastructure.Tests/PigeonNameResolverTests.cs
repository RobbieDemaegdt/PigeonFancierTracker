using FluentAssertions;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class PigeonNameResolverTests
{
    [Fact]
    public void ReadTranslations_returns_empty_for_no_snapshots()
    {
        var result = PigeonNameResolver.ReadTranslations([]);

        result.FirstNames.Should().BeEmpty();
        result.LastNames.Should().BeEmpty();
    }

    [Fact]
    public void ReadTranslations_parses_first_and_last_names()
    {
        var snapshots = new[]
        {
            CreateTranslationSnapshot(200, "{\"first-name\":{\"1\":\"Tom\",\"2\":\"Jan\"},\"last-name\":{\"10\":\"Ape-head\"}}"),
        };

        var result = PigeonNameResolver.ReadTranslations(snapshots);

        result.FirstNames.Should().HaveCount(2);
        result.FirstNames[1].Should().Be("Tom");
        result.FirstNames[2].Should().Be("Jan");
        result.LastNames.Should().ContainSingle();
        result.LastNames[10].Should().Be("Ape-head");
    }

    [Fact]
    public void ReadTranslations_filters_non_2xx_snapshots()
    {
        var snapshots = new[]
        {
            CreateTranslationSnapshot(404, "{\"first-name\":{\"1\":\"ShouldBeIgnored\"}}"),
            CreateTranslationSnapshot(200, "{\"first-name\":{\"2\":\"Tom\"}}"),
        };

        var result = PigeonNameResolver.ReadTranslations(snapshots);

        result.FirstNames.Should().ContainSingle();
        result.FirstNames[2].Should().Be("Tom");
    }

    [Fact]
    public void ReadTranslations_ignores_malformed_json()
    {
        var snapshots = new[]
        {
            CreateTranslationSnapshot(200, "not valid json"),
            CreateTranslationSnapshot(200, "{\"first-name\":{\"1\":\"Tom\"}}"),
        };

        var result = PigeonNameResolver.ReadTranslations(snapshots);

        result.FirstNames.Should().ContainSingle();
        result.FirstNames[1].Should().Be("Tom");
    }

    [Fact]
    public void ReadTranslations_newest_snapshot_wins()
    {
        var older = CreateTranslationSnapshot(200, "{\"first-name\":{\"1\":\"OldName\"}}",
            DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = CreateTranslationSnapshot(200, "{\"first-name\":{\"1\":\"NewName\"}}",
            DateTimeOffset.UtcNow);

        var result = PigeonNameResolver.ReadTranslations(new[] { older, newer });

        result.FirstNames[1].Should().Be("NewName");
    }

    [Fact]
    public void ReadTranslations_skips_non_integer_keys()
    {
        var snapshots = new[]
        {
            CreateTranslationSnapshot(200, "{\"first-name\":{\"abc\":\"Tom\",\"1\":\"Jan\"}}"),
        };

        var result = PigeonNameResolver.ReadTranslations(snapshots);

        result.FirstNames.Should().ContainSingle();
        result.FirstNames[1].Should().Be("Jan");
    }

    [Fact]
    public void CreateDisplayName_joins_first_and_last_name()
    {
        var translations = CreateTranslations(
            new Dictionary<int, string> { [1] = "Tom" },
            new Dictionary<int, string> { [10] = "Ape-head" });
        var pigeon = CreatePigeon(id: 7, firstNameId: 1, lastNameId: 10);

        var name = PigeonNameResolver.CreateDisplayName(pigeon, translations);

        name.Should().Be("Tom Ape-head");
    }

    [Fact]
    public void CreateDisplayName_returns_first_name_only()
    {
        var translations = CreateTranslations(
            new Dictionary<int, string> { [1] = "Tom" },
            new Dictionary<int, string>());
        var pigeon = CreatePigeon(id: 7, firstNameId: 1, lastNameId: 999);

        var name = PigeonNameResolver.CreateDisplayName(pigeon, translations);

        name.Should().Be("Tom");
    }

    [Fact]
    public void CreateDisplayName_falls_back_to_pigeon_id()
    {
        var translations = CreateTranslations(
            new Dictionary<int, string>(),
            new Dictionary<int, string>());
        var pigeon = CreatePigeon(id: 7, firstNameId: null, lastNameId: null);

        var name = PigeonNameResolver.CreateDisplayName(pigeon, translations);

        name.Should().Be("Pigeon #7");
    }

    [Fact]
    public void CreateDisplayName_falls_back_to_pigeon_without_id()
    {
        var translations = CreateTranslations(
            new Dictionary<int, string>(),
            new Dictionary<int, string>());
        var pigeon = CreatePigeon(id: null, firstNameId: null, lastNameId: null);

        var name = PigeonNameResolver.CreateDisplayName(pigeon, translations);

        name.Should().Be("Pigeon");
    }

    [Fact]
    public void CreateDisplayName_works_for_TransferPigeonDto()
    {
        var translations = CreateTranslations(
            new Dictionary<int, string> { [1] = "Tom" },
            new Dictionary<int, string> { [10] = "Ape-head" });
        var pigeon = new TransferPigeonDto(7, null, 1, 10, null, null, null, null, null, null, null, null, null, null, null, null);

        var name = PigeonNameResolver.CreateDisplayName(pigeon, translations);

        name.Should().Be("Tom Ape-head");
    }

    private static RawApiSnapshotEntity CreateTranslationSnapshot(
        int statusCode,
        string json,
        DateTimeOffset? capturedAt = null) =>
        new()
        {
            Endpoint = "/api/translation/nl",
            HttpMethod = "GET",
            StatusCode = statusCode,
            CapturedAtUtc = capturedAt ?? DateTimeOffset.UtcNow,
            ResponseBodyJson = json,
            BodySha256 = Guid.NewGuid().ToString(),
        };

    private static PigeonNameTranslations CreateTranslations(
        Dictionary<int, string> firstNames,
        Dictionary<int, string> lastNames) =>
        new(firstNames, lastNames);

    private static PigeonDto CreatePigeon(
        int? id,
        int? firstNameId,
        int? lastNameId) =>
        new(id, null, firstNameId, lastNameId, null, null, null, null, null, null, null, null, null, null, null, null, null);
}
