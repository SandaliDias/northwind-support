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

Creation uses requested priority (default Medium), overridden by the category's forced-Critical
flag. `Sla` in `appsettings.Development.json` controls 4/8/24/72-hour windows, Premium's 0.5
multiplier, 1-hour Premium floor and 25% risk threshold. Change configuration and restart;
existing due dates remain promises, while subsequent create/escalate operations use new windows.

Active eligible agents must be below capacity and specialized when the category flag requires it.
Choose fewest open tickets, then lowest agent ID. Escalation keeps an eligible owner, including
this ticket in the open count: an owner at the limit is ineligible. Changed manual assignments
use the same rules. No candidate is a normal, explained unassigned outcome.

Escalation raises one level and resets the window from now; Critical/Resolved/Closed returns 409.
Reasons are trimmed to 5–500 characters, temporary actors to 1–100; the UI validates before
submission. Task 3 must replace this caller-supplied actor with the authenticated identity.
Exactly at due is At risk for unresolved tickets and Met for a resolution; breach is strictly late.
SLA is derived, never stored. The migration adds window-start metadata, a concurrency token,
immutable `TicketEscalations` and the mapped SQL function used before count/paging. Its
`(TicketId, EscalatedAtUtc DESC, Id DESC)` index supports stable newest-first history; agent FK
indexes support referential checks. Ticket/agent deletes are restricted to retain audit history.

**Design:** The primary design constraint was sharing rules between create and escalation without putting
EF/HTTP in the domain or business decisions in controllers. `SlaPolicy` owns configurable window
calculation and priority resolution; `AgentAssignmentPolicy` owns eligibility, preferred-owner
retention and deterministic selection. `TicketTriageService` combines those policies with a
single projected candidate query and human-readable explanations. A generic rules engine and
one interface per rule were rejected because these are small deterministic policies.

CQRS already fits the starter: command handlers change tracked aggregates; no-tracking queries
project only the DTO data needed by list/detail/history. Repository/Unit of Work is the existing
persistence seam; one SaveChanges transaction includes an escalation's ticket update and audit
insert. A GUID concurrency token changes on ticket mutations, so stale writes become a 409 and
the losing audit insert rolls back. The persistence test exercises two contexts loaded before
the winning escalation, rather than merely mocking a conflict.

The current owner must remain strictly below MaxOpenTickets, including this open ticket, on
escalation. This literal BR-5 interpretation can replace an agent who is exactly at capacity;
it is explicitly tested. Manual assignment no longer bypasses the specialist/capacity rules for
a changed owner. An eligible current owner is retained over a less busy alternative. Ineligible
or absent owners are reassigned by open count then ID; nobody eligible is a successful null
assignment. General/Technical specialization names are not hard-coded: only flags drive rules.

`Ticket.ApplyTriage` and `Ticket.Escalate` provide domain behavior for state changes. Escalate
checks terminal status/Critical before mutation and snapshots priority, nullable owner and due
date before updating them. Immutable history has private setters, no update/delete API, save-time
mutation protection, length constraints, priority constraints and restricted foreign keys. This
does not claim protection against privileged direct SQL changes. The history index begins with
TicketId and uses descending timestamp/ID for both lookup and deterministic newest-first order.
Separate agent FK indexes are retained; no redundant TicketId-only index is needed.

**Verification:** Run `dotnet test --configuration Release`, `npm run test --workspace apps/web`, `npm run lint`,
`npm run typecheck`, and `npm run build --workspace apps/web`. For real SQL/HTTP coverage, set
`SUPPORTDESK_TEST_SQL` as shown in Tests section before dotnet test. The verified result was 77 backend tests (three SQL/HTTP tests included), four architecture/model
checks, and 22 frontend tests. The three opt-in tests create only
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
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server
```

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. **Never commit real
credentials.**

**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

## Tests

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

The ordinary suites do not need a database. Three SQL Server/real HTTP integration tests are
explicitly skipped unless `SUPPORTDESK_TEST_SQL` is set. In PowerShell, after `npm run db:up`:

```powershell
$env:SUPPORTDESK_TEST_SQL='Server=localhost,1433;Database=master;User Id=sa;Password=Local_Dev_Password1;TrustServerCertificate=True;Encrypt=False'
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
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`, `sortBy`, `sortDirection`; filters/count/sort/paging run in SQL. |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create with automatic priority, due date, owner and `triage` explanations (201 even when unassigned) |
| `POST /api/tickets/{id}/escalate` | `{ "reason": "Customer impact increased", "escalatedBy": "Team lead" }`; returns updated `ticket` and `escalation` |
| `GET /api/tickets/{id}/escalations` | Immutable before/after history, newest first |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |

Errors are `ProblemDetails`: `400` validation, `404` not found, `409` conflict or business rule.

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

Task 3 authentication and Task 4 API container are not implemented in this change. Login
credentials are therefore not available yet. The optional SLA summary report is not implemented.
Simultaneous creates can race on agent capacity and highest-ID reference generation; optimistic
concurrency protects changes to an existing ticket, not global agent capacity. Historical seed
priorities are retained even where current create-time flags would force Critical. SQL reference
scripts describe the starter; use EF migrations for the upgraded schema. No browser visual QA
was performed; automated component and real API/database tests verify the implemented behavior.

Codex assisted with repository review, implementation and tests. Generated code was checked by
compilation, architecture tests, SQL Server parity/upgrade/HTTP tests and React behavior tests;
[APPROACH.md](APPROACH.md) records an accepted design and a generated test issue corrected during
verification. Registration/reset, roles, SSO, refresh tokens, MFA, notifications, background jobs,
CI additions, caching, real-time updates and deployment work remain outside this Task 2 change.
