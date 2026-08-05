using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Http;

public sealed class HttpClientWriteTransport : IAuthenticatedWriteTransport
{
    private const string ApiOrigin = "https://www.pigeonfancier.com";
    private const string AllowedPath = "/api/transfer/bid";
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

    public async Task<TransportResponse> PostJsonAsync(
        string path,
        string jsonBody,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(path, AllowedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"POST path is not allowed: {path}");
        }

        try
        {
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
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
