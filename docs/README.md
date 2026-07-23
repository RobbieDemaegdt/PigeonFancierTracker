# Pigeon Fancier Tracker

A Windows-first standalone companion application for collecting and reviewing read-only Pigeon Fancier data locally.

The application uses a WPF desktop shell, an authenticated WebView2 browser profile, SQLite persistence through Entity Framework Core, and a GET-only API client. It is designed to preserve historical observations without performing game mutations.

> **Current status:** the project is under active development. The foundation, WebView2 authentication flow, raw response capture, manual Quick synchronization, retry handling, sync-run auditing, dashboard summary, and the first local pigeon history page are implemented. The broader analytics, normalized domain views, scheduled synchronization, exports, and installer described in the implementation plan are still being built.

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
README.md
standalone-dotnet-implementation-plan.md
src/
  PigeonFancierTracker.App/
    App.xaml
    MainWindow.xaml
    ConnectionView.xaml
  PigeonFancierTracker.Core/
    Contracts/
    Domain/
    Analytics/
  PigeonFancierTracker.Infrastructure/
    Persistence/
    PigeonFancierApi/
    Sync/
    WebView2/
tests/
  PigeonFancierTracker.Core.Tests/
  PigeonFancierTracker.Infrastructure.Tests/
fixtures/
docs/
```

### Responsibilities

| Project | Responsibility |
| --- | --- |
| `PigeonFancierTracker.App` | WPF shell, dashboard, connection window, WebView2 host, user actions |
| `PigeonFancierTracker.Core` | Domain contracts, session states, sync contracts, analytics primitives |
| `PigeonFancierTracker.Infrastructure` | WebView2 transport, API allowlist, synchronization, SQLite, EF Core, raw capture |
| `PigeonFancierTracker.Core.Tests` | Pure domain and analytics tests |
| `PigeonFancierTracker.Infrastructure.Tests` | Allowlist, session, persistence, retry, and synchronization tests |

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
- EF Core migration metadata.

Do not copy the WebView2 profile or database to an untrusted location. The WebView2 profile can contain an authenticated browser session.

### Sign out and delete data

- Use **Clear session** to remove the saved browser session while retaining collected data.
- The current UI does not yet provide a **Delete all collected data** command.
- To delete collected data manually, close the application and remove `tracker.db` from `%LOCALAPPDATA%\PigeonFancierTracker\`. The database will be recreated on the next launch.
- To remove the saved browser session manually, close the application and remove the `WebView2` directory. The official login will be required on the next launch.

Only delete these files when the application is not running.

## Run tests

Run all tests from the repository root:

```text
dotnet test PigeonFancierTracker.sln
```

The test suite currently covers:

- weekly growth calculations;
- API GET allowlist behavior;
- session expiry and selected-fancier state transitions;
- raw response and sync audit persistence;
- transient retry behavior;
- no-retry behavior for `401`;
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
- normalized pigeon, finance, flight, breeding, or transfer domain tables;
- transfer-sale reconciliation;
- dashboard charts and historical views;
- JSON and CSV export;
- database backup UI;
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
4. Normalized MVP data and dashboard — next major phase.
5. Transfer reconciliation.
6. Flights and breeding.
7. Scheduling, export, packaging, and hardening.
