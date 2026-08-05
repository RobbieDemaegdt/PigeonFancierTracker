# Flight results tracking — implementation plan

**Date:** 2026-07-29  
**Goal:** Track every pigeon's race results, persist them locally, and surface per-pigeon distance-category performance (short / middle / long) so the user can see what each pigeon scores best on.

---

## 1. What exists today

| Layer | Current state |
|---|---|
| **API allowlist** | Already permits `/api/flight`, `/api/flight/{id}`, `/api/flight/{id}/results`, `/api/pigeon/{id}/results`, `/api/ranking` |
| **Sync catalog** | Standard profile includes flight calendar and rankings but does **not** fetch individual flight results or pigeon results |
| **Domain model** | No flight or result entities; distance scores (Short / Medium / Long) are currently **derived from pigeon skills** only (e.g. Short = Speed + Aerodynamics + Intelligence) |
| **Persistence** | `RawApiSnapshotEntity` stores raw JSON; no dedicated flight or result tables |
| **UI** | No flight results view; the dashboard shows skill-derived distance scores but not actual race outcomes |
| **Analytics** | `StatsComparer` computes market/flock percentiles from skill-derived scores; `WeeklyGrowthCalculator` tracks skill growth |

### Key API shapes — CONFIRMED via live discovery (2026-07-29)

**`/api/flight/{id}`** — flight detail (confirmed from flight 35 and 216):
```json
{
  "id": 35,
  "ageType": "elder",
  "start": "2026-07-28T09:00:00",
  "location": { "id": 265, "name": "rotterdam", "lat": 51.9242, "lng": 4.48178 },
  "public": true,
  "department": 0,
  "season": 1,
  "seasonStart": "2026-07-27T00:00:00",
  "entryPrice": 0,
  "type": "training",
  "payoutType": "none",
  "status": "ended",
  "progress": 100,
  "canSubscribe": false,
  "fancierId": 11,
  "distance": 111,
  "subscribers": 100,
  "invitations": [11, 14, 21, 19, ...]
}
```

**`/api/flight/{id}/results`** — paginated race results (confirmed):
```json
{
  "items": [
    {
      "id": 126,
      "position": 3,
      "points": 0,
      "averageSpeed": 1478.42,
      "ageType": 4,
      "agePosition": 3,
      "currentSpeed": 0,
      "distance": 137,
      "remainingDistance": 0,
      "direction": 100,
      "progress": 100,
      "pigeonId": 157,
      "firstNameId": 1920,
      "lastNameId": 1113,
      "fancierId": 19,
      "fancier": "Robshot"
    }
  ],
  "count": 100,
  "page": 1
}
```

**`/api/pigeon/{id}/results`** — flat array, per-pigeon flight registrations (confirmed):
```json
[
  {
    "id": 1058,
    "position": 1,
    "points": 0,
    "ageType": 4,
    "agePosition": 1,
    "flight": {
      "id": 216,
      "ageType": "elder",
      "start": "2026-07-28T20:00:00",
      "public": false,
      "season": 0,
      "type": "regional",
      "status": "notStarted",
      "canSubscribe": false,
      "subscribers": 1
    },
    "direction": 0,
    "progress": 0,
    "pigeonId": 0
  }
]
```

**Critical observations from discovery:**
1. **`/api/pigeon/{id}/results` is unreliable for actual results** — the nested flight object is stale (shows `status: "notStarted"` even for ended flights), has no `averageSpeed`, no `location`, no `distance`. Only useful for discovering flight IDs.
2. **`distance` on result items ≠ flight distance** — each pigeon has a different `distance` value (137, 144, 121…), which is their home-to-release-point distance. The flight's `distance` (from `/api/flight/{id}`) is a nominal value (e.g. 111 km).
3. **`averageSpeed`** is the primary ranking metric (higher = better, probably m/min based on values ~1000–1500).
4. **`points: 0`** for training flights (`payoutType: "none"`). Points are likely only awarded in competitive national/regional flights.
5. **`/api/flight` list endpoint returns 400** without multi-value query params. The browser uses `status=notStarted&status=started&status=ended` which requires special handling.
6. **All 100 results returned in one page** — pagination exists but single-page responses seem normal for flight sizes.

