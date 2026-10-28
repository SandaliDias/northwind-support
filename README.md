# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

---

## The assignment

You are joining a team that already has this app working. Complete the tasks **in order**.
Full requirements are in **[`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md)**.

| # | Task | Done looks like | Effort |
| --- | --- | --- | --- |
| 1 | **Fix a defect, add server-side filtering** | Root cause fixed with a regression test; every list filter applied in the database with a correct count | ~2.5 h |
| 2 | **Ticket escalation & assignment** | Automatic triage on create, escalation with history, SLA status, UI for all of it | ~7 h |
| 3 | **Login and authentication** | Agents sign in; API rejects unauthenticated calls; login UI | ~4 h |
| 4 | **Dockerise the API** *(optional)* | `docker compose up` gives a migrated, seeded API with no baked-in secret | ~1.5 h |

### Guidance

- **Time: 2 days.** Honestly marking a task "not finished" beats rushing all of them.
- **Fork** this repo to your own GitHub account and work there. **Do not push to the original.**
- **AI tools are allowed.** You must understand and be able to defend every line you submit.
- **Commit as you go** and keep the real history. Commit the Task 1 defect fix and the filtering
  separately from each other and from the feature work. A single squashed commit is not accepted.
- **Where the spec is ambiguous**, make a decision, write it down in the README, and move on.
- **Out of scope:** registration, password reset, roles, OAuth/SSO, refresh tokens, MFA,
  notifications, CI, caching, visual redesign. Mention them under *Known limitations* instead.

### Submission

1. Update this **README** with what you built, your design decisions (chosen, rejected and why),
   the defect's root cause, dev login credentials, how to run and test it, AI usage, and known
   limitations. A reviewer must be able to run it without asking you anything.
2. Add an **APPROACH.md** explaining your approach and thought process for each task.
3. Commit both, then send us the **link to your fork**, the **dev login credentials**, and a
   **rough note of time spent** on what. (If the fork is private, add the reviewers as collaborators.)

---

## Defect fix (DEFECT-117)

**Root cause:** The `useEffect` hook in `useTicketList` debounced fetches but only included `[filters.search, filters.page, filters.pageSize]` in its dependency array, omitting `status` and all other filter controls. Consequently, changing dropdown filters never triggered a network request until a tracked dependency such as `search` changed and triggered the debounced effect with the accumulated state.

**Fix:** In `useTicketList`, any non-search filter change triggers an immediate request to `GET /api/tickets`, while `search` input changes remain debounced by 300ms so no requests fire per keystroke. All non-search filter properties (including Task 2's `slaStatus`) trigger immediately without requiring special wiring.

---

## Server-side filtering (Task 1.2)

- **Where applied:** Filtering is implemented directly in `SupportDesk.Infrastructure.Queries.TicketQueries.ApplyFilters`. It operates on the `IQueryable<TicketWithLabels>` query before `CountAsync` and pagination (`Skip`/`Take`), guaranteeing that all counts, total pages, and retrieved records are filtered entirely in SQL by EF Core without in-memory C# evaluation.
- **Supported filters:**
  - `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`: exact matches on the corresponding aggregate fields.
  - `unassignedOnly`: when `true`, filters records where `AssignedAgentId == null`.
  - `search`: trimmed substring match across ticket `Title`, `Reference` (e.g. `TCK-0042`), or `Customer.Name`. Blank/whitespace-only input is ignored.
  - Combination: all active filters compose with `AND`.
- **How the next filter is added (Task 2 `slaStatus`):**
  - Extend `TicketQuery` with the new filter property.
  - Add one conditional `.Where(...)` clause inside `ApplyFilters`. The underlying query joins, sorting, and pagination logic remain unchanged.

---

## Task 2: triage, escalation and SLA

**Verification:** Run `dotnet test --configuration Release`, `npm run test --workspace apps/web`, `npm run lint`,
`npm run typecheck`, and `npm run build --workspace apps/web`. For real SQL/HTTP coverage, set
`SUPPORTDESK_TEST_SQL` as shown in Tests section before dotnet test. The Task 2 recorded result was 77 backend tests (three SQL/HTTP tests included), four architecture/model
checks, and 22 frontend tests. The current seven opt-in tests create only
unique SupportDesk_Task2_<guid> databases and remove them in finally blocks; the HTTP test starts
and terminates only its own API process on an ephemeral loopback port. No existing application
process or database is stopped/reset. Without that environment variable the tests are visibly
skipped. Ordinary SQLite/domain/component/architecture tests need no server.

Tests cover all priority/tier windows, configurable floor, forced priority flags, specialization,
capacity/inactive/ties/no candidates, retained/replaced owners, escalation state/reason/actor
rejections and exact history snapshots, immutable persistence and concurrency rollback, SLA
boundaries and SQL parity, HTTP status contracts including successful unassignment, five distinct
SLA labels, real SLA query serialization, client validation/no-call, pending state, 409 without
mutation, and detail/history refresh including partial failure. No load test, browser screenshot
review, multi-instance capacity serialization or production migration deployment was performed.

## Quick start

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

```bash
cp .env.example .env          # optional, every value has a working default
npm install                   # installs the web app
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
export Jwt__SigningKey="$(node -e 'process.stdout.write(require("node:crypto").randomBytes(48).toString("base64"))')"
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server
```

In **PowerShell**, generate the key in the API terminal before `npm run api`:

```powershell
$env:Jwt__SigningKey = node -e "process.stdout.write(require('node:crypto').randomBytes(48).toString('base64'))"
npm run api
```

Keep the same key when restarting that terminal's API. `.env.example` lists JWT/database settings;
Compose reads `.env`, while `dotnet run`/`npm run api` use exported environment values and do not
automatically read that file. No signing key is committed. API startup fails clearly if it is
missing or shorter than 32 bytes.

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. **Never commit real
credentials.**

**Visual Studio 2026:** configure `Jwt:SigningKey` using the project's user-secrets UI, run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

## Task 3: sign-in and credentials

**Local fixture login:** `alex.turner@northwind-support.example` / `Local_Only_Demo1!`.

## Task 4: run the API in Docker

With Docker Desktop running, set the JWT key using the command above (or fill `Jwt__SigningKey`
in your ignored `.env`), then run from the repository root:

```bash
docker compose up --build
```

The API is at **http://localhost:5080**, Development Swagger at `/swagger`, and anonymous
`GET /health` returns `Healthy` (200) when the database is reachable or `Unhealthy` (503) otherwise.
All `/api` data routes still require a token: log in with the fixture above, then use Swagger's
Authorize input or `Authorization: Bearer <token>`. A fresh volume receives migrations, six
credentialed demo agents and 40 tickets. Existing data and password hashes are preserved.

`apps/api/Dockerfile` restores the four-project API dependency graph before copying C# source,
publishes with the .NET 10 SDK, then runs on the ASP.NET 10 runtime as `app` on HTTP 8080. No SDK,
web app, appsettings files, source, environment file or development certificate is published.
The build context is an API-only allowlist. Compose supplies configuration at runtime and waits
for SQL Server's health check before starting the API; the image has no deployment credentials,
JWT key or environment name. Non-secret policy defaults are also supplied through runtime settings.

`.env.example` lists `MSSQL_SA_PASSWORD`, `SQLSERVER_PORT` (1433), `API_PORT` (5080), environment,
JWT issuer/audience/lifetime/key and `Sla__...` options. Host `ConnectionStrings__Default` is for
`dotnet run`; Compose constructs its own connection string using **sqlserver,1433**, never
localhost. Ports bind to IPv4 loopback; host SQL settings use `127.0.0.1` to avoid IPv6 localhost
resolution differences. An empty JWT key fails API startup clearly while SQL-only
`npm run db:up` remains usable without a key.

Run React locally against the container:

```bash
VITE_API_PROXY_TARGET=http://localhost:5080 npm run web
```

```powershell
$env:VITE_API_PROXY_TARGET='http://localhost:5080'
npm run web
```

**Verification:** `npm run verify:docker` runs [scripts/verify-task4.mjs](scripts/verify-task4.mjs).
It requires Node 22, Docker-engine access and an available port 5080. It resets only a uniquely
named disposable Compose project/volume, checks fresh startup and authenticated seed data,
non-root execution, no runtime SDK/configuration/source/certificates, web proxy login/list/create/
escalation, preserved data/history/hashes across restart, database health failure/recovery, and a
cached restore after a temporary `.cs` change. It restores the source, removes its stack/key and
writes logs/results under ignored `artifacts/task4-*`. It never prunes Docker or resets the
existing development volume. `docker compose down -v` removes the selected project's database;
use that command only for a disposable database.

**Verification recorded:** the initial disposable run passed fresh migration
(three migrations), 40 seeded tickets, six credentialed agents, anonymous health 200, protected
tickets 401, runtime user `app` (UID 1654), no runtime SDK/configuration/source/certificates,
Vite proxy login/list/create/escalation, and restart preservation of tickets/history/password
hashes. The database-outage health request then timed out. The probe was changed to a fresh
connection with a three-second connect timeout and no retries. Container outage/recovery, the
final rebuilt image, source-change restore caching and measured runtime/SDK image sizes remain

## Tests

The final Task 4 source run passes **97 backend tests**, including all **seven SQL/HTTP tests**,
and **four architecture/model checks**. This includes anonymous health 200/503 and the
unreachable-server timeout/non-disclosure regression. Without SQL opt-in, 90 backend tests run
and seven are skipped. The unchanged web suite's prior Task 3 run passed 45 tests. Scoped C#
formatting, verifier syntax and Git whitespace checks passed. Container checks still pending
are listed above.

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

The ordinary suites do not need a database. Seven SQL Server/real HTTP integration tests are
explicitly skipped unless `SUPPORTDESK_TEST_SQL` is set. In PowerShell, after `npm run db:up`:

```powershell
$env:SUPPORTDESK_TEST_SQL='Server=127.0.0.1,1433;Database=master;User Id=sa;Password=Local_Dev_Password1;TrustServerCertificate=True;Encrypt=False'
dotnet test --configuration Release
```

These tests create and delete only their own `SupportDesk_Task2_<guid>` databases and require
create/drop permission. Release avoids Debug DLL locks if the developer API is running.
Web tests use one thread worker to avoid fork startup timeouts on constrained Windows hosts.

## Commands

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |

## API at a glance

| Endpoint | Description |
| --- | --- |
| `GET /health` | Anonymous database reachability |
| `POST /api/auth/login` | Anonymous email/password login; token, expiry and user |
| `GET /api/auth/me` | Current authenticated agent |
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`, `sortBy`, `sortDirection`; filters/count/sort/paging run in SQL. |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create with automatic priority, due date, owner and `triage` explanations (201 even when unassigned) |
| `POST /api/tickets/{id}/escalate` | `{ "reason": "Customer impact increased" }`; actor comes from token; returns updated `ticket` and `escalation` |
| `GET /api/tickets/{id}/escalations` | Immutable before/after history, newest first |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |

All data endpoints require a bearer token centrally. Login, health and Development Swagger stay open.
Errors are `ProblemDetails`: `400` validation, `401` authentication, `404` not found, `409` conflict.

**Seed data:** 5 categories, 6 customers (3 Premium), 6 agents (one inactive, one at their limit)
and 40 historical tickets across statuses/priorities and timed SLA states; No SLA is covered by explicit test fixtures.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |

## Current limitations and AI use

Task 4 source is implemented; remaining container checks are listed above. The optional SLA summary
report remains unimplemented.
