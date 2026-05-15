---
name: coding-standards
description: |
  Project-wide coding standards for FlowBoard. **Always** consult this skill
  before writing or modifying code in this repo. Covers .NET/Dapper/SQL on
  the backend and React/TypeScript on the frontend.
---

# FlowBoard Coding Standards

These rules are mandatory for every change. They were extracted from a 2026-05
audit of the codebase. When in doubt, follow them; if a rule blocks progress,
flag it in the PR description rather than silently ignoring it.

---

## A. Backend (.NET / Dapper / Postgres)

### A1. Authorization is per-endpoint
Every project-scoped endpoint **must** check membership before reading or
writing. Use `ProjectAuthorizer.IsMemberAsync(projectId, User.GetUserId())`.
Endpoints scoped by issue/epic/sprint/column id must first resolve the
parent `project_id` (via `ProjectQueries.IssueProjectId`,
`EpicProjectId`, etc.) and then call `IsMemberAsync`. Owner-only operations
use `IsOwnerAsync`.

### A2. All SQL lives in `*Queries.cs`
Never inline a SQL string inside a controller. Add it as
`public const string XxxByYyy = @"..."` in the relevant
`src/FlowBoard.Core/Data/Queries/*Queries.cs`. Controllers read the
constant and execute via Dapper. This keeps queries reviewable and
prevents accidental string concatenation.

### A3. Parameterized queries only
All Dapper calls must use `@ParameterName` placeholders bound to a
parameters object. **Never** interpolate user input into a SQL string,
not even for `ORDER BY` columns — use a whitelist instead.

### A4. Dapper record types match Postgres reader types
- `DATE` → `DateTime`
- `TIMESTAMPTZ` → `DateTime`
- `COUNT(*)` / `COALESCE(SUM(int4),0)` → `long`
- Window `SUM(...) OVER (...)` over int → `decimal`
- `ROUND(x, n)` → `decimal`

Request DTOs may keep `DateOnly` because the registered type handlers
in `DapperSetup.Initialize()` translate to `DbType.Date`. Result records
always use the reader-native type.

### A5. Validate at the API edge
All request DTOs are `record` types in `Api/Models/Dtos.cs` with
`System.ComponentModel.DataAnnotations` attributes. Required attrs:

- `[Required]` on non-nullable string inputs
- `[StringLength(n)]` on every user-supplied string (mirror the DB cap)
- `[Range(min, max)]` on numeric bounds (e.g. `StoryPoints`, `Position`)
- `[RegularExpression(ValidationPatterns.X)]` for enum-like fields.
  Patterns live in `ValidationPatterns` (Priority, SprintStatus, HexColor)
  and **must mirror the Postgres `CHECK` constraints**.
- `[EmailAddress]` on email fields.

ASP.NET returns 400 ProblemDetails automatically when these fail —
controllers don't need to check `ModelState.IsValid` explicitly.

### A6. Schema parity with API
Every `CHECK` constraint in `schema.sql` must have a matching
`[RegularExpression]`/`[Range]` attribute in `Dtos.cs`. Validation lives
in **two** places by design — DB protects against bad direct writes, API
protects against confusing 500s.

### A7. Indexes for every FK and every WHERE filter
Add `CREATE INDEX IF NOT EXISTS idx_<table>_<col> ON <table>(<col>)` for
every foreign key column and any column that appears in a WHERE clause
of a hot-path query. Run `EXPLAIN ANALYZE` on new queries that scan
> 1k rows; verify index usage.

### A8. Errors do not leak internals
The `UseExceptionHandler` middleware in `Program.cs` logs the exception
and returns `{ error: "internal_error" }` with HTTP 500. Never `try`/
`catch` to wrap an exception in `Ok(...)` or expose
`ex.StackTrace` / `ex.Message` to the client. Use specific status codes:
**201** Created, **204** NoContent, **400** validation, **401**
unauthenticated, **403** forbidden, **404** not found, **409** conflict.

### A9. Connection lifetime
`DbConnectionFactory` returns a fresh `NpgsqlConnection` per call.
Always `using var c = _db.Create();` — never store one as a field.
Multi-statement writes (insert + insert + insert) wrap in
`await c.BeginTransactionAsync()` + `CommitAsync()` (see
`ProjectsController.Create`). Single-statement writes don't need a
transaction.

### A10. Secrets never in source
Use `Program.ResolveSecret(configKey, envKey, devFallback)` for any
secret. The function order is: env var → `IConfiguration` → dev fallback
(only when `IsDevelopment()`). In non-dev it must throw if absent.
`appsettings.Development.json` is gitignored.

### A11. CORS allowlist, not wildcards
`Program.cs` configures CORS with explicit origins, headers, and
methods. Do not add `.AllowAnyHeader()` or `.AllowAnyMethod()`.

### A12. Async all the way
Every controller action is `async Task<IActionResult>`. Every Dapper
call uses the `Async` overload. Never use `.Result`, `.Wait()`, or
`.GetAwaiter().GetResult()`.

