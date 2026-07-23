using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Http;

public sealed class AuthenticatedReadTransportProxy : IAuthenticatedReadTransport
{
    private readonly object gate = new();
    private IAuthenticatedReadTransport? current;

    public void SetTransport(IAuthenticatedReadTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        lock (gate)
        {
            current = transport;
        }
    }

    public void ClearTransport()
    {
        lock (gate)
        {
            current = null;
        }
    }

    public Task<TransportResponse> GetJsonAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default)
    {
        IAuthenticatedReadTransport? transport;
        lock (gate)
        {
            transport = current;
        }

        return transport is null
            ? Task.FromException<TransportResponse>(new InvalidOperationException("Geen geauthenticeerde transport verbonden. Meld je eerst aan."))
            : transport.GetJsonAsync(path, query, cancellationToken);
    }
}
