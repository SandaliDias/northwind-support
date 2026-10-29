# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

.NET 10 API, React 19/TypeScript and SQL Server. Implements ticket filtering, automatic triage,
escalation/history, JWT login, API containerisation and the optional SLA summary endpoint.
Detailed reasoning is in [APPROACH.md](APPROACH.md)

## Run locally

Requires Docker Desktop, Node 22 (`.nvmrc`); .NET SDK 10 (`global.json`) is needed for host API/tests.
From the repository root, in PowerShell:

```powershell
cp .env.example .env          # optional overrides; generate the signing key below
npm ci
$env:Jwt__SigningKey = node -e "process.stdout.write(require('node:crypto').randomBytes(48).toString('base64'))"
docker compose up --build
```

In a second terminal:

```powershell
$env:VITE_API_PROXY_TARGET='http://localhost:5080'
npm run web
```

POSIX web command: `VITE_API_PROXY_TARGET=http://localhost:5080 npm run web`.
Open http://localhost:5173; API/Swagger are http://localhost:5080 and http://localhost:5080/swagger.
Sign in with **alex.turner@northwind-support.example / Local_Only_Demo1!**.
Swagger: call `POST /api/auth/login`, copy its token into Authorize, then call protected endpoints.

Development startup applies the three committed migrations and seeds 40 tickets/six credentialed
demo agents. Restart preserves data and existing password hashes. The fixture password is local
development data; Production never provisions it.

Compose reads an optional ignored `.env` copied from `.env.example`. Exported environment values
take precedence. Generate your own signing key; no usable key is committed. Settings include
environment, JWT key/issuer/audience/lifetime, SQL password/host port, API port and all `Sla__...`
policy values. Defaults publish only IPv4 loopback: SQL 1433, API 5080. Containers reach
`sqlserver,1433`; host SQL uses `127.0.0.1,1433`. Compose deliberately ignores a host-local
`ConnectionStrings__Default` and constructs the service-address connection string.

For a host API instead: `npm run db:up`, generate/export the same key in the API terminal, then
`dotnet run --project apps/api/src/SupportDesk.Presentation --launch-profile http`.
Set the web terminal's proxy target to `http://localhost:5043`. Host dotnet run does not read
root `.env` automatically; export any overrides. These HTTP instructions need no certificate.

## Rules and design decisions

Creation uses requested priority or Medium; category flags can force Critical. Base SLA hours
are Critical 4, High 8, Medium 24, Low 72. Premium multiplies by 0.5 with a 1-hour floor.
Windows, multiplier, floor and at-risk percentage are runtime configuration, validated at startup.
Specialist categories require matching specialisation. Eligible owners are active and strictly
below capacity; choose fewest open tickets, then lowest agent ID. Open excludes Resolved/Closed.
No eligible agent is a normal unassigned 201 with an explanation; manual changes share the rules.

Escalation raises one priority step, restarts the SLA window, and retains an eligible owner or
selects another. The current ticket counts toward capacity. Critical/Resolved/Closed reject with
409; reason is trimmed and must be 5–500 characters. Immutable history records before/after values,
reason, token-derived actor (`agent:<id>`) and time. No client actor is trusted.

SLA is derived: no due date wins; resolution at/before due is Met, late resolution is Breached.
An unresolved ticket exactly at due is AtRisk; strictly after due is Breached. At or below the
configured remaining-window percentage is AtRisk; otherwise WithinSla. One injected clock value
is used per query. Policy changes affect new windows, not existing promised deadlines.

- **CQRS:** handlers orchestrate writes; query services project narrow DTOs directly in SQL.
  A mapped SQL SLA function keeps count/filter/page/report semantics aligned. Materialising
  tickets to filter or aggregate in C# was rejected.
- **Repository / Unit of Work:** repositories load command aggregates; one save commits ticket
  and escalation history atomically. Read services avoid one query per ticket/agent.
