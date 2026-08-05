namespace PigeonFancierTracker.Core.Contracts;

public interface IAuthenticatedWriteTransport
{
    Task<TransportResponse> PostJsonAsync(
        string path,
        string jsonBody,
        CancellationToken cancellationToken = default);
}
