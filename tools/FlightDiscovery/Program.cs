using System.Net;
using System.Text.Json;
using PigeonFancierTracker.Infrastructure.Http;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

// This tool logs in using saved DPAPI credentials and fetches flight API endpoints
// to discover their JSON shapes. Output goes to stdout and fixtures/ files.

var appDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "PigeonFancierTracker");

var credentialStore = new CredentialStore(appDataDir);
var credentials = await credentialStore.TryLoadAsync();

if (credentials is null)
{
    Console.Error.WriteLine("No saved credentials found. Please log in through the app first with 'Remember credentials' checked.");
    return 1;
}

var (email, password) = credentials.Value;
Console.WriteLine($"Using saved credentials for: {email}");

var cookieContainer = new CookieContainer();
var loginService = new LoginService(cookieContainer);

Console.WriteLine("Logging in...");
var loginResult = await loginService.LoginAsync(email, password);
if (!loginResult.Success)
{
    Console.Error.WriteLine($"Login failed: {loginResult.ErrorMessage}");
    return 1;
}
Console.WriteLine("Login successful.");

// Select fancier (ID 7 from site map reconnaissance)
Console.WriteLine("Selecting fancier...");
var selectResult = await loginService.SelectFancierAsync(7);
if (!selectResult.Success)
{
    Console.Error.WriteLine($"Fancier selection failed: {selectResult.ErrorMessage}");
    return 1;
}
Console.WriteLine("Fancier selected.");

var transport = new HttpClientReadTransport(cookieContainer);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

var fixturesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "api"));
Directory.CreateDirectory(fixturesDir);

// --- Try various flight endpoint patterns ---
// The browser uses: /flight?season=1&department=1&public=false&status=notStarted&status=started&status=ended&flightType=national&flightType=regional&flightType=training
// But our dictionary can't do repeated keys. Let's try simpler forms.

var flightEndpoints = new (string label, string path, Dictionary<string, string?>? query)[]
{
    ("flight (no params)", "/api/flight", null),
    ("flight (public=false)", "/api/flight", new() { ["public"] = "false" }),
    ("flight/live (season=1, dept=1, public=false)", "/api/flight/live", new() { ["season"] = "1", ["department"] = "1", ["public"] = "false" }),
    ("season", "/api/season", null),
};

foreach (var (label, path, query) in flightEndpoints)
{
    Console.WriteLine($"\n=== {label} → {path} ===");
    var resp = await transport.GetJsonAsync(path, query);
    Console.WriteLine($"Status: {resp.StatusCode}");
    var json = FormatJson(resp.Body);
    // Only print first 3000 chars to avoid flooding
    Console.WriteLine(json.Length > 3000 ? json[..3000] + "\n... (truncated)" : json);
    var safeName = label.Replace("/", "-").Replace(" ", "-").Replace("(", "").Replace(")", "").Replace(",", "").Replace("=", "");
    await File.WriteAllTextAsync(Path.Combine(fixturesDir, $"discovery-{safeName}.json"), json);
}

// --- If any flight list returned 200, try to find ended flights ---
// Try to parse first successful flight response
Console.WriteLine("\n=== Attempting flight/{id}/results for known flights ===");

// We know flight 35 (from pigeon 152 results) and flight 216 (from pigeon 59 results)
var knownFlightIds = new[] { 35, 216 };
foreach (var flightId in knownFlightIds)
{
    Console.WriteLine($"\n--- /api/flight/{flightId}/results ---");
    var resultsResponse = await transport.GetJsonAsync($"/api/flight/{flightId}/results");
    Console.WriteLine($"Status: {resultsResponse.StatusCode}");
    var resultsJson = FormatJson(resultsResponse.Body);
    Console.WriteLine(resultsJson.Length > 5000 ? resultsJson[..5000] + "\n... (truncated)" : resultsJson);
    await File.WriteAllTextAsync(Path.Combine(fixturesDir, $"flight-{flightId}-results.json"), resultsJson);
}

// --- Also try the flight detail endpoint ---
foreach (var flightId in knownFlightIds)
{
    Console.WriteLine($"\n--- /api/flight/{flightId} (detail) ---");
    var detailResponse = await transport.GetJsonAsync($"/api/flight/{flightId}");
    Console.WriteLine($"Status: {detailResponse.StatusCode}");
    var detailJson = FormatJson(detailResponse.Body);
    Console.WriteLine(detailJson.Length > 3000 ? detailJson[..3000] + "\n... (truncated)" : detailJson);
    await File.WriteAllTextAsync(Path.Combine(fixturesDir, $"flight-{flightId}-detail.json"), detailJson);
}

// --- Also fetch pigeon results for multiple pigeons to get more data ---
Console.WriteLine("\n=== /api/pigeon (roster) ===");
var pigeonResponse = await transport.GetJsonAsync("/api/pigeon");
if (pigeonResponse.StatusCode == 200)
{
    using var pigeonDoc = JsonDocument.Parse(pigeonResponse.Body ?? "[]");
    if (pigeonDoc.RootElement.ValueKind == JsonValueKind.Array)
    {
        var pigeons = pigeonDoc.RootElement.EnumerateArray().ToList();
        Console.WriteLine($"Pigeon count: {pigeons.Count}");

        // Get results for first 3 pigeons
        foreach (var pigeon in pigeons.Take(3))
        {
            var pigeonId = pigeon.GetProperty("id").GetInt32();
            Console.WriteLine($"\n=== /api/pigeon/{pigeonId}/results ===");
            var pigeonResultsResponse = await transport.GetJsonAsync($"/api/pigeon/{pigeonId}/results", new Dictionary<string, string?>
            {
                ["page"] = "1",
                ["pageSize"] = "100",
            });
            Console.WriteLine($"Status: {pigeonResultsResponse.StatusCode}");
            var pigeonResultsJson = FormatJson(pigeonResultsResponse.Body);
            Console.WriteLine(pigeonResultsJson);
            await File.WriteAllTextAsync(Path.Combine(fixturesDir, $"pigeon-{pigeonId}-results.json"), pigeonResultsJson);
        }
    }
}

Console.WriteLine("\n\nDone! Fixtures saved to: " + fixturesDir);
return 0;

static string FormatJson(string? json)
{
    if (string.IsNullOrWhiteSpace(json)) return "(empty)";
    try
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }
    catch
    {
        return json;
    }
}
