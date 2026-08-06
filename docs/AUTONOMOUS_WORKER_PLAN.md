# Autonomous Worker — Implementation Plan

The headless worker (`PigeonFancierTracker.Worker`) will fully manage its own pigeon fancier account on pigeonfancier.com without human intervention — breeding, training, racing, feeding, health, finances, and transfers.

---

## Foundation

| # | Task | Status |
|---|------|--------|
| F1 | Game knowledge base (`docs/GAME_GUIDE.md`) | Done |
| F2 | Distance categories fixed (Short 0-200km, Middle 200-500km, Long >500km) | Done |
| F3 | Worker lifecycle: auth → sync → ingest → manage → wait | Done |
| F4 | `WorkerOptions`: ManagementEnabled, AutoFlightEnabled, DryRun | Done |
| F5 | Regex-based POST allowlist in `HttpClientWriteTransport` | Done |
| F6 | Docker Compose env vars for management config | Done |

---

## Phase A — Survival (keep the fancier alive)

### A1. Flight Enrollment

| # | Task | Status |
|---|------|--------|
| A1.1 | `FlightEnrollmentPlan` / `FlightEnrollmentAction` DTOs | Done |
| A1.2 | `IFlightManager` interface | Done |
| A1.3 | `FlightManager.BuildEnrollmentPlanAsync` — score pigeons per flight by distance-category skills | Done |
| A1.4 | GET `/api/flight/{id}/subscriptions` — eligibility check (filter `status == "success"`) | Done |
| A1.5 | POST `/api/flight/{id}/subscriptions` — execute enrollment (`{"add":[], "remove":[]}`) | Done |
| A1.6 | `FlightManager` wired into `FancierWorkerService.RunManagementCycleAsync` | Done |
| A1.7 | DryRun mode logging | Done |
| A1.8 | POST path added to `HttpClientWriteTransport` allowlist | Done |

### A2. Food Management

| # | Task | Status |
|---|------|--------|
| A2.1 | Discover API: check food stock endpoint | Done |
| A2.2 | Discover API: buy food endpoint | Done |
| A2.3 | Discover API: set food distribution (corn/barley/grain/peanuts %) | Done |
| A2.4 | `IFoodManager` interface + DTOs | Done |
| A2.5 | `FoodManager` — monitor stock, buy when < 7 days reserve, set balanced mix | Done |
| A2.6 | Wire into worker lifecycle | Done |
| A2.7 | `WorkerOptions.AutoFeedEnabled` | Done |

### A3. Health Management

| # | Task | Status |
|---|------|--------|
| A3.1 | Discover API: administer medicine endpoint | Todo |
| A3.2 | Discover API: buy medicine from store endpoint | Todo |
| A3.3 | `IHealthManager` interface + DTOs | Todo |
| A3.4 | `HealthManager` — detect sick pigeons from sync, buy correct medicine, administer daily | Todo |
| A3.5 | Disease → medication mapping (8 diseases) | Todo |
| A3.6 | Wire into worker lifecycle | Todo |
| A3.7 | `WorkerOptions.AutoHealEnabled` | Todo |

### A4. Finance Guard

| # | Task | Status |
|---|------|--------|
| A4.1 | Read balance from synced `/api/fancier/selected` snapshot | Done |
| A4.2 | `IFinanceGuard` interface | Done |
| A4.3 | `FinanceGuard` — monitor balance, log warnings at threshold, prevent spending when low | Done |
| A4.4 | Bankruptcy detection (< -5000 EUR) | Done |
| A4.5 | Wire into worker lifecycle (runs before any spending decisions) | Done |
| A4.6 | `WorkerOptions.MinBalanceAlert` | Done |

---

## Phase B — Active Management (compete effectively)

### B1. Training Management

| # | Task | Status |
|---|------|--------|
| B1.1 | Discover API: PATCH `/api/fancier/trainingtype/{general\|conditional\|strategic}` | Done |
| B1.2 | `ITrainingManager` interface + DTOs (`TrainingFocus` enum, `TrainingManagementPlan`) | Done |
| B1.3 | `TrainingManager` — set training focus based on pigeon skill gaps and upcoming flight distances | Done |
| B1.4 | Training matrix logic (distance x focus → skills trained) | Done |
| B1.5 | Weekly rotation: adjust after Monday training reports | Done |
| B1.6 | Wire into worker lifecycle | Done |
| B1.7 | `WorkerOptions.AutoTrainEnabled` | Done |

### B2. Loft Management

| # | Task | Status |
|---|------|--------|
| B2.1 | Discover API: clean loft endpoint | Done (`PATCH /api/barn`) |
| B2.2 | Discover API: upgrade loft endpoint | Todo |
| B2.3 | Discover API: buy pens endpoint | Done — single pen (reuses `POST /api/fancier/items`); breeding pen body needed |
| B2.4 | `ILoftManager` interface + DTOs | Done |
| B2.5 | `LoftManager` — clean loft, buy single pens; upgrade + breeding pen pending | Done |
| B2.6 | Wire into worker lifecycle | Done |

---

## Phase C — Growth & Strategy (win the season)

### C1. Breeding Management

