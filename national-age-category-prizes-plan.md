# National flight prizes by age category — implementation plan

**Date:** 2026-08-23
**Goal:** For **national** flights, capture and display prize amounts and prize positions broken down by the three age categories — **Elder** (elderly), **Yearling** (1-year-old) and **Youth** — instead of treating the flight as one combined pool.

---

## 1. The problem

A national flight mixes all three age categories in one release, but pigeons are ranked and prized **separately per category** (each result row carries an `agePosition` = rank within its own age category). The number of prize positions and the prize money therefore depend on the **participant count of each category**, not on the flight's total `Subscribers`.

Today the app computes a **single** prize table from the total subscriber count:

```csharp
// FlightResultsReader — Upcoming / Active / Completed
var prizeTable = PrizeCalculator.CalculatePrizeTableWithMoney(f.Subscribers, flightType, f.EntryPrice);
var totalPrizes = PrizeCalculator.GetTotalPrizePositions(f.Subscribers);
```

For a national flight this over-counts positions and mis-states amounts, because it never splits the field into Elder / Yearling / Youth.

The per-category count is **only** available by calling the results endpoint with an `ageType` filter and reading the `count` field:

```
GET /api/flight/{id}/results?page=1&pageSize=50&ageType=Elder&activeSort=position&sortDirection=asc
→ { "items": [ ... ], "count": 768, "page": 1 }
```

`count` (768 for Elder on flight 3) is the total participants in that category — independent of `page`/`pageSize`.

## 2. The constraint

> "Only do this on the same day as the flight, else we are doing too many queries to the api endpoint."

So the three extra calls per national flight must be gated:

- **Same-day only** — `flight.Start.Date == today` (local machine date).
- **Once per flight** — never re-query once the counts are stored.
- **National only** — regional flights keep the single-pool behaviour (see open question 2).

Worst case = **3 extra GETs per national flight, on its flight day only.**

## 3. What already exists (no change needed)

| Piece | State |
|---|---|
| **Allowlist** | `PigeonFancierApiAllowlist` permits `/api/flight/{id}/results`; only the *path* is checked, so `ageType`/`activeSort`/`sortDirection`/`page`/`pageSize` query params pass through. **No allowlist change.** |
| **Response contract** | `FlightResultsResponse(Items, Count, Page)` already exposes `Count`. Reusable as-is. |
| **HTTP + snapshotting** | `PigeonFancierApiClient.GetJsonAsync(path, query)` + `RawSnapshotStore.SaveAsync` already used for every flight call. |
| **Prize maths** | `PrizeCalculator.CalculatePrizeTable` / `GetTotalPrizePositions` already take a participant count + type; we call them per category. Money is a flat **10 €/point** (`PrizeMoneyPerPoint`) — *not* the pool-share `CalculatePrizeTableWithMoney`. |
| **Migration tooling** | `DesignTimeDbContextFactory.cs` is present, so `dotnet ef migrations add` works (same path used for `AddWeatherToFlight`). |

**Confirmed API facts**
- National results carry numeric `ageType` per row: **4 = Elder** (confirmed from the `ageType=Elder` query). Values `1` and `2` are Yearling/Youth in some order (seen together in `flight-35-results.json`). *Not needed for this feature* — we send the **string** category and read `count`, so we never rely on the numeric mapping.
- The in-progress national flight (flight 3) already returns a valid `count`, so counts can be captured while the flight is **live**.

## 4. Design

### 4.1 Storage — inline columns on `FlightEntity`
Follows the existing weather-columns precedent. Prize *positions* and *amounts* are **derived** from the count, so only the counts need to be stored.

```csharp
// Entities.cs — FlightEntity
public int? AgeCategoryElderCount { get; set; }
public int? AgeCategoryYearlingCount { get; set; }
public int? AgeCategoryYouthCount { get; set; }
public DateTimeOffset? AgeCategoryCountsCapturedAtUtc { get; set; }
```

Nullable → non-national / never-captured flights stay null and fall back to today's single-pool behaviour. `AgeCategoryCountsCapturedAtUtc` doubles as the "already captured, don't re-query" flag.

*Alternative:* a child table `FlightAgeCategory(FlightId, Category, Count, CapturedAtUtc)`. More normalized/extensible but heavier; inline columns match precedent and the fixed set of exactly 3 categories. **Recommend inline.**

New EF migration: `AddAgeCategoryCountsToFlight`.

### 4.2 Contracts — `Core/Contracts/FlightContracts.cs`

```csharp
public enum AgeCategory { Elder, Yearling, Youth }   // .ToString() == API ageType value

public sealed record AgeCategoryPrizeInfo(
    AgeCategory Category,
    int Participants,
    int PrizePositions,
    decimal TotalPrizeMoney,          // Σ over tiers of Count × Points × 10 €
    IReadOnlyList<PrizeTier> PrizeTable);
```

Extend the three flight view-models with an optional breakdown (null ⇒ render as today):

```csharp
UpcomingFlightInfo   … IReadOnlyList<AgeCategoryPrizeInfo>? AgeCategoryPrizes = null
ActiveFlightInfo     … IReadOnlyList<AgeCategoryPrizeInfo>? AgeCategoryPrizes = null
CompletedFlightSummary … IReadOnlyList<AgeCategoryPrizeInfo>? AgeCategoryPrizes = null
```

### 4.3 Ingestion — new step in `FlightResultIngester.IngestAsync`

Add **Step 6: `CaptureNationalAgeCategoryCountsAsync`** after weather backfill.

> **Source decision (changed during build):** candidates come from the **Flights
> table**, not `/api/flight/live`. Both the worker and app sync with
> `SyncProfile.Quick`, which never fetches `/api/flight/live`, so no live snapshot
> is reliably present. National flights the user participates in are already
> discovered from their pigeon results and persisted (Steps 1–3), so their rows
> exist on flight day. Sourcing from the DB also avoids creating rows for
> non-participated flights, which would pollute the Completed list.

