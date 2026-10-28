# Implementation approach

## Task 1 context

Task 1 involved diagnosing and fixing DEFECT-117 in the frontend ticket-filter flow (Task 1.1) and implementing the backend filtering that the API query contract already implied (Task 1.2). The approach was to identify the actual failure mechanism, make the smallest maintainable change consistent with the existing architecture, and verify the behavior with regression tests. Both changes also needed to support Task 2's SLA filtering without introducing special-case logic into shared components.

### 1.1 Defect fix (DEFECT-117)

#### Root cause analysis

The first step was to trace the filter update flow from the ticket-list controls through updateFilters and into the useEffect responsible for loading tickets. The defect was not in the dropdown controls themselves: the filter state updated, but the effect did not observe all the values that determined when a request should run.

The effect in apps/web/src/features/tickets/hooks/useTicketList.ts suppressed the React exhaustive-dependencies lint rule and tracked only [filters.search, filters.page, filters.pageSize]. Changes to status and other dropdown filters were therefore not independently observed. Because changing a filter also reset the page to 1, the page dependency did not change when the user was already on the first page. The UI could consequently display the updated filter selection without issuing a corresponding request.

Search appeared to resolve the problem temporarily because changing filters.search triggered the effect. After its 300 ms debounce, the request used the latest filter state, including previously selected dropdown values. Clearing or changing search could therefore make the accumulated filters take effect, masking the original defect rather than fixing it.

#### Design decisions

The fix needed to distinguish between search input, which benefits from debouncing, and other filter changes, which should take effect immediately.

Track dependencies correctly: Remove the lint suppression and make the effect respond to filter state changes instead of relying on an incomplete dependency list.

Preserve search debouncing: Keep a 300 ms delay for search input to avoid sending a request for every keystroke.

Make other filter changes immediate: Changes to status, priority, category, customer, assigned agent, unassigned state, sorting, and pagination should trigger a request without waiting for a search change.

Handle page resets deliberately: When a user searches from a later page, group the reset to page 1 with the debounced search rather than issuing an extra immediate request for each keystroke.

Avoid hard-coded filter wiring: Detect changes to non-search filter properties dynamically. This allows new filters, including Task 2's slaStatus, to use the same behavior without modifying the hook for each new field.

Prevent stale responses: Assign each request an incrementing identifier and ignore responses from older requests when a newer request has already been initiated. This prevents slower responses from overwriting more recent results.

Reset consistently: Clearing filters should issue an immediate request using the default filter state.

The alternative of adding individual effect dependencies or separate handlers for every dropdown was rejected because it would duplicate logic and make future filter additions easier to miss. Suppressing the exhaustive-dependencies rule was also rejected: the dependency tracking needed to reflect the real state used by the effect.

#### Regression testing and verification

Tests were added in apps/web/src/features/tickets/hooks/useTicketList.test.ts to verify immediate status-filter requests, immediate SLA-filter requests without special wiring, search debouncing, and immediate reset behavior.

The DEFECT-117 regression test was first checked against the original implementation, where it failed because the expected request was not made. It passed after the fix. This confirmed that the test reproduced the original failure rather than merely exercising the corrected implementation.

The frontend test suite should be run with:

npm --prefix apps/web test

The README was updated with the defect's root cause and the corrected request behavior.

### 1.2 Server-side filtering

#### Problem analysis

The API already accepted filter parameters and bound them into TicketQuery, but the infrastructure query did not apply those values. Fixing this required following the query from its request contract into the database access layer and ensuring that filtering affected both the returned tickets and the pagination metadata.

The main architectural constraint was that filtering had to happen in the database before counting and paging. Loading a broad result set and filtering it in C# afterward would produce incorrect totals and page boundaries, could omit matching records from later pages, and would scale poorly as the ticket dataset grew.

#### Design decisions

Filtering was implemented in SupportDesk.Infrastructure/Queries/TicketQueries.cs using a dedicated ApplyFilters method that composes predicates on an IQueryable.

FL-1 — Exact matches: Apply equality predicates for supplied status, priority, categoryId, customerId, and assignedAgentId values. This ensures that categorical and identifier filters return only tickets matching the selected value.

FL-2 — Unassigned tickets: When unassignedOnly=true, filter for a null AssignedAgentId. When the flag is false or absent, do not add this restriction.

