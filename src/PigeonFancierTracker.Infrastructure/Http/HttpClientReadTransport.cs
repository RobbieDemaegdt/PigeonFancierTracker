using System.Net;
using System.Net.Http.Headers;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Http;

public sealed class HttpClientReadTransport : IAuthenticatedReadTransport
{
    private const string ApiOrigin = "https://www.pigeonfancier.com";
    private readonly HttpClient client;

    public HttpClientReadTransport(CookieContainer cookieContainer)
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

    public async Task<TransportResponse> GetJsonAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default)
    {
        if (!PigeonFancierApiAllowlist.IsAllowedGetPath(path))
        {
            throw new InvalidOperationException($"GET path is not allowed: {path}");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildRelativeUri(path, query));
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

            if (response.Headers.RetryAfter?.Delta is { } retryAfter)
            {
                headers["retry-after"] = ((int)Math.Max(0, retryAfter.TotalSeconds)).ToString();
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

    private static string BuildRelativeUri(
        string path,
        IReadOnlyDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0)
        {
            return path;
        }

        var normalizedQuery = string.Join('&', query
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Value is not null)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ThenBy(x => x.Value, StringComparer.Ordinal)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));
        return string.IsNullOrEmpty(normalizedQuery) ? path : $"{path}?{normalizedQuery}";
    }
}
