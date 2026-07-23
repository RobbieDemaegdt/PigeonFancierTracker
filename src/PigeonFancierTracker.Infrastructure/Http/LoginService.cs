using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Http;

public sealed class LoginService : ILoginService
{
    private const string ApiOrigin = "https://www.pigeonfancier.com";
    private const string LoginPath = "/api/user/login?useCookies=true&useSessionCookies=false";
    private readonly HttpClient client;

    public LoginService(CookieContainer cookieContainer)
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

    public async Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { email, password });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, LoginPath) { Content = content };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new LoginResult(true);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Ongeldig e-mailadres of wachtwoord.",
                HttpStatusCode.Forbidden => "Toegang geweigerd.",
                HttpStatusCode.TooManyRequests => "Te veel aanmeldpogingen. Probeer het later opnieuw.",
                _ => $"Aanmelding mislukt (HTTP {(int)response.StatusCode}).",
            };

            return new LoginResult(false, message);
        }
        catch (HttpRequestException exception)
        {
            return new LoginResult(false, $"Kan de server niet bereiken: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LoginResult(false, "De aanmelding duurde te lang (timeout).");
        }
    }

    public async Task<LoginResult> SelectFancierAsync(
        int fancierId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/fancier/select/{fancierId}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new LoginResult(true);
            }

            return new LoginResult(false, $"Melkerselectie mislukt (HTTP {(int)response.StatusCode}).");
        }
        catch (HttpRequestException exception)
        {
            return new LoginResult(false, $"Kan de server niet bereiken: {exception.Message}");
        }
    }
}