### A13. Empty-Guid sentinel for "clear field" updates
On `UpdateXxxRequest` for nullable FK fields (`epic_id`, `sprint_id`,
`assignee_id`):
- `null` → leave value unchanged (keep current)
- `Guid.Empty` (`00000000-...`) → clear the field to `NULL`
- any other Guid → set to that value

The SQL uses `CASE WHEN @X IS NULL THEN col WHEN @X = '00000000-...'
THEN NULL ELSE @X END`. **Never** use `COALESCE(@X, col)` for these —
it makes "clear" impossible.

---

## B. Frontend (React / TypeScript)

### B1. No `any`
Forbid `as any` casts and `catch (e: any)` blocks. For thrown errors
use `catch (err)` and pass to `getApiError(err, fallback)` from
`src/types/api.ts`. For untyped third-party data, declare an
`interface` in `src/types/index.ts` first.

### B2. Use `useQuery` for all GETs
TanStack Query owns server-state caching, retry, and request
cancellation. Manual `useEffect` + `api.get` is permitted only for one-
off resource hydration (e.g. issue detail panel) and must include the
`let cancelled = false` cleanup pattern.

### B3. Stable query keys
Format: `['<resource>', <id>]` — e.g. `['board', projectId]`,
`['epics', projectId]`, `['sprints', projectId]`,
`['velocity', sprintId]`, `['sprint-issues', sprintId]`. Mutations
must `qc.invalidateQueries({ queryKey: [...] })` for every key whose
data they affect (creating an issue invalidates `board` AND
`sprint-issues` if a sprint is set).

### B4. `enabled` guard on dependent queries
Any `useQuery` whose key depends on another query's data must use
`enabled: !!dep`. The fetch fn may then assert with `!`:
`enabled: !!project, queryFn: () => api.get(\`/projects/${project!.id}/...\`)`.

### B5. Modals must close on Escape
Every modal calls `useEscapeKey(onClose)` (from `src/lib/useEscapeKey.ts`)
in addition to the overlay-click handler. Click handlers on the inner
panel use `e.stopPropagation()`.

### B6. Form submit guard
Set a local `loading`/`busy` flag in the submit handler **before**
the async call, and disable the submit button on `loading`. This
prevents double-clicks from creating duplicate records.