| # | Task | Status |
|---|------|--------|
| C1.1 | Discover API: create couple endpoint | Done (`POST /api/couple`, body `{"id":0,"cockId":X,"henId":Y}`) |
| C1.2 | Discover API: delete couple endpoint | Pending (user will provide) |
| C1.3 | `IBreedingManager` interface + DTOs | Done |
| C1.4 | `BreedingManager` — pair compatible pigeons (complementary skills, libido, breed) | Done |
| C1.5 | 21-day incompatibility detection and pair rotation | Done (detection); split deferred until C1.2 resolved |
| C1.6 | Breeding loft capacity management | Done |
| C1.7 | Wire into worker lifecycle | Done |
| C1.8 | `WorkerOptions.AutoBreedEnabled` (opt-in, default false) | Done |

### C2. Transfer Management

| # | Task | Status |
|---|------|--------|
| C2.1 | Discover API: create transfer listing endpoint | Todo |
| C2.2 | Discover API: dump pigeon endpoint | Todo |
| C2.3 | Auto-bid service (existing `AutoBidService`) | Done |
| C2.4 | `ITransferScout` — evaluate market for undervalued pigeons | Todo |
| C2.5 | `IDumpAdvisor` — identify net-drain pigeons (declining skills, high premiums, persistent disease) | Todo |
| C2.6 | Wire into worker lifecycle | Todo |
| C2.7 | `WorkerOptions.AutoDumpEnabled` (opt-in, default false) | Todo |

### C3. Sponsor Management

| # | Task | Status |
|---|------|--------|
| C3.1 | Discover API: accept/decline sponsor endpoint | Todo |
| C3.2 | `ISponsorManager` interface + DTOs | Todo |
| C3.3 | `SponsorManager` — accept best sponsor deals, maintain 3 active | Todo |
| C3.4 | Wire into worker lifecycle | Todo |

---

## Phase D — Intelligence Layer

### D1. Season Planner

| # | Task | Status |
|---|------|--------|
| D1.1 | Track season week from `/api/season` data | Todo |
| D1.2 | `ISeasonPlanner` interface | Todo |
| D1.3 | `SeasonPlanner` — adjust strategy for promotion push vs relegation avoidance | Todo |
| D1.4 | Prioritize national flights (Sunday) for ranking points | Todo |
| D1.5 | Wire into flight/training decisions | Todo |

### D2. Weather Tactician

| # | Task | Status |
|---|------|--------|
| D2.1 | 14-day forecast reading from `/api/weather` | Done (used by FlightManager) |
| D2.2 | Wind/aerodynamics bonus in scoring | Done (FlightManager) |
| D2.3 | Night flight / nightvision bonus in scoring | Done (FlightManager) |
| D2.4 | Avoid enrolling in extreme weather conditions | Todo |
| D2.5 | Pre-plan training type shifts based on upcoming weather | Todo |

---

## Write Transport — API Endpoint Registry

POST/PUT endpoints that must be allowlisted in `HttpClientWriteTransport.cs` as they are discovered.

| Endpoint | Method | Purpose | Status |
|----------|--------|---------|--------|
| `/api/transfer/bid` | POST | Place bid on transfer | Done |
| `/api/flight/{id}/subscriptions` | POST | Subscribe pigeons to flight | Done |
| `/api/fancier/items` | POST | Purchase food/medicine from store | Done |
| `/api/fancier/distribution` | PUT | Set food mix percentages | Done |
| `/api/fancier/trainingtype/{type}` | PATCH | Set training focus (general/conditional/strategic) | Done |
| Administer medicine | ? | Treat sick pigeon | Need API |
| `/api/couple` | POST | Create breeding pair (`{"id":0,"cockId":X,"henId":Y}`) | Done |
| Delete couple | ? | Split breeding pair | Need API |
| Dump pigeon | ? | Remove pigeon from loft | Need API |
| Create transfer | ? | List pigeon for sale | Need API |
| `/api/barn` | PATCH | Clean loft for hygiene | Done |
| Upgrade loft | ? | Upgrade loft capacity | Need API |
| Accept sponsor | ? | Accept sponsor deal | Need API |

---

## Worker Lifecycle (target)

Each sync interval the worker runs this sequence:

```
1. Authenticate          (done)
2. Sync                  (done — Quick profile)
3. Ingest flight results (done)
4. Finance guard         (done — Phase A4)
5. Health check          (todo — Phase A3)
6. Food check            (done — Phase A2)
7. Training review       (done — Phase B1)
8. Flight enrollment     (done — Phase A1)
9. Breeding management   (done — Phase C1, split pending C1.2)
10. Transfer operations  (done — auto-bid; todo — scout/dump)
11. Loft maintenance     (done — Phase B2, upgrade pending)
12. Wait                 (done)
```

---

## Configuration (target)

```csharp
// WorkerOptions.cs — current + planned
ManagementEnabled       // master switch                    (done)
AutoFlightEnabled       // enroll pigeons in flights        (done)
AutoFeedEnabled         // monitor/buy food                 (done)
AutoHealEnabled         // detect/treat sick pigeons        (todo)
AutoTrainEnabled        // adjust training focus            (done)
AutoLoftEnabled         // clean loft, buy pens             (done)
AutoBreedEnabled        // manage breeding pairs            (done, opt-in)
AutoDumpEnabled         // dump underperforming pigeons     (todo, opt-in)
DryRun                  // log actions without executing    (done)
MinFoodDaysReserve      // days of food to keep in stock    (done)
AutoFinanceGuardEnabled  // monitor balance, gate spending    (done)
MinBalanceAlert         // warn below this balance          (done)
```
