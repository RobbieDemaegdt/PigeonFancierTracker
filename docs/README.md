# Pigeon Fancier Tracker

A standalone companion application for collecting and reviewing Pigeon Fancier data locally.

The application has two modes: a **WPF desktop app** for interactive use on Windows, and a **headless worker service** that runs autonomously in Docker as its own pigeon fancier. Both share the same Core and Infrastructure libraries, with SQLite persistence through Entity Framework Core.

> **Current status:** the project is under active development. The foundation, WebView2 authentication flow, raw response capture, manual Quick synchronization, retry handling, sync-run auditing, dashboard summary with pigeon grid, pigeon history, transfer market view, data export/import, data reset, completed transfer tracking, and price estimation analytics are implemented. Scheduled synchronization, normalized domain tables, dashboard charts, and the installer described in the implementation plan are still being built.

## Safety boundaries

The tracker is intentionally read-only after login:

- Login credentials are entered directly into the official Pigeon Fancier page hosted in WebView2.
- The application does not inspect or store the password.
- The collector only allows documented `GET` API paths.
- `POST`, `PUT`, `PATCH`, and `DELETE` game requests are rejected by the collector.
- Cookies remain inside the persistent WebView2 profile and are not written to application logs or configuration files.
- The application does not bid, buy, sell, breed, train, clean, upgrade, subscribe, invite, or change account settings.
- Raw responses are stored locally in SQLite for historical and forward-compatibility purposes.

Use the application only with an account and data you are authorized to access.

## Requirements

### Runtime requirements

- Windows 10 or Windows 11
- Microsoft .NET 10 Desktop Runtime, or the .NET 10 SDK for development
- Microsoft Edge WebView2 Runtime
- An internet connection for login and synchronization
- A Pigeon Fancier account

The WebView2 Runtime is normally already installed on current Windows systems. If the connection view cannot initialize, install or repair the Evergreen WebView2 Runtime from Microsoft before trying again.

### Development requirements

- .NET 10 SDK
- Visual Studio Code, Visual Studio, or another editor with C# support
- Git is recommended, but the project does not currently require a Git repository to run

Verify the SDK from a terminal in the project directory:

```text
dotnet --info
```

The solution targets `net10.0` for the Core and Infrastructure libraries and `net10.0-windows` for the WPF application.

## Project structure

```text
PigeonFancierTracker.sln
docker-compose.yml
src/
  PigeonFancierTracker.App/          # WPF desktop application (Windows)
  PigeonFancierTracker.Worker/       # Headless worker service (cross-platform / Docker)
  PigeonFancierTracker.Core/         # Shared contracts, domain, analytics
  PigeonFancierTracker.Infrastructure/ # Shared persistence, API, sync, auth
tests/
  PigeonFancierTracker.Core.Tests/
  PigeonFancierTracker.Infrastructure.Tests/
  PigeonFancierTracker.App.Tests/
tools/
  FlightDiscovery/                   # API endpoint exploration tool
docs/
```

### Responsibilities

| Project | Responsibility |
| --- | --- |
| `PigeonFancierTracker.App` | WPF shell, dashboard with pigeon grid, connection window, pigeon history, transfer market, data management (export/import/reset), WebView2 host, user actions |
| `PigeonFancierTracker.Worker` | Headless `BackgroundService` that runs autonomously in Docker: login, periodic sync, flight ingestion, auto-bid |
| `PigeonFancierTracker.Core` | Domain contracts, session states, sync contracts, API response contracts, transfer data contracts, price estimation contracts, data port contracts, analytics (weekly growth, price estimation) |
| `PigeonFancierTracker.Infrastructure` | API client, API allowlist, synchronization, SQLite, EF Core, raw capture, pigeon history reader, tracker data reader, transfer data reader, auto-bid service, data export/import, data reset |
| `PigeonFancierTracker.Core.Tests` | Weekly growth, price estimation, and API contract deserialization tests |
| `PigeonFancierTracker.Infrastructure.Tests` | Allowlist, session, persistence, retry, synchronization, tracker data reader, data export, and data import tests |

## Build the application

Open a terminal in the repository root, the directory containing `PigeonFancierTracker.sln`, and run:

```text
dotnet restore PigeonFancierTracker.sln
dotnet build PigeonFancierTracker.sln
```