- **Domain policies/behaviour:** shared priority/window/eligibility rules serve creation and
  escalation; ticket methods enforce transitions. A generic rules engine or mediator framework
  would add complexity without improving these rules.

The triage response explains priority, window and assignment. One accessible SLA indicator serves
list/detail. The escalation form handles validation/pending/409 and refreshes ticket/history
without reloading; a history-refresh failure preserves the confirmed ticket and offers retry.

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

## Ticket triage, escalation and SLA

Task 2's migration adds SLA start metadata/concurrency token and immutable escalation snapshots;
legacy windows are backfilled without changing historical priorities/deadlines. The history
index starts with TicketId then descending timestamp/ID for newest-first lookup; optional agent
FKs restrict deletion. Task 3 adds nullable agent PasswordHash and normalises legacy emails.
History immutability is protected by the application, not against a privileged SQL administrator.

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

## Authentication

Nullable Agent.PasswordHash fits existing users; a separate Identity user/role schema was rejected
because registration/roles are out of scope. Identity PasswordHasher uses vetted salted PBKDF2;
successful login upgrades hashes when required. Development backfills only missing known fixture
hashes, independently of existing tickets. Unknown/wrong/inactive logins share a generic 401.

Presentation issues HS256 JWTs; signature, algorithm, issuer, audience, positive subject and expiry
are validated centrally, with 30-second skew. Default lifetime is configurable 60 minutes; startup
requires a signing key of at least 32 bytes. Active-agent lookup rejects deactivated agents.
Fallback authorization protects data/report routes; only login, Development Swagger and centrally
configured database health are public.

The browser stores token/expiry in sessionStorage (per tab), validates restored sessions through
me, attaches Bearer centrally, and clears session on logout/401. Protected routes preserve a safe
internal return destination. This is exposed to JavaScript/XSS; production needs HTTPS, managed
key rotation and a deliberate session design (for example BFF/HttpOnly cookies with CSRF controls).
Password changes alone do not revoke existing JWTs before expiry.

## Optional SLA summary

Open **SLA summary** in the main navigation at `/reports/sla-summary`. Sign-in is required.
Choose From/To UTC calendar dates and Agent or Priority, then **Generate report**. Both dates
are included; the UI sends midnight after the end date as the API's exclusive upper bound.
The table shows every SLA count, breach percentage and average resolution hours, including
Unassigned and No SLA. No resolved tickets displays “No resolutions”; a zero-hour average remains
0.00. Loading disables controls; validation, empty results and server errors/retry are explicit.
Changing draft filters leaves the displayed report labelled with its actual submitted range.
The table scrolls horizontally on small screens. Existing authentication/401 handling is reused.

`GET /api/reports/sla-summary?from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z&groupBy=agent`
requires a Bearer token. Both timestamps are required ISO 8601 instants with Z or an explicit
offset (URL-encode `+` as `%2B`); offsets normalise to UTC. Creation cohort is `[from,to)`, with
to strictly later than from. groupBy is agent (default) or priority, case-insensitive; bad input
returns shared 400 ProblemDetails.

Returns an array per current assignee/priority: total, breached, atRisk, within, met,
notApplicable, breachRate and averageResolutionHours. Agent groups contain agentId/agentName,
including null/Unassigned and inactive owners; priority groups contain priority. Unused group
keys are null. Agent IDs distinguish duplicate names. Only populated groups are returned, sorted
by ID/priority; an empty cohort is `[]`.

Breach rate is `100 * breached / total`, rounded to two decimal places; no-SLA tickets remain in
total and are exposed separately so all counts reconcile. Average resolution hours is
creation-to-resolution across resolved/closed tickets, including no-SLA resolutions; unresolved
tickets are ignored and no resolutions gives null. It measures elapsed UTC hours, not working
hours or an as-at historical snapshot. Current SLA state uses one injected now/current threshold.
One SQL command performs filtering, grouping, counts, percentage and average; no migration was
needed. The UI displays these server metrics without recomputing them. Day/time-of-day arithmetic avoids millisecond DATEDIFF overflow.