FL-3 — Search: Trim the search term and match it against ticket title, ticket reference (for example, TCK-0042), or customer name. Ignore null, empty, and whitespace-only terms.

FL-4 — Combining filters: Apply each supplied criterion as another Where predicate. This composes the filters using logical AND, so a ticket must satisfy every active criterion.

FL-5 — Database execution and pagination: Apply filters before CountAsync, sorting, Skip, and Take. Both the total count and page results therefore operate on the same filtered query. The returned totalCount, totalPages, and page contents reflect the filtered dataset without a separate in-memory filtering step.

FL-6 — Extensibility: Keep filtering logic isolated from sorting and pagination. Adding a new filter should require only another predicate in ApplyFilters, not a restructuring of the query.

A separate query method for each filter combination was rejected because the combinations would grow rapidly and duplicate query logic. Filtering the results after materialization was rejected because it would violate the database-execution requirement and produce unreliable pagination metadata.

The composable IQueryable approach keeps the implementation small, lets the database provider translate predicates into SQL, and gives future tasks a clear place to introduce additional filters.

#### Regression tests and verification

SQLite in-memory tests were added in SupportDesk.UnitTests/Infrastructure/Queries/TicketQueriesTests.cs. A relational provider was chosen over mocked query results so that the tests exercise actual query composition and database filtering behavior.

##### Coverage includes:

Exact-match priority and status filters.

Filtering for tickets without an assigned agent.

Search by customer name, title, and ticket reference.

Ignoring whitespace-only search input.

Combining multiple filters using AND semantics.

Correct filtered counts, page totals, and page slices.

The customer-name search test was strengthened so that the ticket titles do not contain the searched customer name. This prevents a title match from accidentally satisfying a test intended to verify the customer-name predicate.

The backend tests should be run with:

dotnet test apps/api/tests/SupportDesk.UnitTests/SupportDesk.UnitTests.csproj

The README was updated to explain where filtering is applied, why it must precede counting and pagination, and how additional filters can be added.

## Task 2: decisions and placement

The primary design constraint was sharing rules between create and escalation without putting
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

### SLA dates, boundaries and database filtering

An SLA window begins at creation, then begins again on escalation. `UpdatedAtUtc` is unsuitable
because unrelated status/assignment changes update it. The migration therefore adds nullable
`SlaStartedAtUtc`, backfilling existing timed tickets from CreatedAtUtc. Historical due dates and
priorities are retained; new tickets follow the current flags. Changing policy windows affects
future decisions, never retroactively changes a due date. At-risk evaluation uses the actual
stored interval and currently configured threshold.

SLA precedence is No SLA, resolved on-time/late, unresolved late, at-risk, within. Due is an
inclusive boundary for on-time resolution; an unresolved ticket at due is at-risk, and the next
instant is breached. Exactly the threshold percentage remaining is at-risk. One injected now
is captured before both SQL count and page queries. Window decisions are rounded to the schema's
millisecond precision; the status function compares nanosecond differences so injected now and
fractional threshold boundaries are not truncated to minutes or seconds. Config accepts positive
windows/floor up to one year, decisions of at least one millisecond, Premium multiplier (0,1],
and a threshold (0,100] with up to eight decimal places. The one-year limit also keeps SQL
DATEDIFF_BIG(nanosecond) comfortably within its supported duration range.

A private C# method in a final Select can execute client-side; it cannot simply be reused in
Where. Instead, `SupportDbContext.TicketSlaStatus` maps to the migration's deterministic SQL
function. `ApplyFilters` adds one predicate on that function; count, sorting, paging and returned
status all execute in SQL. This was chosen over compiling a LINQ expression in memory or
maintaining a stored status that becomes stale as time passes. The trade-off is a SQL Server
function in the schema and potential per-row cost on large sets; verify plans/scalar UDF inlining
and consider a directly translated CASE expression for very large workloads. No performance
claim is made from a 40-ticket fixture.

The pure C# evaluator remains useful for clock-based domain tests. SQLite unit tests register a
local equivalent SQL function, which tests composition and paging but does not prove the SQL
Server implementation. Separate real SQL Server tests compare the actual function/projections
with the evaluator over all states, exact boundaries and fractional configurations, inspect
function use in WHERE, and verify filtered totalCount/totalPages. These tests also cover both
fresh migration/seeding and upgrading a starter database with an existing ticket. SQL reference
scripts intentionally remain the starter schema, not an alternative upgrade path.