A successful build produces the application under:

```text
src/PigeonFancierTracker.App/bin/Debug/net10.0-windows/
```

The build currently reports two known warnings:

1. A transitive vulnerability warning for `SQLitePCLRaw.lib.e_sqlite3`.
2. A WebView2 `WindowsBase` assembly reference conflict caused by the current WebView2 package targeting `net5.0-windows` while the application targets .NET 10 WPF.

These warnings do not currently prevent the application or tests from building, but the SQLite warning must be reviewed before production packaging.

## Run the application

From the repository root:

```text
dotnet run --project src/PigeonFancierTracker.App/PigeonFancierTracker.App.csproj
```

You can also run the built executable directly from the `bin/Debug/net10.0-windows` directory.

### First launch

1. Start the application.
2. The **Dashboard** opens and shows the current connection state and next action.
3. Open **Connection** from the left navigation. The official Pigeon Fancier login page is loaded in WebView2.
4. Enter your credentials directly into that page. The tracker does not read the password field.
5. Complete the official login flow.
6. Click **Open fancier selection** if the lobby does not open automatically.
7. Select the fancier account manually in the official site.
8. Return to the tracker and wait for the connection banner to become **Ready to sync**.
9. Click **Sync now** on the dashboard to run the read-only Quick profile.

The tracker confirms authentication with `GET /api/user` and confirms the selected context with `GET /api/fancier/selected`.

### Subsequent launches

The WebView2 profile is persistent. If the Pigeon Fancier session is still valid:

1. Start the application.
2. The existing WebView2 profile is opened.
3. The tracker checks `/api/user` and `/api/fancier/selected`.
4. If a fancier is selected, the dashboard becomes ready for synchronization.

If the session expired, the dashboard shows **Session expired** and provides a **Sign in again** action that opens the Connection page.

### Connection actions

The Connection page provides a three-step guide and contextual actions:

- **Open fancier selection** — navigate to the official lobby for manual fancier selection.
- **Sign in again** — return to the official login page when the session is expired or logged out.
- **Check connection** — immediately re-check the authenticated session instead of waiting for the background check.
- **Sign out and clear saved session** — clear WebView2 browsing data after confirmation. This does not delete the local SQLite database.

The Dashboard distinguishes a network sync from **Refresh local data**. During synchronization it shows determinate endpoint progress and a final success, partial-success, cancelled, expired, or failed summary.

## Headless worker (Docker)

The headless worker runs as an autonomous pigeon fancier — its own account operating 24/7. It performs login, periodic data synchronization, flight result ingestion, and autonomous management of food, training, flights, breeding, and loft maintenance. It defaults to **dry-run (advisor) mode**, where it analyzes the game state and reports what it would do without taking any actions.

### Requirements

- Docker and Docker Compose (Linux containers)
- A dedicated Pigeon Fancier account for the worker
- The fancier ID for the account

### Quick start with Docker Compose

1. Create a `.env` file in the repository root:

```text
PF_EMAIL=your-worker-account@example.com
PF_PASSWORD=your-password
PF_FANCIER_ID=7
PF_SYNC_INTERVAL=30
PF_AUTOBID=false
```

2. Start the worker:

```text
docker compose up -d
```

3. Check the logs:

```text
docker compose logs -f worker
```

The worker will log in, select the fancier, run an initial sync with flight ingestion, and then repeat on the configured interval.

### Running without Docker

The worker can also run directly on any platform with the .NET 10 runtime:

```text
dotnet run --project src/PigeonFancierTracker.Worker -- --Worker:Email=your@email.com --Worker:Password=yourpass --Worker:FancierId=7
```

Or with environment variables:

```text
PF_EMAIL=your@email.com PF_PASSWORD=yourpass dotnet run --project src/PigeonFancierTracker.Worker -- --Worker:FancierId=7
```

### Configuration

All configuration is provided via the `Worker` section in `appsettings.json` or as environment variables. In Docker, use the `Worker__Key` naming convention for environment variables.