## Docker and production

`apps/api/Dockerfile` uses root context and .NET 10 SDK restore/publish, then ASP.NET 10 runtime
as `app` on HTTP 8080. Shared build files/all API csproj files precede source copying for restore
caching. The context allowlist excludes web/tests, generated outputs, .git, env files,
appsettings and certificates; only published runtime output is copied. Deployment connection,
JWT key, environment and SLA options arrive at runtime. SQL's sqlcmd health check gates API startup.
Anonymous `/health` returns 200/Healthy or 503/Unhealthy without provider/credential details;
its fresh SQL connection has a three-second timeout, no retries and a five-second cancellation limit.

`npm run verify:docker` resets only its own uniquely named disposable project/volume, exercises
fresh seed/login/proxy/restart/outage, inspects user/contents, changes/restores one .cs file for
cache proof, rebuilds original source and measures image bytes. It saves non-secret evidence under
ignored `artifacts/task4-*` and removes its own stack/key. **Port 5080 may stay in use**: verification
prefers it when free, otherwise chooses a free API port and routes its own web proxy to that port.
SQL uses a separate free port. The normal app's default remains 5080. Run `npm run test:docker-verifier`
for the occupied-port regression. Never run unscoped `docker compose down -v` against data you need.

## Verification, limitations and AI use

```powershell
dotnet restore
dotnet test --configuration Release --no-restore
npm run test --workspace apps/web
npm run typecheck
npm run lint
npm run build
npm run format:check -- --no-restore
```

Real SQL/HTTP tests opt in before dotnet test:
```powershell
$env:SUPPORTDESK_TEST_SQL='Server=127.0.0.1,1433;Database=master;User Id=sa;Password=Local_Dev_Password1;TrustServerCertificate=True;Encrypt=False'
```
They create/drop only unique SupportDesk_Task2_<guid> databases and start only their own API
processes. Normal suites do not require SQL. Optional-report tests verify all metrics, UTC
bounds/offsets, both groupings, empty/null cases, changed clock/threshold, long fractional
durations, shared validation/auth and one executed SQL GROUP BY/AVG/count command per report.
Full backend run: **117 passed**, including all **nine SQL/HTTP tests**, plus **four architecture
checks**. The report-specific run passed 20 tests. The latest report-UI run passed **65 web tests**, including
real client serialization/auth, dates, grouping/metrics, pending/empty/error/retry/cancellation and
protected return navigation. Three verifier port tests, type checking, linting and
the web production build passed. Full C# formatting (no-restore after a successful restore)
and Git whitespace checks passed. Without SQL opt-in, 108 backend tests run and nine skip.

No load test, fresh-clone rehearsal or
production deployment is claimed.
Capacity selection/reference generation can race across concurrent requests; a single-ticket
concurrency token does not serialise global capacity. At scale review plans, a CreatedAtUtc
report index, substring search and keyset paging; no unmeasured performance promise is made.
Registration/reset/verification, roles, SSO, refresh/MFA, abuse controls, notifications/jobs,
realtime, microservices, caching, event sourcing, tenancy, localisation, redesign and extra
deployment infrastructure were deliberately excluded.

Codex was used selectively, with clear guidance, for code tracing, implementation support, test generation, and documentation. Its outputs were reviewed and validated, with decisions and final changes guided by my own understanding and judgment. Useful workflow
was pure rules first, then compilation and real SQL/HTTP checks; the existing SlaCell was reused.
A separate Identity schema and in-memory SLA aggregation were rejected. Tests caught malformed
session storage not being cleared and the original container health timeout; fixes were tested.
The report rejected a whole-duration millisecond DATEDIFF because it overflows on long tickets,
then proved the alternative against SQL Server. Generated code was reviewed, formatted and tested;
detailed choices and verification limits are recorded in APPROACH.md.
