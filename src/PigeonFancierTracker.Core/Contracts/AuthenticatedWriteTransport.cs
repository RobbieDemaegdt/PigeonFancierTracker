namespace PigeonFancierTracker.Core.Contracts;

public interface IAuthenticatedWriteTransport
{
    Task<TransportResponse> PostJsonAsync(
        string path,
        string jsonBody,
        CancellationToken cancellationToken = default);

    Task<TransportResponse> PutJsonAsync(
        string path,
        string jsonBody,
        CancellationToken cancellationToken = default);

    Task<TransportResponse> PatchAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<TransportResponse> DeleteAsync(
        string path,
        CancellationToken cancellationToken = default);
}