---

## 2. Distance-category classification

The game's flights have a `distance` field (in km) on the `/api/flight/{id}` detail endpoint. The standard pigeon racing bands are:

| Category | Distance range | Relevant pigeon skills | Observed examples |
|---|---|---|---|
| **Short** (vitesse) | ≤ 300 km | Speed, Aerodynamics, Intelligence | Not yet observed (only training flights seen so far) |
| **Middle** (demi-fond) | 301 – 500 km | Speed, Stamina, Technique | Not yet observed |
| **Long** (fond) | > 500 km | Stamina, Navigation, Intelligence | Not yet observed |

These thresholds should be configurable constants so they can be tuned if the game uses different breakpoints.

**Only regional and national flights are tracked.** Training flights (`type: "training"`) are excluded from all result persistence, analytics, and distance profiles. They are low-stakes practice runs that would skew performance data (e.g. flight 216 had only 1 subscriber).

**Important:** The `distance` field on individual result items is the **pigeon-specific** home-to-release distance (varies per pigeon, e.g. 97–148km for the same flight). The flight's own `distance` from `/api/flight/{id}` is the nominal value to use for classification.

---

## 3. Implementation phases

### Phase 1 — Domain model & contracts

**New domain types** in `PigeonFancierTracker.Core`:

```
Domain/
  DistanceCategory.cs          — enum { Short, Middle, Long }
  FlightType.cs                — enum { National, Regional }  (training is filtered out at ingestion)
  FlightStatus.cs              — enum { NotStarted, Started, Ended }

Contracts/
  FlightContracts.cs           — API DTOs + reader interface
```

#### 3.1 API DTOs (in `FlightContracts.cs`)

```csharp
// Deserialized from /api/flight/{id}
public sealed record FlightDto(
    int Id,
    string AgeType,           // "elder" | "young"
    DateTime Start,
    FlightLocationDto? Location,
    bool Public,
    int Department,
    int Season,
    DateTime SeasonStart,
    decimal EntryPrice,
    string Type,              // "training" | "regional" | "national"
    string PayoutType,        // "none" | (TBD for competitive)
    string Status,            // "notStarted" | "started" | "ended"
    int Progress,             // 0–100
    bool CanSubscribe,
    int? FancierId,           // organizer fancier ID
    int Distance,             // km — nominal flight distance for classification
    int Subscribers,
    int[]? Invitations);

public sealed record FlightLocationDto(
    int Id,
    string Name,
    double Lat,
    double Lng);

// Deserialized from /api/flight/{id}/results → items[]
public sealed record FlightResultDto(
    int Id,
    int Position,
    int Points,               // 0 for training; >0 for competitive
    decimal AverageSpeed,     // primary performance metric (~1000–1500 range, likely m/min)
    int AgeType,              // numeric (4 = elder)
    int AgePosition,
    decimal CurrentSpeed,     // 0 when flight is over
    int Distance,             // pigeon's home-to-release distance (NOT flight distance!)
    int RemainingDistance,    // 0 when completed
    int Direction,            // 100 = completed
    int Progress,             // 100 = completed
    int PigeonId,
    int? FirstNameId,
    int? LastNameId,
    int FancierId,
    string? Fancier);

// Paginated wrapper for /api/flight/{id}/results
public sealed record FlightResultsResponse(
    FlightResultDto[] Items,
    int Count,
    int Page);

// Deserialized from /api/pigeon/{id}/results — flat array, NOT paginated
// WARNING: This endpoint is unreliable for actual results:
//   - nested flight.status is stale (shows "notStarted" even for ended flights)
//   - no averageSpeed field
//   - no location or distance on nested flight
//   - pigeonId is always 0
// Use ONLY to discover which flight IDs a pigeon participated in,
// then fetch actual results from /api/flight/{id}/results
public sealed record PigeonResultDto(
    int Id,
    int Position,
    int Points,
    int AgeType,              // numeric (4 = elder)
    int? AgePosition,
    int Direction,
    decimal Progress,
    int PigeonId,             // always 0 (implicit from query path)
    PigeonResultFlightDto Flight);

public sealed record PigeonResultFlightDto(
    int Id,
    string AgeType,           // "elder" | "young"
    DateTime Start,
    PigeonResultLocationDto? Location,  // nullable — not always present
    bool Public,
    int Season,
    string Type,              // WARNING: may show "regional" when actual is "training"
    string Status,            // WARNING: stale — always shows "notStarted"
    bool CanSubscribe,
    int Subscribers);

public sealed record PigeonResultLocationDto(
    int Id,
    string Name,
    double Lat,
    double Lng);
```