1. `candidates = Flights where Type == "national" && AgeCategoryCountsCapturedAtUtc == null`.
2. Filter in memory to `Start.Date == DateTime.Now.Date` (SQLite can't translate a
   `.Date` comparison).
3. For each such flight:
   - For each `AgeCategory`:
     ```
     GET /api/flight/{id}/results?page=1&pageSize=1&ageType={Category}&activeSort=position&sortDirection=asc
     ```
     Snapshot it, deserialize `FlightResultsResponse`, read `.Count`. (`pageSize=1` minimizes payload — we only need `count`; the user confirmed `pageSize=50` also works.)
   - Only stamp once **all three** fetches return non-null; a null means a failed/blank
     response, so leave the flight uncaptured and retry on a later sync **the same day**
     rather than persisting a partial reading. (`count: 0` is a valid empty category.)
   - Write the 3 counts + `AgeCategoryCountsCapturedAtUtc = UtcNow`; `SaveChangesAsync`.
   - Wrap per-flight in try/catch (matches existing best-effort style).

> **Incidental fix:** `BackfillFlightWeatherAsync` (Step 5, runs just before Step 6)
> ordered by `CapturedAtUtc`, which SQLite can't translate in an `ORDER BY` — it threw
> and, being upstream of Step 6 in the same method, would have blocked capture. Changed
> to `OrderBy(x => x.Id)` (Id order == capture order, the convention used elsewhere in
> the file); the "latest capture wins" choice is still made in memory, so the result is
> unchanged.

### 4.4 Prize computation — `Core/Analytics/PrizeCalculator.cs`

```csharp
public static IReadOnlyList<AgeCategoryPrizeInfo> CalculateAgeCategoryPrizes(
    params (AgeCategory Category, int? Count)[] categories)
{
    // For each category with Count > 0:
    //   tiers  = CalculatePrizeTable(count, FlightType.National)
    //              .Select(t => t with { PrizeMoneyPerPosition = t.PointsPerPosition * PrizeMoneyPerPoint })
    //   places = GetTotalPrizePositions(count)
    //   money  = tiers.Sum(t => t.Count * t.PrizeMoneyPerPosition)   // flat 10 €/point
}
```

Each category is just a smaller "subscribers" value fed to the National points table; money is a flat 10 €/point, so **no `entryPrice` and no prize pool** are involved (unlike the pool-share `CalculatePrizeTableWithMoney` used elsewhere).

### 4.5 Reader — `FlightResultsReader`

In `GetActiveFlightsAsync`, `GetUpcomingFlightsAsync`, `GetCompletedFlightSummariesAsync`: when a flight is national and its stored `FlightEntity` has counts, build `AgeCategoryPrizes` via the new helper and attach it. (Active flights read the freshly-captured counts from the DB entity by id.)

### 4.6 UI — `FlightResultsView.xaml(.cs)`

When the selected Active/Upcoming/Completed flight is national **and** has `AgeCategoryPrizes`, render **three grouped prize tables** (Elder / Yearling / Youth), each showing its participant count, prize positions and amounts, in place of the single combined table. Update the `ActivePrizeInfo` summary line to read per-category positions for national flights. Non-national flights are unchanged.

## 5. Files touched

| File | Change |
|---|---|
| `Core/Contracts/FlightContracts.cs` | `AgeCategory`, `AgeCategoryPrizeInfo`; extend 3 view-models |
| `Core/Analytics/PrizeCalculator.cs` | `CalculateAgeCategoryPrizes` helper |
| `Infrastructure/Persistence/Entities.cs` | 4 nullable columns on `FlightEntity` |
| `Infrastructure/Persistence/Migrations/*` | **New** `AddAgeCategoryCountsToFlight` (+ snapshot update) |
| `Infrastructure/Persistence/FlightResultIngester.cs` | **New** Step 6 capture, same-day + once-only + national gates |
| `Infrastructure/Persistence/FlightResultsReader.cs` | Populate `AgeCategoryPrizes` for national flights |
| `App/FlightResultsView.xaml` / `.xaml.cs` | Render per-category prize tables |
| `tests/…Core.Tests/PrizeCalculatorTests.cs` | Per-category tables / positions / amounts |
| `tests/…Infrastructure.Tests/…` | Gate behaviour (see §6) |
| `fixtures/api/flight-3-results-elder.json` (etc.) | Filtered-by-ageType fixture with `count` |

## 6. Tests

- **PrizeCalculator** — per-category positions correct; money = points × 10 € per position (e.g. national 1st = 150 pts → 1500 €); total money sums correctly; empty/zero category yields no tier.
- **Ingester gates** (fake transport / fixtures):
  - flight dated **today** & national & uncaptured → 3 calls made, counts stored, timestamp set.
  - flight **not** today → **no** calls (same-day gate).
  - already captured (`CapturedAtUtc != null`) → **no** calls (once-only gate).
  - regional flight → skipped.
  - `count` correctly parsed from the filtered response.

## 7. Decisions (confirmed 2026-08-23)

1. **Prize money** — flat **10 € per point** for every category (`PrizeMoneyPerPoint`); no pool-share, no `entryPrice` dependency. Money per position = points × 10.
2. **Scope** — **national flights only**; regional flights are already calculated correctly and stay untouched.
3. **"Same day"** — timezone is irrelevant; a plain `DateTime.Now.Date == flight.Start.Date` local comparison is fine.
4. **Season/department** — internal detail; factor `ReadSeasonAndDepartmentAsync` into a shared helper or duplicate the small block (implementer's choice).
