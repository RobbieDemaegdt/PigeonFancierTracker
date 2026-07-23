using FluentAssertions;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class ApiAllowlistTests
{
    [Theory]
    [InlineData("/api/user")]
    [InlineData("/api/translation/en")]
    [InlineData("/api/fancier/selected")]
    [InlineData("/api/transfer")]
    [InlineData("/api/flight/123/results")]
    public void Allows_documented_get_paths(string path)
    {
        PigeonFancierApiAllowlist.IsAllowedGetPath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("POST /api/user")]
    [InlineData("https://pigeonfancier.com/api/user")]
    [InlineData("/api/user/../admin")]
    [InlineData("/api/unknown")]
    public void Rejects_non_allowlisted_or_non_path_values(string path)
    {
        PigeonFancierApiAllowlist.IsAllowedGetPath(path).Should().BeFalse();
    }
}