### Frontend and validation

The existing SlaCell is exhaustive over all five states and remains shared by list and detail;
text conveys state independently of color. The new typed SLA parameter uses the generic filter
hook behavior without special SLA timing logic. Creation displays the server's actual priority,
owner/deadline/state and explanations, including why a ticket stayed unassigned.

The escalation form trims and validates the reason before calling the API, disables
controls while pending, displays ProblemDetails including 409, and avoids optimistic mutation.
On success the returned ticket replaces local detail state, history reloads independently and
reference-data counts refresh. A history refresh failure keeps the confirmed ticket change and
offers retry. History shows before/after priority, agent and due dates plus actor/reason/time.
Task 3 removes the interim actor input/body field and uses a bounded identity from validated
claims. Historical actor labels are retained as immutable audit values.

### Verification and deliberately untested scope

Run `dotnet test --configuration Release`, `npm run test --workspace apps/web`, `npm run lint`,
`npm run typecheck`, and `npm run build --workspace apps/web`. For real SQL/HTTP coverage, set
`SUPPORTDESK_TEST_SQL` as shown in README before dotnet test. The current verified result is 95 backend tests (six SQL/HTTP tests included), four architecture/model
checks, and 45 frontend tests. The six opt-in tests create only
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

### Limitations

A useful workflow was to write pure rules first, compile/test, then prove the database translation against
SQL Server instead of relying on SQLite or a plausible-looking LINQ query. Reusing the existing
shared SLA component was accepted; a generated test using target-typed `new(...)` in
FluentValidation.ValidateAsync was rejected by compilation because of ambiguous overloads and
changed to explicit EscalateTicketRequest construction. No application defect from that test
reached a running build. A generated file-write step also hit Windows encoding limitations;
subsequent writes used explicit UTF-8 and the affected component was rewritten and tested.

Assignment capacity and highest-ID reference generation still race across simultaneous creates;
a database-backed reservation/sequence would be follow-up work. The GUID token prevents lost
updates to a single ticket, not these aggregate-wide races.No optional report, background worker, notification,
registration, role matrix, cache, CI addition or deployment feature was added.

## Task 3: authentication and session handling

### Credential and identity choices

Existing agents are the only users, so a nullable PasswordHash on Agent is sufficient. A linked
Users/Identity schema was rejected because registration, roles and password workflows are out of
scope. Null means no credential and can never authenticate. The credential migration canonicalizes
legacy emails with trim/lowercase; new Agent construction and login input use the same rule, so
lookups can use the existing unique email index. Passwords are never normalized. Existing SQL
Server email uniqueness already treats case variants as the same address under its default
collation; login deliberately adopts a case-insensitive identifier policy.

IPasswordService is a real cryptography seam; its Infrastructure adapter uses Identity's
PasswordHasher<Agent> from Microsoft.Extensions.Identity.Core. That assembly preserves the
architecture boundary despite its Microsoft.AspNetCore.Identity namespace. The encoded salted
PBKDF2 hash includes the version/work factor. Verification honors SuccessRehashNeeded and upgrades
the hash after a successful login. Unknown or missing-credential accounts still verify a dummy
hash to reduce simple timing differences. Generic failures cover wrong email/password and
inactive agents without issuing a token. Password/hash/token values are not logged or exposed
through agent/user DTOs.

Development fixture provisioning runs independently of the existing-ticket seed guard, only for
the six known demo email addresses with null hashes. It hashes Local_Only_Demo1! at seed time,
never replaces a present hash, and never activates an inactive agent. The host invokes this
seeder only in Development. Real upgrade and Production-startup tests verify these boundaries;
there is no database-migration literal password/hash and no Production fixture provisioning.

### Tokens and central API enforcement

The token issuer and HttpContext adapter live in Presentation; Application depends only on
ITokenIssuer and ICurrentAgent. Domain/Application do not acquire JWT, HTTP or EF dependencies.
The issuer uses the injected clock and issues HS256 JWTs with subject agent ID, name, email,
issued-at, random token ID and expiry. Default lifetime is 60 minutes; key, issuer, audience and
lifetime are runtime configuration, validated at startup. A fresh random key is required, not
committed or put in a VITE setting. Validation checks signed tokens, algorithm allowlist,
signature, issuer, audience, expiry/not-before and a positive subject ID; 30 seconds of clock
skew is intentional. Token validation rechecks the agent's current active state on every request,
trading one database read for immediate rejection of deactivated/deleted agents.

