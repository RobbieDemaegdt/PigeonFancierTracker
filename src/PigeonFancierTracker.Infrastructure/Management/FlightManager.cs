using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Management;

public sealed class FlightManager(
    IDbContextFactory<AppDbContext> contextFactory,
    PigeonFancierApiClient apiClient,
    IAuthenticatedWriteTransport writeTransport,
    RawSnapshotStore snapshotStore,
    ILogger<FlightManager> logger) : IFlightManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<FlightEnrollmentPlan> BuildEnrollmentPlanAsync(
        int fancierId,
        CancellationToken cancellationToken = default)
    {
        var pigeons = await ReadPigeonsAsync(fancierId, cancellationToken);
        var nameTranslations = await ReadNameTranslationsAsync(fancierId, cancellationToken);
        var flightsThisWeek = await CountFlightsThisWeekAsync(fancierId, cancellationToken);
        var flights = await FetchUpcomingFlightsAsync(fancierId, cancellationToken);
        var weather = await ReadWeatherAsync(fancierId, cancellationToken);

        if (flights.Count == 0)
        {
            logger.LogInformation("No upcoming flights found for enrollment");
            return new FlightEnrollmentPlan([], ["No upcoming flights with open enrollment"]);
        }

        var pigeonLookup = pigeons
            .Where(p => p.Id.HasValue)
            .ToDictionary(p => p.Id!.Value);

        var actions = new List<FlightEnrollmentAction>();
        var skipped = new List<string>();

        foreach (var flight in flights)
        {
            var distanceCategory = DistanceProfileCalculator.Classify(flight.Distance);
            var flightWeather = FindWeatherForDate(weather, flight.Start);

            var eligibility = await FetchFlightEligibilityAsync(flight.Id, cancellationToken);
            if (eligibility is null)
            {
                skipped.Add($"Flight {flight.Id} ({flight.Location?.Name}, {flight.Distance}km): failed to fetch eligibility");
                continue;
            }

            var alreadySubscribed = new HashSet<int>(
                (eligibility.Subscriptions ?? []).Select(s => s.PigeonId));

            var eligibleIds = (eligibility.Eligible ?? [])
                .Where(e => e.Status == "success")
                .Select(e => e.PigeonId)
                .Where(id => !alreadySubscribed.Contains(id))
                .ToList();

            if (eligibleIds.Count == 0)
            {
                skipped.Add($"Flight {flight.Id} ({flight.Location?.Name}, {flight.Distance}km): no eligible pigeons");
                continue;
            }

            var candidates = new List<(int PigeonId, string Name, double Score, string Reason)>();

            foreach (var pigeonId in eligibleIds)
            {
                var thisWeekCount = flightsThisWeek.GetValueOrDefault(pigeonId, 0);
                if (thisWeekCount >= 2)
                {
                    skipped.Add($"Pigeon {pigeonId}: at max 2 flights/week");
                    continue;
                }

                if (!pigeonLookup.TryGetValue(pigeonId, out var pigeon))
                {
                    skipped.Add($"Pigeon {pigeonId}: not found in roster");
                    continue;
                }

                var name = PigeonNameResolver.CreateDisplayName(pigeon, nameTranslations);
                var (score, reason) = ScorePigeonForFlight(pigeon, distanceCategory, flightWeather);
                candidates.Add((pigeonId, name, score, reason));
            }

            foreach (var (pigeonId, name, score, reason) in candidates.OrderByDescending(c => c.Score))
            {
                var thisWeekCount = flightsThisWeek.GetValueOrDefault(pigeonId, 0);
                if (thisWeekCount >= 2) continue;

                actions.Add(new FlightEnrollmentAction(
                    flight.Id,
                    flight.Type ?? "unknown",
                    flight.Distance,
                    distanceCategory.ToString(),
                    flight.Start,
                    flight.Location?.Name,
                    pigeonId,
                    name,
                    score,
                    reason));

                flightsThisWeek[pigeonId] = thisWeekCount + 1;
            }
        }

        logger.LogInformation("Flight plan: {ActionCount} enrollments across {FlightCount} flights, {SkipCount} skipped",
            actions.Count, flights.Count, skipped.Count);

        return new FlightEnrollmentPlan(actions, skipped);
    }

    public async Task<int> ExecuteEnrollmentAsync(
        FlightEnrollmentPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (plan.Actions.Count == 0)
            return 0;

        var byFlight = plan.Actions
            .GroupBy(a => a.FlightId)
            .ToList();

        var enrolled = 0;

        foreach (var group in byFlight)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var flightId = group.Key;
            var pigeonIds = group.Select(a => a.PigeonId).ToArray();
            var first = group.First();

            logger.LogInformation(
                "Subscribing {Count} pigeons to {Type} flight {FlightId} ({Location}, {Distance}km {Category}): [{Pigeons}]",
                pigeonIds.Length, first.FlightType, flightId,
                first.Location, first.DistanceKm, first.DistanceCategory,
                string.Join(", ", group.Select(a => $"{a.PigeonName}({a.PigeonId})")));

            try
            {
                var request = new FlightSubscriptionRequest(Add: pigeonIds, Remove: []);
                var json = JsonSerializer.Serialize(request, JsonOptions);
                var response = await writeTransport.PostJsonAsync($"/api/flight/{flightId}/subscriptions", json, cancellationToken);

                if (response.StatusCode >= 200 && response.StatusCode < 300)
                {
                    logger.LogInformation("Flight {FlightId}: enrolled {Count} pigeons", flightId, pigeonIds.Length);
                    enrolled += pigeonIds.Length;
                }
                else
                {
                    logger.LogWarning("Flight {FlightId}: enrollment failed — HTTP {Status}: {Body}",
                        flightId, response.StatusCode, response.Body);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Flight {FlightId}: enrollment request failed", flightId);
            }
        }

        return enrolled;
    }

    private static (double Score, string Reason) ScorePigeonForFlight(
        PigeonDto pigeon,
        DistanceCategory distance,
        WeatherForecastDto? weather)
    {
        var skills = pigeon.Skills;
        if (skills is null)
            return (0, "no skill data");

        double baseScore = distance switch
        {
            DistanceCategory.Short => Sum(skills.Speed, skills.Aerodynamics, skills.Intelligence),
            DistanceCategory.Middle => Sum(skills.Speed, skills.Stamina, skills.Technique),
            DistanceCategory.Long => Sum(skills.Stamina, skills.Navigation, skills.Intelligence),
            _ => Sum(skills.Speed, skills.Stamina, skills.Intelligence),
        };

        var parts = new List<string>();
        parts.Add(distance switch
        {
            DistanceCategory.Short => $"short-skills={baseScore:F0}",
            DistanceCategory.Middle => $"mid-skills={baseScore:F0}",
            DistanceCategory.Long => $"long-skills={baseScore:F0}",
            _ => $"skills={baseScore:F0}",
        });

        double formBonus = (double)(skills.Form ?? 0) * 0.5;
        if (formBonus > 0) parts.Add($"form+{formBonus:F1}");

        double expBonus = (double)(skills.Experience ?? 0) * 0.3;
        if (expBonus > 0) parts.Add($"exp+{expBonus:F1}");

        double weatherBonus = 0;
        if (weather is not null)
        {
            if (weather.Beaufort is >= 5 && skills.Aerodynamics is not null)
            {
                weatherBonus += (double)skills.Aerodynamics * 0.4;
                parts.Add($"wind-aero+{weatherBonus:F1}");
            }

            if (weather.Day == false && skills.Nightvision is not null)
            {
                var nightBonus = (double)skills.Nightvision * 0.4;
                weatherBonus += nightBonus;
                parts.Add($"night+{nightBonus:F1}");
            }
        }

        var total = baseScore + formBonus + expBonus + weatherBonus;
        return (total, string.Join(", ", parts));
    }

    private static double Sum(params decimal?[] values)
    {
        double total = 0;
        foreach (var v in values)
            total += (double)(v ?? 0);
        return total;
    }

    private static WeatherForecastDto? FindWeatherForDate(
        IReadOnlyList<WeatherForecastDto> forecasts,
        DateTime flightStart)
    {
        return forecasts.FirstOrDefault(w =>
            w.Date.HasValue && w.Date.Value.Date == flightStart.Date);
    }

    private async Task<FlightSubscriptionsResponse?> FetchFlightEligibilityAsync(int flightId, CancellationToken ct)
    {
        var response = await apiClient.GetJsonAsync($"/api/flight/{flightId}/subscriptions", cancellationToken: ct);

        if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(response.Body))
        {
            logger.LogWarning("Failed to fetch eligibility for flight {FlightId}: HTTP {Status}", flightId, response.StatusCode);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FlightSubscriptionsResponse>(response.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse eligibility for flight {FlightId}", flightId);
            return null;
        }
    }

    private async Task<IReadOnlyList<PigeonDto>> ReadPigeonsAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/pigeon"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null) return [];

        try
        {
            return JsonSerializer.Deserialize<List<PigeonDto>>(snapshot.ResponseBodyJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<PigeonNameTranslations> ReadNameTranslationsAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var snapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint.StartsWith("/api/translation/")
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.CapturedAtUtc)
            .Take(2)
            .ToListAsync(ct);

        return PigeonNameResolver.ReadTranslations(snapshots);
    }

    private async Task<Dictionary<int, int>> CountFlightsThisWeekAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var weekStart = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek + 1);
        var weekEnd = weekStart.AddDays(7);

        var results = await db.FlightResults
            .AsNoTracking()
            .Where(r => r.FancierId == fancierId)
            .Join(db.Flights.AsNoTracking(),
                r => r.FlightId,
                f => f.Id,
                (r, f) => new { r.PigeonId, f.Start })
            .Where(x => x.Start >= weekStart && x.Start < weekEnd)
            .ToListAsync(ct);

        return results
            .GroupBy(x => x.PigeonId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private async Task<IReadOnlyList<FlightDto>> FetchUpcomingFlightsAsync(int fancierId, CancellationToken ct)
    {
        var (season, department) = await ReadSeasonAndDepartmentAsync(fancierId, ct);
        if (season is null || department is null)
        {
            logger.LogWarning("Cannot fetch flights: season or department unknown");
            return [];
        }

        var query = new Dictionary<string, string?>
        {
            ["season"] = season.Value.ToString(),
            ["department"] = department.Value.ToString(),
            ["public"] = "false",
            ["status"] = "notStarted",
        };

        var response = await apiClient.GetJsonAsync("/api/flight", query, ct);
        await snapshotStore.SaveAsync("/api/flight", query, response, fancierId, cancellationToken: ct);

        if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(response.Body))
        {
            logger.LogWarning("Failed to fetch flights: HTTP {Status}", response.StatusCode);
            return [];
        }

        try
        {
            var flights = JsonSerializer.Deserialize<List<FlightDto>>(response.Body, JsonOptions) ?? [];
            var enrollmentDeadline = DateTime.UtcNow.AddHours(12);

            return flights
                .Where(f => f.CanSubscribe && f.Start > DateTime.UtcNow && f.Start > enrollmentDeadline)
                .OrderBy(f => f.Start)
                .ToList();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse flight list");
            return [];
        }
    }

    private async Task<IReadOnlyList<WeatherForecastDto>> ReadWeatherAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/weather"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null)
        {
            var response = await apiClient.GetJsonAsync("/api/weather", cancellationToken: ct);
            await snapshotStore.SaveAsync("/api/weather", null, response, fancierId, cancellationToken: ct);

            if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(response.Body))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<WeatherForecastDto>>(response.Body, JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        try
        {
            return JsonSerializer.Deserialize<List<WeatherForecastDto>>(snapshot.ResponseBodyJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<(int? Season, int? Department)> ReadSeasonAndDepartmentAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        int? season = null;
        int? department = null;

        var seasonSnapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.Endpoint == "/api/season"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (seasonSnapshot is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(seasonSnapshot.ResponseBodyJson);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.TryGetProperty("active", out var active) && active.GetBoolean())
                        {
                            if (item.TryGetProperty("id", out var id))
                                season = id.GetInt32();
                            break;
                        }
                    }
                }
            }
            catch (JsonException) { }
        }

        var fancierSnapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/fancier/selected"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (fancierSnapshot is not null)
        {
            try
            {
                var fancier = JsonSerializer.Deserialize<SelectedFancierDto>(fancierSnapshot.ResponseBodyJson, JsonOptions);
                department = fancier?.Department;
            }
            catch (JsonException) { }
        }

        return (season, department);
    }
}
