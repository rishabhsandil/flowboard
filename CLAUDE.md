# FlowBoard — Claude project memory

> Project context loaded automatically by Claude Code. Keep entries terse.
> User/global memory lives in `~/.claude/CLAUDE.md`; this file is repo-scoped.

## ⚠️ Read this first

**Before writing any code in this repo, read
[`.claude/skills/coding-standards/SKILL.md`](.claude/skills/coding-standards/SKILL.md).**
It defines the mandatory rules for authorization, validation, Dapper type
mapping, error handling, modal UX, query keys, and the empty-Guid sentinel.

## Stack

- **API**: ASP.NET Core 10 (`net10.0`), C# 14, Dapper 2.1.66, Npgsql 9.0.2, BCrypt.Net-Next, JWT (access + refresh).
- **DB**: Postgres 18 locally (`flowboard` / user `postgres`); Neon Postgres in prod. Raw SQL only — no EF.
- **Client**: React 18 + TS + Vite + Tailwind + Zustand + TanStack Query + `@dnd-kit` + Recharts on `http://localhost:5173`.
- **API URL**: `http://localhost:8080` locally. Frontend reads `VITE_API_URL=http://localhost:8080/api`.

## Layout

```
src/FlowBoard.Api/        Controllers, Auth, Program.cs, launchSettings.json, appsettings*.json
src/FlowBoard.Core/       DbConnectionFactory, DapperSetup, Models/Domain.cs, Data/Queries/*.cs
client/                   Vite + React SPA
schema.sql                Full DB schema (idempotent enough for local re-apply)
docs/queries.md           Annotated SQL
```

## Build / run (Windows)

`dotnet` is **not on PATH**. Always invoke via full path:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build FlowBoard.sln -nologo
& "C:\Program Files\dotnet\dotnet.exe" run --project src/FlowBoard.Api --no-build
```

Before rebuild, kill any running API to release the file lock:

```powershell
Get-Process FlowBoard.Api -ErrorAction SilentlyContinue | Stop-Process -Force
```

`launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development` and binds `http://localhost:8080`.

## Local Postgres

- `psql` at `C:\Program Files\PostgreSQL\18\bin\psql.exe`
- DB `flowboard`, user `postgres`, password lives in `appsettings.Development.json` (gitignored).
- Schema: `psql -U postgres -d flowboard -f schema.sql`.

## Dapper + Npgsql gotchas (hard-learned)

Dapper 2.1.66 record materialization is strict — record constructor parameter **types must exactly match** the Npgsql reader column types. There is no implicit `int↔long` or `DateTime↔DateOnly` conversion.

| Postgres column / expression | CLR type returned by Npgsql |
|---|---|
| `DATE` | `DateTime` (NOT `DateOnly`) |
| `TIMESTAMPTZ` | `DateTime` |
| `COUNT(*)`, `COALESCE(SUM(int4),0)` | `long` |
| `SUM(SUM(int4)) OVER (...)` window | `decimal` (numeric) |
| `ROUND(x, 1)` | `decimal` |

Rules:

1. Result records (e.g. `Epic`, `Sprint`, `EpicWithProgress`, `SprintVelocityRow`) use `DateTime`/`long`/`decimal` to match the reader.
2. Request DTOs may keep `DateOnly` — `DapperSetup.Initialize` registers `SqlMapper.TypeHandler<DateOnly>` and `<DateOnly?>` so they bind cleanly as `DbType.Date` parameters.
3. Snake_case → camelCase mapping is enabled via `DefaultTypeMap.MatchNamesWithUnderscores = true`.
4. When a query returns fewer columns than the canonical record, add a forwarding constructor on the record (see `Project` for the pattern with optional `Role`).
5. Diagnose errors of the form `A parameterless default constructor or one matching signature (...types...) is required for ... materialization` by reading the type list in the message — those are the **actual** reader types. Adjust the record to match.

## Auth / config

- Secrets resolved by `Program.ResolveSecret` in this order: env var → `IConfiguration` → dev fallback (only when `IHostEnvironment.IsDevelopment()` → throw otherwise).
- `JwtService` ctor: `(IConfiguration config, IHostEnvironment env)`. Both `Secret` and `RefreshSecret` must be ≥ 64 chars in non-dev.
- Production env vars: `ConnectionStrings__Default`, `Jwt__Secret`, `Jwt__RefreshSecret`.

## Smoke test (PowerShell)

After API is up on `:8080`, see `.claude/commands/test-endpoints.md` for a one-shot register → project → epic → sprint → velocity check.

