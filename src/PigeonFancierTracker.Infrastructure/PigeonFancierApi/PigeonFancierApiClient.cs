using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.PigeonFancierApi;

public sealed class PigeonFancierApiClient(IAuthenticatedReadTransport transport)
{
    public Task<TransportResponse> GetJsonAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default)
    {
        if (!PigeonFancierApiAllowlist.IsAllowedGetPath(path))
        {
            throw new InvalidOperationException($"GET path is not allowed: {path}");
        }

        return transport.GetJsonAsync(path, query, cancellationToken);
    }
}