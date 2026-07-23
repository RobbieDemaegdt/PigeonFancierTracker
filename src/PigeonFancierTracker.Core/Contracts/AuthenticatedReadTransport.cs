namespace PigeonFancierTracker.Core.Contracts;

public interface IAuthenticatedReadTransport
{
    Task<TransportResponse> GetJsonAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default);
}

public sealed record TransportResponse(
    int StatusCode,
    string? ContentType,
    string Body,
    IReadOnlyDictionary<string, string> Headers,
    string? ErrorMessage = null)
{
    public bool IsSuccessStatusCode => StatusCode is >= 200 and <= 299;
}