## Testing

- **Unit / structural** tests run with no external dependencies:
  `& "C:\Program Files\dotnet\dotnet.exe" test FlowBoard.sln --nologo`.
- **Functional** tests in `tests/FlowBoard.Tests/Integration/` boot the
  real Program pipeline (`WebApplicationFactory<Program>`) against a
  local Postgres. They require the `FLOWBOARD_TEST_DB` env var — a
  libpq connection string pointing at a superuser / admin DB. The
  `PostgresFixture` drops + recreates `flowboard_test` and applies
  `schema.sql` per run. Set it once per shell:

  ```powershell
  $env:FLOWBOARD_TEST_DB = 'Host=localhost;Port=5432;Username=postgres;Password=...;Database=postgres'
  ```

  Don't override `Jwt:Secret` / `Jwt:RefreshSecret` from the test
  factory — `Program.cs` resolves them eagerly during builder build,
  while `JwtService` resolves them lazily, and the two paths can
  desync silently. Let the dev fallback fire instead.
- **Schema cascades to remember** when writing tests:
  `activities.issue_id` is `ON DELETE CASCADE` (deleting an issue wipes
  its prior log; only the post-delete `issue_deleted` row — with
  `issue_id = NULL` — survives on the project feed). The mentions PK
  column is `mentioned_user_id`, not `user_id`.

## Conventions

- **Always add/update tests with code changes.** New feature → at least one structural test pinning the new SQL/contract plus one integration test through `WebApplicationFactory`. Behaviour change → update the existing tests so they fail before the fix and pass after. PRs without test updates are incomplete.
- **Always update docs with code changes.** Touch `README.md`, `docs/FEATURES.md`, `docs/queries.md`, and this file as needed — only what's necessary, not exhaustive.
- Raw SQL lives in `src/FlowBoard.Core/Data/Queries/*.cs` as `const string` fields. Controllers `using var c = _db.Create();` then `QueryAsync<T>` / `ExecuteAsync`.
- Authorization is per-controller via `ProjectAuthorizer.IsMemberAsync(projectId, userId)`. Every project-scoped endpoint must check it.
- New endpoints: define query in `Data/Queries`, add domain record (matching reader types), add request DTO, wire controller, then update `client/src/api/*` and the relevant Zustand store / query hook.
- **Optional-filter SQL pattern** (see `IssueQueries.ListByProject`): `WHERE (@X IS NULL OR col = @X)` for each filter. For nullable FKs, accept the empty-Guid sentinel `'00000000-0000-0000-0000-000000000000'` to mean "filter to NULL". Same sentinel is used by `IssueQueries.Update` to clear an FK.
- **Frontend error UX**: `client/src/lib/api.ts` toasts every non-401 backend error via `lib/toast.tsx`. Forms that render the error inline pass `{ silent: true }` on the request config (axios module-augmented). Use `humanizeApiError(code)` to map known error codes to user-readable copy.

## Post-implementation checklist (mandatory after every code change / feature)

Run these steps in order every time code is written or modified:

### 1 — Backend tests
```powershell
# Unit + structural tests (no DB needed):
& "C:\Program Files\dotnet\dotnet.exe" test FlowBoard.sln --nologo

# Integration tests (need real Postgres — set the env var once per shell):
$env:FLOWBOARD_TEST_DB = 'Host=localhost;Port=5432;Username=postgres;Password=<password>;Database=postgres'
& "C:\Program Files\dotnet\dotnet.exe" test FlowBoard.sln --nologo
```

All tests must be **green** before continuing.

### 2 — Frontend lint
```powershell
Set-Location client
npm run lint
```

Fix every ESLint error before continuing.

### 3 — Playwright e2e tests
```powershell
Set-Location client
npx playwright test
```

The suite lives in `client/tests/`. Tests hit the running local stack
(`http://localhost:8080` API, `http://localhost:5173` client). If the API is
not running, start it first (see **Build / run** above). All tests must pass.

### 4 — Docs
After every feature, update as needed (only the sections that actually changed):
- `README.md` — user-facing features, new routes, env vars
- `docs/FEATURES.md` — detailed feature descriptions
- `docs/queries.md` — any new or changed SQL
- `CLAUDE.md` — any new conventions or gotchas discovered

## Don't

- Don't introduce EF Core. Raw SQL is the showcase.
- Don't add `DateOnly` to result records — it will compile but crash at runtime on materialization.
- Don't commit `appsettings.Development.json` (gitignored).
- Don't use `--no-verify` or amend pushed commits.