#### 3.2 Reader interface

```csharp
public sealed record FlightResultListItem(
    int FlightId,
    DateTime FlightDate,
    string FlightType,          // regional / national (training excluded)
    string? Location,
    int FlightDistanceKm,       // nominal flight distance
    DistanceCategory Category,
    int Position,
    int TotalParticipants,
    double Percentile,          // position / participants * 100 (lower = better)
    int Points,
    decimal AverageSpeed,       // primary performance metric
    string PigeonName,
    int PigeonId);

public sealed record PigeonDistanceProfile(
    int PigeonId,
    string PigeonName,
    // per-category aggregates
    int ShortRaces,
    double ShortAvgPosition,
    double ShortAvgPercentile,   // 0–100; position / participants * 100
    int ShortBestPosition,
    int ShortTotalPoints,
    int MiddleRaces,
    double MiddleAvgPosition,
    double MiddleAvgPercentile,
    int MiddleBestPosition,
    int MiddleTotalPoints,
    int LongRaces,
    double LongAvgPosition,
    double LongAvgPercentile,
    int LongBestPosition,
    int LongTotalPoints,
    DistanceCategory BestCategory);  // which category has the best avg percentile

public sealed record FlightResultsPageData(
    FlightResultListItem[] RecentResults,
    PigeonDistanceProfile[] PigeonProfiles);

public interface IFlightResultsReader
{
    Task<FlightResultsPageData> GetFlightResultsAsync(int fancierId);
    Task<FlightResultListItem[]> GetResultsForPigeonAsync(int fancierId, int pigeonId);
}
```

---

### Phase 2 — Persistence

#### 3.3 New EF Core entities (in `Persistence/Entities.cs`)

```csharp
public sealed class FlightEntity
{
    public int Id { get; set; }             // PK = source flight ID
    public int Season { get; set; }
    public int Department { get; set; }
    public string Type { get; set; }        // regional | national (training excluded)
    public string PayoutType { get; set; }  // none | (TBD)
    public string Status { get; set; }      // notStarted | started | ended
    public DateTime Start { get; set; }
    public string? LocationName { get; set; }
    public double? LocationLat { get; set; }
    public double? LocationLng { get; set; }
    public int DistanceKm { get; set; }     // nominal flight distance
    public string DistanceCategory { get; set; }  // Short | Middle | Long
    public string AgeType { get; set; }     // elder | young
    public decimal EntryPrice { get; set; }
    public int Subscribers { get; set; }
    public DateTime DetectedAtUtc { get; set; }
    public DateTime? ResultsFetchedAtUtc { get; set; }
}

public sealed class FlightResultEntity
{
    public int Id { get; set; }             // auto PK
    public int FlightId { get; set; }       // FK → FlightEntity
    public int PigeonId { get; set; }
    public int FancierId { get; set; }
    public int Position { get; set; }       // 1 = first place
    public int TotalParticipants { get; set; }  // from flight.Subscribers
    public int Points { get; set; }
    public decimal AverageSpeed { get; set; }   // KEY metric for performance
    public int PigeonDistance { get; set; }      // pigeon's home-to-release distance
    public string? PigeonName { get; set; }
    public DateTime DetectedAtUtc { get; set; }
}
```

