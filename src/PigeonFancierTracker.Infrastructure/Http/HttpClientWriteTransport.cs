using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Http;

public sealed class HttpClientWriteTransport : IAuthenticatedWriteTransport
{
    private const string ApiOrigin = "https://www.pigeonfancier.com";
    private static readonly Regex[] AllowedPostPaths =
    [
        new(@"^/api/transfer/bid$", RegexOptions.Compiled),
        new(@"^/api/flight/\d+/subscriptions$", RegexOptions.Compiled),
        new(@"^/api/fancier/items$", RegexOptions.Compiled),
        new(@"^/api/couple$", RegexOptions.Compiled),
        new(@"^/api/barn$", RegexOptions.Compiled),
    ];
    private static readonly Regex[] AllowedPutPaths =
    [
        new(@"^/api/fancier/distribution$", RegexOptions.Compiled),
    ];
    private static readonly Regex[] AllowedPatchPaths =
    [
        new(@"^/api/fancier/trainingtype/(general|conditional|strategic)$", RegexOptions.Compiled),
        new(@"^/api/barn$", RegexOptions.Compiled),
    ];
    private static readonly Regex[] AllowedDeletePaths =
    [
    ];
    private readonly HttpClient client;

    public HttpClientWriteTransport(CookieContainer cookieContainer)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookieContainer,
            AutomaticDecompression = DecompressionMethods.All,
        };
        client = new HttpClient(handler)
        {
            BaseAddress = new Uri(ApiOrigin),
        };
    }

    public Task<TransportResponse> PostJsonAsync(
        string path,
        string jsonBody,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedPostPaths.Any(p => p.IsMatch(path)))
            throw new InvalidOperationException($"POST path is not allowed: {path}");

        return SendJsonAsync(HttpMethod.Post, path, jsonBody, cancellationToken);
    }

    public Task<TransportResponse> PutJsonAsync(
        string path,
        string jsonBody,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedPutPaths.Any(p => p.IsMatch(path)))
            throw new InvalidOperationException($"PUT path is not allowed: {path}");

        return SendJsonAsync(HttpMethod.Put, path, jsonBody, cancellationToken);
    }

    public Task<TransportResponse> PatchAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedPatchPaths.Any(p => p.IsMatch(path)))
            throw new InvalidOperationException($"PATCH path is not allowed: {path}");

        return SendJsonAsync(HttpMethod.Patch, path, "{}", cancellationToken);
    }

    public Task<TransportResponse> DeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedDeletePaths.Any(p => p.IsMatch(path)))
            throw new InvalidOperationException($"DELETE path is not allowed: {path}");

        return SendJsonAsync(HttpMethod.Delete, path, "{}", cancellationToken);
    }

    private async Task<TransportResponse> SendJsonAsync(
        HttpMethod method,
        string path,
        string jsonBody,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(method, path) { Content = content };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (response.Content.Headers.ContentType?.ToString() is { } contentType)
            {
                headers["content-type"] = contentType;
            }

            return new TransportResponse(
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.MediaType,
                body,
                headers);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new TransportResponse(
                0,
                null,
                string.Empty,
                new Dictionary<string, string>(),
                exception.Message);
        }
    }
}