### B7. Empty-Guid sentinel mirrored
When the user "clears" an epic/sprint dropdown in `IssueModal`, send
`EMPTY_GUID = '00000000-0000-0000-0000-000000000000'`. On
`CreateIssueModal`, send plain `null` (creates can't have a "leave
unchanged" semantic). Keep the constant identical to backend
`Guid.Empty.ToString()`.

### B8. Type all axios responses
`api.get<{ epics: EpicWithProgress[] }>(...)` not `api.get(...)`. The
generic flows through into `.data` so consumers get IntelliSense.

### B9. Tailwind class vocabulary
Stick to the existing tokens: `panel`, `input`, `btn-primary`,
`btn-ghost`, `mono`, `heading`, `text-text-dim`, `text-text-muted`,
`text-accent`, `text-priority-low|critical`, `bg-bg-panel|subtle`,
`border-border`. Don't invent new utilities; extend
`tailwind.config.js` if a new token is genuinely needed.

### B10. Auth & secrets
Tokens persist in localStorage via the Zustand `useAuthStore` and are
attached by the axios request interceptor. **Never** `console.log` a
token, password, or PII. **Never** use `dangerouslySetInnerHTML`.

### B11. Environment access
Reference env vars only via `import.meta.env.VITE_*`. The
`vite-env.d.ts` triple-slash directive provides the types.

### B12. Component size budget
Components stay under ~200 lines. If a page exceeds that, extract
sub-components into `src/components/` and keep the page as
orchestration only.

### B13. Surface backend errors
Don't write try/catch blocks just to swallow API errors — the axios
response interceptor in `src/lib/api.ts` already toasts them via
`lib/toast.tsx`. Wrap a call only when you need to control local UI
state (reset a form, keep a modal open, etc.); when you also render the
error inline, pass `{ silent: true }` on the request config so the user
doesn't see the same message twice. Map known error codes to user copy
through `humanizeApiError(code)`.

### B14. Tests are part of the change
Every code change ships with tests:

- **New feature** → add a structural test in
  `tests/FlowBoard.Tests/` pinning the SQL contract (filter shape,
  ordering, sentinels, pagination), **and** an integration test in
  `tests/FlowBoard.Tests/Integration/` exercising the endpoint
  end-to-end through `WebApplicationFactory<Program>`. See
  `IssuesListFlowTests` and the `ListByProject_*` tests in
  `CommentsLabelsActivityQueryTests` for the canonical pattern.
- **Behaviour change** → update the existing test first so it fails
  against the old code, then make the fix.
- **Bug fix** → add a regression test that would have caught it.
- Run `dotnet test FlowBoard.sln --nologo` (with `FLOWBOARD_TEST_DB`
  set, see `CLAUDE.md` → Testing). All tests must pass before declaring
  the change done.

---

## C. SQL / migrations

- Schema lives in `schema.sql`. Edits must be **idempotent** — wrap new
  indexes in `CREATE INDEX IF NOT EXISTS`, new constraints in a
  `DO $$ … EXCEPTION WHEN duplicate_object THEN NULL; END $$` block.
- After editing `schema.sql`, apply the delta to the local DB with
  `psql -U postgres -d flowboard -f schema.sql` or by running just the
  changed statement.
- Document non-obvious `ON DELETE` behavior with a comment:
  `-- Epic deletion sets issues.epic_id NULL (issues survive)`.

---

## D. Pre-commit checklist

Before declaring a change complete:

1. `dotnet build FlowBoard.sln -nologo` is clean (0 warnings, 0 errors).
2. `cd client && npx tsc --noEmit` is clean.
3. `dotnet test FlowBoard.sln --nologo` is green (set
   `FLOWBOARD_TEST_DB` first — see `CLAUDE.md` → Testing). New
   feature ⇒ new structural + integration tests (B14).
4. Smoke test from `.claude/commands/test-endpoints.md` returns 2xx for
   every step.
5. If you added an endpoint: it has the auth check (A1) and validation (A5).
6. If you added a `Priority`/`Status`/enum field: the
   `[RegularExpression]` and the Postgres `CHECK` agree (A6).
7. If you added a query that filters by a column: that column has an
   index in `schema.sql` (A7).

---

## E. Done — formerly out-of-scope

These were deferred during the initial audit and have since been
implemented. They're now standards: don't regress them.

- **Refresh token rotation + revocation.** `refresh_tokens` table stores
  every issued jti with `revoked_at` / `replaced_by`. `/auth/refresh`
  rotates (revokes old, mints new). Replay of a revoked token revokes
  the entire chain for that user (theft response). `/auth/logout`
  revokes the presented token.
- **Rate limiting** on `/auth/*`. `auth-strict` (5/min) on login/refresh,
  `auth-loose` (10/5min) on register, both partitioned by client IP.
  Use `[EnableRateLimiting("auth-strict|auth-loose")]` on any new
  auth-adjacent endpoint.
- **`FlowBoard.Tests` xUnit project** lives at `tests/FlowBoard.Tests`.
  Run with `dotnet test FlowBoard.sln`. Two layers:
  1. Unit / structural — Dapper type handlers, JwtService round-trip,
     validation patterns, SQL shape (`QueryShapeTests`,
     `CommentsLabelsActivityQueryTests`), DTO attributes,
     `MentionParserTests`.
  2. End-to-end functional in `tests/FlowBoard.Tests/Integration/`.
     `PostgresFixture` drops + recreates a `flowboard_test` database
     and applies `schema.sql`; `FlowBoardFactory : WebApplicationFactory<Program>`
     boots the real ASP.NET pipeline against it; per-test
     `IntegrationTestBase` TRUNCATEs row data and exposes
     `RegisterUserAsync` / `CreateProjectWithIssueAsync` /
     `AddProjectMemberAsync` helpers. To run them, set the env var
     before `dotnet test`:
     ```powershell
     $env:FLOWBOARD_TEST_DB = 'Host=localhost;Port=5432;Username=postgres;Password=...;Database=postgres'
     ```
  Add tests for any new query/handler/service — prefer functional tests
  in `Integration/` for behavioural changes, structural tests for SQL
  shape regressions.
- **ESLint + Prettier** in `client/`. Scripts: `npm run lint`,
  `lint:fix`, `format`, `format:check`. Config: `.eslintrc.cjs`,
  `.prettierrc.json`, `.prettierignore`.
- **Husky + lint-staged** at the repo root. Pre-commit runs
  `lint:fix` + `prettier --write` on staged client files. Setup is in
  the root `package.json`.
- **Pagination** on list endpoints. Use `[FromQuery] PageRequest page`
  and return `Paged<T>(items, total, skip, take)`. Default
  `skip=0, take=50`, max take=100. Each list query has a matching
  `Count*` query.
- **Serilog structured logging + correlation IDs.** Configured in
  `Program.cs` via `UseSerilog`. `CorrelationIdMiddleware` reads or
  generates `X-Correlation-Id`, propagates it through `LogContext`,
  and echoes it on the response. Don't `Console.WriteLine` —
  inject `ILogger<T>` and let Serilog handle output.
- **HTTPS redirect + HSTS** enabled when `!IsDevelopment()`. Railway
  terminates TLS so the redirect is mostly a defence-in-depth header.

## F. Still out-of-scope

- Email verification + password reset flow.
- 2FA / MFA.
- WebSocket/SSE for live board updates.
- Distributed rate limiting (current limiter is per-instance memory).
- Test coverage target ≥60% (now ~106 tests across unit + functional;
  expand functional coverage to permissions on all controllers).