#### 3.4 DbContext additions

- Add `DbSet<FlightEntity> Flights` and `DbSet<FlightResultEntity> FlightResults` to `AppDbContext`.
- Add an EF migration or `EnsureCreated` schema update.
- Add a composite unique index on `FlightResultEntity(FlightId, PigeonId)` for deduplication.

---

### Phase 3 — Sync integration

#### 3.5 Expand the sync endpoint catalog

The `/api/flight` list endpoint requires multi-value query parameters (e.g. `status=notStarted&status=started&status=ended`) which the current `IReadOnlyDictionary<string, string?>` transport API cannot express. Two options:

**Option A (recommended):** Use `/api/pigeon/{id}/results` per-pigeon to discover flight IDs, then fetch `/api/flight/{id}` and `/api/flight/{id}/results` for each discovered flight.

**Option B:** Extend the transport to support multi-value query parameters (e.g. `IReadOnlyDictionary<string, string[]?>`).

For MVP, use Option A — the ingestion flow becomes:

| Step | Endpoint | Purpose |
|---|---|---|
| 1 | `/api/pigeon/{id}/results` (for each pigeon) | Discover flight IDs this pigeon participated in |
| 2 | `/api/flight/{id}` (for each unique flight ID) | Get flight metadata: distance, type, status, location |
| 3 | `/api/flight/{id}/results` (for ended flights only) | Get full ranked results with averageSpeed |

#### 3.6 Flight result ingestion flow

```
FlightResultIngester (post-sync or on-demand)
  ├─ for each pigeon in roster:
  │    └─ fetch /api/pigeon/{id}/results → extract flight IDs
  ├─ deduplicate flight IDs against FlightEntity table
  ├─ for each new/unknown flight ID:
  │    ├─ fetch /api/flight/{id} → get metadata (distance, type, status, location)
  │    ├─ SKIP if type == "training" (only track regional + national)
  │    └─ insert FlightEntity (with DistanceCategory from flight.Distance)
  ├─ for each FlightEntity with status="ended" AND ResultsFetchedAtUtc=null:
  │    ├─ fetch /api/flight/{id}/results → get paginated results
  │    ├─ filter to results where FancierId matches user's fancier(s)
  │    └─ upsert FlightResultEntity (deduplicate by FlightId + PigeonId)
  └─ mark FlightEntity.ResultsFetchedAtUtc
```

**Key design decisions:**
- Store only **regional and national** flight metadata (skip training flights entirely)
- Only fetch detailed results for ended flights (progress=100)
- Store only the user's own pigeons' results in `FlightResultEntity` (not all 100 participants) to keep the database small
- Keep the full flight results JSON in `RawApiSnapshotEntity` for later analysis if needed
- `averageSpeed` is the key performance metric for ranking/comparison

---

### Phase 4 — Analytics

#### 3.7 Distance profile calculator (in `Core/Analytics/`)

New class: `DistanceProfileCalculator`

```csharp
public static class DistanceProfileCalculator
{
    public static PigeonDistanceProfile Calculate(
        int pigeonId,
        string pigeonName,
        IReadOnlyList<FlightResultListItem> results)
    {
        // Group results by DistanceCategory
        // For each group: count races, avg position, avg percentile,
        //   best (lowest) position, total points
        // Determine BestCategory = category with lowest avg percentile
        //   (lower percentile = better, e.g. top 10%)
    }
}
```

#### 3.8 Enhance existing analytics