A fallback authenticated-user policy closes existing and future data endpoints. Login explicitly
allows anonymous access. Development Swagger is served before authorization and has bearer input.
Missing/invalid tokens produce a shared 401 ProblemDetails response and Bearer challenge; request
validation remains 400. No registration, OAuth/SSO, refresh or role system was introduced.

EscalatedBy is now agent:<id> from the validated subject, which is stable and fits the existing
100-character column even if a person's name/email is long. A client-supplied escalatedBy property
is not bound to the request DTO and cannot affect attribution. Existing historical actor strings
are left intact. A real HTTP test deliberately sends a spoofed administrator name and checks the
recorded token-derived actor. Domain history still stores its audit snapshot, not a live user
name that could change later.

### Browser session and routing

The shared session store persists token/expiry in sessionStorage, retaining only validated user
data in memory. On reload AuthProvider calls me and the route guard waits before rendering any
private content. Login fields are labeled and validated, preserve significant password spaces,
and show pending/rejected states. Successful login returns to the requested internal path/query;
external/protocol-relative/backslash/control-character destinations are rejected. User name and
logout are in the existing shell. No visual redesign was attempted.

The central API client adds Authorization, preserving arbitrary caller headers. Any 401 clears
the matching session, which makes the protected route redirect to login and unmount private data.
Login failure stays on the public login page with its error. A delayed response from an old token
cannot erase a newer successful login. Malformed/expired saved sessions are discarded, and a
storage access failure falls back to memory. Temporary me/network failures offer retry rather
than treating server unavailability as invalid credentials. Logout removes storage and memory;
it is not a server token-revocation mechanism. Other tabs have independent sessionStorage.

This storage choice is simple for the assessment and survives reload within one tab; it remains
vulnerable to JavaScript/XSS and a stolen bearer token. Production would use HTTPS, appropriate
key management/rotation and a deliberate browser-session architecture (for example a BFF with
HttpOnly cookies and CSRF protections), plus abuse controls and appropriate identity workflows.
Those changes are documented, not added beyond scope. Deactivation invalidates current tokens;
password changes alone do not revoke them before expiry. No refresh/revocation/lockout/rate-limit
feature was added because these are explicitly outside the assignment.

### Verification

Tests cover generic failures/no token, real salted hash verification, significant whitespace,
rehashing, startup key/lifetime validation, claim-derived actors, client headers, protected-route
redirect/return, pending/rejected login, restored-session validation, logout, invalid storage,
401 redirect and an old-request/new-login race. SQL/HTTP tests additionally audit every existing
endpoint family, valid/invalid JWTs (signature, issuer, audience, lifetime, algorithm and subject),
inactive agents, spoofed attribution, legacy credential backfill, preserved changed hashes after
restart, and absence of Production fixture seeding. Task 2's real API tests now log in and verify
its behavior under central authentication. Six opt-in tests use unique disposable databases;
ordinary suites remain database-independent.

The API test hosts disable only the Windows Event Log provider, retaining console logs, because
restricted Windows test accounts cannot write the system log. Application authentication and
authorization are unchanged in tests. No elevation or writes to protected OS log locations are
needed for this verification. No browser visual audit, load/brute-force exercise, external identity
provider or production deployment was tested.

Tests caught a real generated session-store bug: malformed JSON was rejected but not removed. The catch path now clears it and a regression asserts storage removal. Compilation also caught an encoding problem when a generated migration BOM was read using the Windows default encoding; the migration was corrected to UTF-8. Official
[JWT validation guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0),
[PasswordHasher documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.passwordhasher-1?view=aspnetcore-10.0),
and [Swashbuckle bearer configuration](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/docs/configure-and-customize-swaggergen.md)
were consulted; generated code was checked with compilation, architecture tests and real request
behavior rather than accepted on appearance.

## Task 4

The API container, anonymous database-health endpoint and container verification remain pending.
Task 3 introduces no baked-in JWT secret; Task 4 must pass the key and connection/environment
settings at runtime and explicitly exempt health from the fallback policy.