| Setting | Environment variable | Default | Description |
| --- | --- | --- | --- |
| `Worker:Email` | `Worker__Email` | | Login email for the worker's Pigeon Fancier account |
| `Worker:Password` | `Worker__Password` | | Login password |
| `Worker:FancierId` | `Worker__FancierId` | `0` | The fancier ID to select after login |
| `Worker:SyncIntervalMinutes` | `Worker__SyncIntervalMinutes` | `30` | Minutes between sync cycles |
| `Worker:AutoBidEnabled` | `Worker__AutoBidEnabled` | `false` | Enable the auto-bid polling loop |
| `Worker:AutoBidRules` | `Worker__AutoBidRules__0__*` | `[]` | List of auto-bid rules (see below) |
| `Worker:ManagementEnabled` | `Worker__ManagementEnabled` | `true` | Enable autonomous fancier management |
| `Worker:AutoFlightEnabled` | `Worker__AutoFlightEnabled` | `true` | Auto-enroll pigeons in flights (requires management) |
| `Worker:AutoFeedEnabled` | `Worker__AutoFeedEnabled` | `true` | Auto-purchase food and adjust distribution (requires management) |
| `Worker:MinFoodDaysReserve` | `Worker__MinFoodDaysReserve` | `7` | Minimum food days before purchasing more |
| `Worker:AutoFinanceGuardEnabled` | `Worker__AutoFinanceGuardEnabled` | `true` | Check balance before spending (requires management) |
| `Worker:MinBalanceAlert` | `Worker__MinBalanceAlert` | `1000` | Balance threshold that blocks spending |
| `Worker:AutoTrainEnabled` | `Worker__AutoTrainEnabled` | `true` | Auto-set training focus (requires management) |
| `Worker:AutoLoftEnabled` | `Worker__AutoLoftEnabled` | `true` | Auto-clean loft and buy pens (requires management) |
| `Worker:AutoBreedEnabled` | `Worker__AutoBreedEnabled` | `false` | Auto-manage breeding pairs (requires management) |
| `Worker:DryRun` | `Worker__DryRun` | `true` | Log intended actions without executing them (advisor mode) |

### Auto-bid rules

Auto-bid rules are configured as a JSON array in `appsettings.json`:

```json
{
  "Worker": {
    "AutoBidEnabled": true,
    "AutoBidRules": [
      { "TransferId": 123, "PigeonName": "Storm", "MaxPrice": 500 },
      { "TransferId": 456, "PigeonName": "Bliksem", "MaxPrice": 300 }
    ]
  }
}
```

When auto-bid is enabled, the worker polls active transfers every 20-45 seconds and places bids at 110% of the current price, up to the configured maximum.

### Worker lifecycle

The worker follows this lifecycle on each iteration:

1. **Authenticate** — log in with the configured credentials, select the fancier, and validate the session.
2. **Sync** — run a Quick sync profile (13 API endpoints with retry and concurrency).
3. **Ingest flights** — discover and fetch flight results for all pigeons.
4. **Manage fancier** — if `ManagementEnabled`, run autonomous management (flight enrollment, etc.). Uses the game rules documented in [GAME_GUIDE.md](GAME_GUIDE.md) for all decisions.
5. **Auto-bid** — if enabled, poll transfers and place bids autonomously.
6. **Wait** — sleep for the configured interval, then repeat from step 1.

### Dry run (advisor mode)

The worker defaults to `DryRun: true`. In this mode it authenticates, syncs data, and builds management plans for every enabled subsystem, but **does not execute any write actions**. Instead it produces an advisor report — a formatted summary of everything it *would* do — so you can review the suggestions and perform them manually on the website.

This is the recommended way to start using the worker: observe what it recommends before letting it act autonomously.

#### Quick start

1. Create a `.env` file (or set environment variables) with your credentials:

```text
PF_EMAIL=your@email.com
PF_PASSWORD=your-password
PF_FANCIER_ID=7
```

2. Run the worker. No other configuration is needed — `DryRun` and `ManagementEnabled` are both `true` by default:

```text
dotnet run --project src/PigeonFancierTracker.Worker -- --Worker:FancierId=7
```

Or with Docker:

```text
docker compose up -d
docker compose logs -f worker
```

3. After each sync cycle, the worker prints an advisor report to the console:

```text
=======================================================
  ADVISOR REPORT — 2026-08-08 09:30 UTC
=======================================================

  FINANCE
     Balance: 1234.56 (delta: -45.00)
     Alert: None — spending allowed

  FOOD (3.2 days remaining)
     -> Buy 5x Grain @ 2.00 = 10.00
     -> Set distribution: B:25% G:30% C:25% P:20%

  TRAINING
     Current: General
     -> Set focus to Conditional (score=0.85)
        Reason: Weak conditional skills detected

  FLIGHTS
     -> Enroll "Speedy" in Sprint #42 (Brussels, 150km) score=8.5
     -> Enroll "Thunder" in Middle #43 (Liege, 300km) score=7.2

  LOFT (capacity: 18/20, 90% full, dirt: 45)
     -> Clean loft (dirt=45): Dirt exceeds threshold
     No pen purchase needed

  BREEDING (4 couples, 2 slots)
     No actions needed

=======================================================
```

4. A JSON file with the full report data is saved after each cycle to:

```text
%LOCALAPPDATA%\PigeonFancierTracker\advisor-reports\report-{timestamp}.json
```

In Docker, the reports are at `/data/advisor-reports/` inside the container. Copy them to the host with:

```text
docker compose cp worker:/data/advisor-reports/ ./advisor-reports/
```

5. Read the report and perform the suggested actions manually on the Pigeon Fancier website.

#### Enabling and disabling subsystems

Each management subsystem can be toggled independently. In dry-run mode, disabled subsystems are simply omitted from the report. To focus the report on specific areas, disable the ones you do not need:

```json
{
  "Worker": {
    "DryRun": true,
    "ManagementEnabled": true,
    "AutoFlightEnabled": true,
    "AutoFeedEnabled": true,
    "AutoTrainEnabled": true,
    "AutoLoftEnabled": true,
    "AutoBreedEnabled": false,
    "AutoFinanceGuardEnabled": true
  }
}
```

Or via command-line overrides:

```text
dotnet run --project src/PigeonFancierTracker.Worker -- --Worker:FancierId=7 --Worker:AutoBreedEnabled=true --Worker:AutoLoftEnabled=false
```

#### Transitioning to autonomous mode

Once you are confident that the worker's suggestions match what you would do manually, switch `DryRun` to `false` to let it execute actions:

```text
dotnet run --project src/PigeonFancierTracker.Worker -- --Worker:FancierId=7 --Worker:DryRun=false
```

You can transition gradually by enabling one subsystem at a time while keeping others in observation. For example, let it manage training autonomously while you continue handling flights manually:

```json
{
  "Worker": {
    "DryRun": false,
    "ManagementEnabled": true,
    "AutoTrainEnabled": true,
    "AutoFlightEnabled": false,
    "AutoFeedEnabled": false,
    "AutoLoftEnabled": false,
    "AutoBreedEnabled": false
  }
}
```

#### What is safe in dry-run mode

- **Authentication and sync always run** — these are read-only (GET requests) and needed so the Build phases have fresh data to analyze.
- **No write API calls** — all `Execute*` methods are skipped. The worker never sends POST, PUT, or PATCH requests.
- **Auto-bid stays disabled** — `AutoBidEnabled` defaults to `false` and is independent of the DryRun flag.
- **The advisor report is the only output** — a formatted console log and a JSON file.

### Session recovery

The worker automatically handles session expiry:

- If the session expires during a sync, it re-authenticates and retries.
- If the auto-bid service detects a 401, it stops and the worker re-authenticates before the next cycle.
- Authentication retries up to 5 times with exponential backoff.

### Data persistence

The worker stores its SQLite database at `/data/tracker.db` inside the container. The `docker-compose.yml` maps this to a named volume (`worker-data`) so data persists across container restarts.

To access the database from the host:

```text
docker compose cp worker:/data/tracker.db ./tracker-backup.db
```

### Adding new behaviors

The worker is designed to be extended with new autonomous capabilities. To add a new behavior:

1. Create a new service in `PigeonFancierTracker.Infrastructure` (following the `AutoBidService` pattern).
2. Add any new GET paths to `PigeonFancierApiAllowlist`.
3. Add any new POST paths to `HttpClientWriteTransport`.
4. Add configuration to `WorkerOptions`.
5. Start/stop the behavior from `FancierWorkerService`.

## Synchronization

### Quick profile

The dashboard's **Sync now** button currently runs the Quick profile. It reads and stores responses from the following endpoint families:

- user and season context;
- selected fancier and fancier details;
- fancier pigeons and pigeon data;
- couples and groups;
- inventory/items;
- reports;
- active transfers.

Each endpoint is captured as a raw snapshot immediately after the response is received.

### Pigeon history

After a successful Quick sync, click **Pigeon history** on the dashboard to open the first detailed data page. It shows:

- every locally observed `/api/pigeon` snapshot for a selected pigeon;
- total skill and individual skill history;
- premium/value history, observation timestamps, and source snapshot IDs;
- first-to-current total-skill change and the number of observations.

The history page reads SQLite only and does not make a new request to the game. Missing values remain blank. Energy, training, flight results, pedigree, offspring, transfers, finance, and reports will be added in later phases.

### Transfer market

The **Transfers** page shows active and completed transfers from the latest local snapshots. It reads SQLite only and does not make a new request to the game. Completed transfers are persisted in a dedicated `CompletedTransfers` table with skill snapshots, sale prices, bid counts, and buyer/seller details.

### Price estimation

The price estimator uses completed transfer history to estimate a pigeon's market value based on skill similarity. It compares the target pigeon's attributes against historical sales, weighted by similarity, and returns an estimated price with a confidence level (high, medium, or low) based on the number of comparable sales.

### Standard profile

The Standard profile is implemented in the synchronization catalog for programmatic use and includes the Quick endpoints plus:

- weather;
- processed transfers;
- selected-fancier transfer views;
- ranking;
- flight and live-flight endpoints.

A dedicated Standard profile control will be added to the UI in a later phase.

### Retry and cancellation behavior

The coordinator:

- runs no more than four GET requests concurrently;
- applies a 15-second timeout per request;
- retries network failures and HTTP `408`, `429`, `502`, `503`, and `504` responses;
- honors a numeric `Retry-After` response header;
- uses bounded exponential backoff with jitter;
- does not retry `400`, `401`, `403`, or `404` responses;
- stops remaining work after a `401` and changes the session to **Session expired**;
- records partial success instead of discarding successful endpoint results;
- prevents overlapping synchronization runs;
- supports cancellation with the **Cancel** button.

## Local data and privacy

The application stores data under:

```text
%LOCALAPPDATA%\PigeonFancierTracker\
```

Important locations:

| Location | Purpose |
| --- | --- |
| `tracker.db` | SQLite database containing raw API snapshots and sync audit records |
| `WebView2\` | Persistent WebView2 user-data profile containing the browser session |

The database currently contains:

- `RawApiSnapshots` — endpoint, normalized query, status, timestamp, content type, body, SHA-256 hash, context IDs, and error details;
- `SyncRuns` — one record for each synchronization run;
- `SyncRunItems` — one record for each endpoint attempt within a run;
- `CompletedTransfers` — transfer ID, pigeon details, start/sold prices, bid count, buyer/seller, skill snapshot, and detection timestamp;
- EF Core migration metadata.

Do not copy the WebView2 profile or database to an untrusted location. The WebView2 profile can contain an authenticated browser session.

### Sign out and delete data

- Use **Clear session** to remove the saved browser session while retaining collected data.
- Use **Alles wissen** (Reset all) on the **Gegevens** (Data) page to permanently delete all collected data, including raw snapshots, sync runs, completed transfers, and saved credentials. This cannot be undone.
- To delete collected data manually, close the application and remove `tracker.db` from `%LOCALAPPDATA%\PigeonFancierTracker\`. The database will be recreated on the next launch.
- To remove the saved browser session manually, close the application and remove the `WebView2` directory. The official login will be required on the next launch.

Only delete files manually when the application is not running.

### Data export and import

The **Gegevens** (Data) page provides export and import:

- **Export** saves all local data (raw snapshots, sync runs, and completed transfers) to a `.pfbackup` file. The archive is a ZIP file containing JSON files. It does not contain login credentials or passwords.
- **Import** reads a `.pfbackup` file and adds new records. Existing records are skipped — the import is additive only.

The `.pfbackup` file can be safely copied via USB, email, or cloud storage to transfer data between devices.

## Run tests

Run all tests from the repository root:

```text
dotnet test PigeonFancierTracker.sln
```

The test suite currently covers:

- weekly growth calculations;
- price estimation with similarity scoring and confidence levels;
- API contract deserialization;
- API GET allowlist behavior;
- session expiry and selected-fancier state transitions;
- raw response and sync audit persistence;
- transient retry behavior;
- no-retry behavior for `401`;
- tracker data reader queries;
- data export to `.pfbackup` archives;
- data import from `.pfbackup` archives;
- migration-backed database setup used by the application.

## Database migrations

Normal users do not need to run EF commands. The application applies pending migrations during startup.

For developers, the existing migrations are under:

```text
src/PigeonFancierTracker.Infrastructure/Persistence/Migrations/
```

After changing the persistence model, generate a migration from the repository root:

```text
dotnet ef migrations add MigrationName --project src/PigeonFancierTracker.Infrastructure/PigeonFancierTracker.Infrastructure.csproj --startup-project src/PigeonFancierTracker.App/PigeonFancierTracker.App.csproj --output-dir Persistence/Migrations
```

Then build and run the application to apply it to the local database.

## Troubleshooting

### The connection view is blank or WebView2 fails to initialize

1. Confirm that Microsoft Edge WebView2 Runtime is installed.
2. Close the tracker.
3. Check that `%LOCALAPPDATA%\PigeonFancierTracker\WebView2` is writable.
4. Remove the `WebView2` directory only if you are comfortable signing in again.
5. Restart the application.

### The tracker says “Waiting for an authenticated login”

The official page has not yet produced an authenticated `GET /api/user` response. Complete the login flow in WebView2, then navigate to the lobby if necessary.

### The tracker says “Select a fancier”

The user session is valid, but the official site has no selected fancier context. Click **Open lobby**, select a fancier manually, and wait for the tracker to confirm `GET /api/fancier/selected`.

### Synchronization is unavailable

A selected fancier and the **Ready** session state are required before synchronization starts. If the session expired, log in again rather than repeatedly retrying synchronization.

### The database fails during startup

1. Close the application.
2. Back up `tracker.db` before making changes.
3. Check whether another tracker process is using the database.
4. Start the application again so EF Core can retry pending migrations.
5. If the database is disposable, remove it and let the application recreate it.

Do not delete the database if historical snapshots need to be preserved.

### A request fails with a non-success status

The sync run records the endpoint failure and continues with other endpoints where possible. Optional flight endpoints may fail without invalidating the whole run. Inspect the sync-run records and raw snapshots locally when diagnosing a response.

## Current limitations

The current implementation does not yet provide:

- scheduled background synchronization;
- normalized pigeon, finance, flight, or breeding domain tables;
- transfer-sale reconciliation;
- dashboard charts and historical trend views;
- JSON and CSV export (the `.pfbackup` archive export is available);
- a configurable settings screen;
- an installer or packaged WebView2 prerequisite;
- a separate Standard profile button;
- a production-grade encrypted settings store;
- automatic deletion or retention policies for raw snapshots.

These items are tracked in [standalone-dotnet-implementation-plan.md](standalone-dotnet-implementation-plan.md).

## Development workflow

A typical development loop is:

```text
dotnet restore PigeonFancierTracker.sln
dotnet build PigeonFancierTracker.sln
dotnet test PigeonFancierTracker.sln
dotnet run --project src/PigeonFancierTracker.App/PigeonFancierTracker.App.csproj
```

To run the headless worker locally during development:

```text
dotnet run --project src/PigeonFancierTracker.Worker -- --Worker:Email=... --Worker:Password=... --Worker:FancierId=7 --Worker:SyncIntervalMinutes=5
```

When changing a protocol path or adding a collector endpoint:

1. Add or update the endpoint in the GET allowlist.
2. Add it to the appropriate sync profile catalog.
3. Preserve raw response capture.
4. Add a fixture or fake-transport test.
5. Confirm that no mutating HTTP method is introduced.
6. Run the complete build and test suite.

## Roadmap

Implementation phases follow [standalone-dotnet-implementation-plan.md](standalone-dotnet-implementation-plan.md):

1. Solution foundation — complete.
2. WebView2 authentication — complete in initial form.
3. Read-only transport and raw capture — complete in initial form.
4. Dashboard, transfer market, data management, and price estimation — complete in initial form.
5. Normalized domain tables and transfer-sale reconciliation — next major phase.
6. Flights and breeding.
7. Scheduling, CSV/JSON export, packaging, and hardening.