- **`StatsComparer`**: Add an overload that compares actual flight percentiles (not just skill-derived scores) against the flock or market population.
- **`WeeklyGrowthCalculator`**: Can be reused to track points earned per week.
- **Dashboard `PigeonListItem`**: Add fields for actual race stats alongside the existing skill-derived distance scores:
  - `ShortRaces`, `ShortAvgPercentile`
  - `MiddleRaces`, `MiddleAvgPercentile`
  - `LongRaces`, `LongAvgPercentile`
  - `BestCategory` (Short / Middle / Long)

---

### Phase 5 — UI

#### 3.9 New Flight Results view

Add a fifth tab to `MainWindow.xaml`: **Flights**.

**FlightResultsView.xaml** layout:

```
┌─────────────────────────────────────────────────────────────┐
│  PIGEON DISTANCE PROFILES                                   │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ DataGrid: one row per pigeon                           │ │
│  │ Columns:                                               │ │
│  │  Name | Best ★ | Short (races, avg%, best, pts) |      │ │
│  │              Middle (races, avg%, best, pts) |          │ │
│  │              Long (races, avg%, best, pts) | Total Pts  │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                             │
│  RECENT FLIGHT RESULTS                                      │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ DataGrid: one row per result                           │ │
│  │ Columns:                                               │ │
│  │  Date | Type | Location | Distance | Category |        │ │
│  │  Pigeon | Position | Points | Speed                    │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                             │
│  [Filter: pigeon ▾] [Filter: category ▾] [Filter: type ▾]  │
└─────────────────────────────────────────────────────────────┘
```

**Key UX elements:**
- The **Best ★** column highlights whether a pigeon is best at Short, Middle, or Long with a colored badge.
- Distance profile rows are sortable by any column so the user can rank pigeons by their short-race average percentile, etc.
- Clicking a pigeon in the top grid filters the bottom grid to that pigeon's individual results.
- Category filter dropdown to show only Short / Middle / Long results.

#### 3.10 Dashboard enhancements

Add a "Best at" badge to each pigeon row in the existing dashboard `DataGrid`, showing the pigeon's strongest distance category based on actual race results (falls back to skill-derived if no results yet).

---

### Phase 6 — Data export/import

#### 3.11 Extend backup format

- Add `FlightEntity` and `FlightResultEntity` collections to the `.pfbackup` ZIP export.
- Add corresponding import logic with deduplication by source flight ID and `(FlightId, PigeonId)` composite key.
- Increment the backup schema version.

---

## 4. File change summary

| File | Change |
|---|---|
| `Core/Domain/DistanceCategory.cs` | **New** — enum |
| `Core/Domain/FlightType.cs` | **New** — enum |
| `Core/Domain/FlightStatus.cs` | **New** — enum |
| `Core/Contracts/FlightContracts.cs` | **New** — DTOs, reader interface, view models |
| `Core/Analytics/DistanceProfileCalculator.cs` | **New** — per-pigeon distance analysis |
| `Core/Contracts/TrackerDataContracts.cs` | **Edit** — add race-based fields to `PigeonListItem` |
| `Infrastructure/Persistence/Entities.cs` | **Edit** — add `FlightEntity`, `FlightResultEntity` |
| `Infrastructure/Persistence/AppDbContext.cs` | **Edit** — add DbSets |
| `Infrastructure/Persistence/FlightResultIngester.cs` | **New** — post-sync ingestion service |
| `Infrastructure/Persistence/FlightResultsReader.cs` | **New** — implements `IFlightResultsReader` |
| `Infrastructure/Persistence/TrackerDataReader.cs` | **Edit** — enrich dashboard with race stats |
| `Infrastructure/Persistence/DataExporter.cs` | **Edit** — include flights in backup |
| `Infrastructure/Persistence/DataImporter.cs` | **Edit** — import flights from backup |
| `Infrastructure/Sync/SyncEndpointCatalog.cs` | **Edit** — add flight result endpoints to Standard profile |
| `Infrastructure/DependencyInjection.cs` | **Edit** — register new services |
| `App/MainWindow.xaml` | **Edit** — add Flights tab |
| `App/FlightResultsView.xaml` | **New** — flight results UI |
| `App/FlightResultsView.xaml.cs` | **New** — code-behind |

---

## 5. Test plan

| Test file | Coverage |
|---|---|
| `Core.Tests/DistanceProfileCalculatorTests.cs` | Category classification at boundary distances; avg percentile calculation; BestCategory selection with ties; empty results |
| `Infrastructure.Tests/FlightResultIngesterTests.cs` | Deduplication on re-sync; partial result pages; flights with no results |
| `Infrastructure.Tests/FlightResultsReaderTests.cs` | Profile aggregation across categories; filtering by pigeon; ordering |
| `Core.Tests/ApiContractDeserializationTests.cs` | **Edit** — add flight and result DTO deserialization fixtures |

---

## 6. Unknowns & risks

| Item | Status | Mitigation |
|---|---|---|
| `/api/pigeon/{id}/results` shape | **Resolved** — flat array with nested `flight` object; data is stale/unreliable | Use ONLY for flight ID discovery; fetch real data from `/api/flight/{id}` and `/api/flight/{id}/results` |
| `/api/flight/{id}/results` shape | **Resolved** — paginated `{items, count, page}` with `averageSpeed`, `position`, `points`, per-pigeon `distance` | Confirmed working; all 100 results returned on one page |
| `/api/flight/{id}` shape | **Resolved** — has `distance`, `location`, `type`, `status`, `payoutType`, `subscribers` | Confirmed; `distance` is the nominal flight distance for classification |
| `/api/flight` list endpoint | Returns 400 — requires multi-value query params (`status=x&status=y`) | Use pigeon-results-driven discovery (Option A) instead of flight list |
| Distance field semantics | **Resolved** — flight `distance` = nominal (111km); result `distance` = pigeon-specific home-to-release (varies 97–148km) | Use flight-level `distance` for Short/Middle/Long classification |
| Distance thresholds for Short/Middle/Long | Using standard racing bands (300/500 km); game distances observed so far are 111km and 251km (both "Short") | Make configurable; need competitive (national) flights to observe longer distances |
| Points system | **Partially resolved** — training flights have `points: 0`; competitive flights presumably award points | Wait for national/regional competitive flights; use `averageSpeed` and `position` as primary metrics meanwhile. Training flights are excluded entirely |
| `averageSpeed` units | Values ~1000–1500 observed; likely meters per minute (m/min) | Display raw value with "m/min" label; adjust if wrong |
| `ageType` numeric values | Result-level: 4 = elder (matches flight `ageType: "elder"`) | Map: 4→elder; observe young flights later |
| Flights across multiple seasons | Only season 1 observed (started 2026-07-27, 9 departments) | Store season on `FlightEntity`; filter by active season in UI |
| Multi-value query params for `/api/flight` | Transport API uses `Dictionary<string, string?>` — can't express repeated keys | Phase 2: extend transport; Phase 1: use pigeon-driven discovery |
| Pigeon results for all pigeons | 6 of 8 pigeons had empty results; only pigeon 59 had a flight entry | Game is early in season 1; more results will accumulate as flights complete |

---

## 7. Suggested implementation order

1. **Domain enums + contracts** — no dependencies, enables parallel work
2. **EF Core entities + migration** — schema must exist before ingestion
3. **Flight result ingester** — the data pipeline
4. **Sync catalog expansion** — connects the pipeline to live data
5. **`FlightResultsReader`** — query layer for the UI
6. **`DistanceProfileCalculator`** — analytics on top of persisted results
7. **`FlightResultsView` UI** — the user-facing feature
8. **Dashboard enhancements** — "Best at" badges
9. **Export/import updates** — backup compatibility
10. **Tests** — can be written alongside each step
