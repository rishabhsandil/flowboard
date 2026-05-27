# FlowBoard

A developer-native project management tool — Kanban boards, epics, sprints, sprint planning, velocity analytics, and cumulative flow diagrams. Built by **Rishabh Sandil** as a portfolio piece to publicly showcase the .NET Core / C# / SQL skills used in his day job.

> **Live demo:** https://flowboard.vercel.app  _(coming soon)_
> **API:** https://flowboard-api.up.railway.app/swagger

![FlowBoard board view](docs/screenshot.png)

---

## What it is

FlowBoard is a ZenHub-inspired project management tool: a fast Kanban board with epics, sprint planning, velocity charts, and cumulative flow diagrams. The frontend is a React SPA on Vercel; the backend is an ASP.NET Core 10 Web API on Railway; the database is Neon Postgres. The API uses **raw SQL** via Dapper + Npgsql — there is no Entity Framework, intentionally, so the SQL is the showcase.

---

## Features

### Board & Issues
- Kanban board with drag-and-drop (`@dnd-kit`) and optimistic UI
- Project-wide issue list with status / priority / epic / sprint / assignee / label / title-search filters
- Bulk actions on issues: move to sprint, apply label, close, reopen, delete
- Click any label chip on a card or row to deep-link into Issues filtered by that label
- Sub-issues (parent/child relationship) with nesting on the issue modal
- **Issue dependencies** — directed `blocks` / `relates` graph between issues in the same project, rendered in the IssueModal sidebar as "Blocks", "Blocked by", and "Related"; 1-hop reverse-blocks cycles are rejected and cross-project links are forbidden
- **Real-time board updates** — SignalR `BoardHub` at `/hubs/board` with per-project groups; every audited mutation publishes a typed event (`issue_created`, `column_updated`, `comment_added`, …) that connected clients consume via a `useBoardSocket` hook to invalidate the right TanStack Query keys without a full page refresh
- Issue comments with `@name` mentions resolved against project members
- Markdown rendering in issue descriptions and comments

### Epics & Sprints
- Epics with color-coded pills, progress bars (issues closed, points completed), and a dedicated detail page
- Sprints with start/end dates and status (`planned` / `active` / `completed`)
- **Sprint planning view** (`/p/:slug/plan`) — drag issues between a backlog column and the selected sprint; drop back to unassign

### Reporting
- **Velocity chart** — per-sprint bar + cumulative line (Recharts), only completed sprints
- **Burndown chart** — remaining story points per day in the active sprint
- **Cumulative flow diagram** (`/p/:slug/reports/cfd`) — stacked area chart of per-column issue counts over time (7 / 14 / 30 / 60 / 90 day range); daily snapshots accumulate automatically on each visit

### Collaboration & Profile
- Per-issue and per-project activity log (append-only, filterable feed at `/p/:slug/activity`)
- Project member invitations + role management (owner / member) with last-owner protection
- Owner-only **delete project** under **Settings → General** with a type-the-word-`delete` confirmation; cascades to every board / issue / epic / sprint / comment / activity row
- User profile page (name + avatar URL) with current-password-gated password change that revokes other refresh tokens
- Forgot / reset password flow — opaque `/auth/forgot` (always 200, no user enumeration; real work runs in a background task so response time can't distinguish known vs unknown emails) issues a single-use SHA-256-hashed token (1 h TTL) and dispatches it through a pluggable `IEmailSender` (log in dev/tests, Resend HTTP API in prod). Resends invalidate prior outstanding tokens and honour a per-email cooldown. `/auth/reset` rotates the password, revokes every outstanding refresh token, and fires a post-reset notification email so the account holder is alerted if a takeover just occurred. A periodic background sweep prunes stale token rows.

### Settings & UX
- Project labels managed under **Settings → Labels**; legacy `/labels` URL redirects
- Global keyboard command palette (`Cmd/Ctrl+K` or `?`) for project navigation and project switching
- Global toast notifications surface backend errors with humanized copy; inline form errors opt out with `silent: true`
- Custom confirm dialog replaces every `window.confirm`

### Security
- JWT auth — short-lived access tokens (15 min) + rotating refresh tokens (30 days), BCrypt password hashing
- CSRF protection middleware — requires `X-Requested-With: XMLHttpRequest` on all mutating requests; `/api/auth/` paths exempt
- Security headers on every response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`
- Swagger UI in production for API exploration

---

## Architecture

```
┌─────────────────┐    HTTPS     ┌────────────────────────┐    SQL/TLS    ┌──────────────┐
│  React 18 SPA   │ ───────────▶ │  ASP.NET Core 10 API   │ ────────────▶ │ Neon Postgres│
│  Vite · TS      │              │  Dapper · Npgsql · JWT │               │  (free tier) │
│  Vercel         │              │  Railway (Nixpacks)    │               │              │
└─────────────────┘              └────────────────────────┘               └──────────────┘
```

```
FlowBoard/
├── FlowBoard.sln
├── src/
│   ├── FlowBoard.Api/        # ASP.NET Core 10 Web API (controllers, middleware, auth)
│   └── FlowBoard.Core/       # DB access + domain models + raw SQL queries
├── tests/
│   └── FlowBoard.Tests/      # xUnit integration tests (WebApplicationFactory + real Postgres)
├── client/                   # React + Vite + Tailwind SPA
│   └── tests/                # Playwright e2e tests
├── schema.sql                # Full DB schema (idempotent)
├── docs/
│   ├── queries.md            # Annotated SQL queries
│   └── FEATURES.md           # Detailed feature checklist
└── scripts/
    └── daily-agent.ps1       # Automated nightly Claude Code agent (Task Scheduler)
```

---

## Tech Stack

| Layer | Tech |
|---|---|
| Frontend | React 18, TypeScript, Vite, Tailwind, React Router 6, Zustand, TanStack Query, `@dnd-kit`, Recharts, Framer Motion |
| Backend | ASP.NET Core 10 Web API, C# 14, Dapper, Npgsql, BCrypt.Net-Next |
| Database | PostgreSQL (Neon free tier) |
| Testing | xUnit + WebApplicationFactory (backend), Playwright (e2e) |
| Hosting | Vercel (web), Railway (API) — total cost: $0 |

---

## SQL showcase

The API is intentionally written without an ORM. Four queries that do the heavy lifting:

### 1. Board view in one round-trip — `json_agg` + `FILTER`
Returns every column and its ordered issues in a single statement. `json_agg(... ORDER BY i.position)` sorts inside the aggregate; `FILTER (WHERE i.id IS NOT NULL)` ensures empty columns return `[]` not `[null]`.

```sql
SELECT c.id AS column_id, c.name AS column_name, c.position,
  COALESCE(
    json_agg(json_build_object(
      'id', i.id, 'title', i.title, 'priority', i.priority,
      'story_points', i.story_points, 'position', i.position,
      'assignee_id', i.assignee_id, 'epic_id', i.epic_id,
      'epic_color', e.color, 'epic_title', e.title
    ) ORDER BY i.position) FILTER (WHERE i.id IS NOT NULL),
    '[]'
  ) AS issues
FROM columns c
LEFT JOIN issues i ON i.column_id = c.id
LEFT JOIN epics  e ON e.id = i.epic_id
WHERE c.board_id = @boardId
GROUP BY c.id, c.name, c.position
ORDER BY c.position;
```

### 2. Velocity with running total — window function over aggregate
`SUM(...) OVER (ORDER BY start_date ROWS UNBOUNDED PRECEDING)` produces the cumulative running total in the same pass as the per-sprint sum. Avoids a self-join or correlated subquery.

```sql
SELECT s.id, s.name, s.start_date, s.end_date,
  COALESCE(SUM(i.story_points), 0) AS points_completed,
  SUM(COALESCE(SUM(i.story_points), 0))
    OVER (ORDER BY s.start_date ROWS UNBOUNDED PRECEDING) AS cumulative_points
FROM sprints s
LEFT JOIN issues i
  ON i.sprint_id = s.id AND i.closed_at IS NOT NULL
  AND i.closed_at <= s.end_date + INTERVAL '1 day'
WHERE s.project_id = @projectId AND s.status = 'completed'
GROUP BY s.id, s.name, s.start_date, s.end_date
ORDER BY s.start_date;
```

### 3. Epic progress — `NULLIF` for safe division, conditional `SUM`
`NULLIF(COUNT(i.id), 0)` makes the percentage `NULL` (not a DB error) for epics with no issues. The conditional `SUM(CASE WHEN closed_at ...)` avoids a second join just for the completed-points subtotal.

```sql
SELECT e.id, e.title, e.color, e.due_date,
  COUNT(i.id)         AS total_issues,
  COUNT(i.closed_at)  AS closed_issues,
  ROUND(COUNT(i.closed_at)::NUMERIC / NULLIF(COUNT(i.id), 0) * 100, 1) AS percent_complete,
  COALESCE(SUM(i.story_points), 0) AS total_points,
  COALESCE(SUM(CASE WHEN i.closed_at IS NOT NULL THEN i.story_points ELSE 0 END), 0) AS completed_points
FROM epics e
LEFT JOIN issues i ON i.epic_id = e.id
WHERE e.project_id = @projectId
GROUP BY e.id, e.title, e.color, e.due_date
ORDER BY e.created_at;
```

### 4. CFD snapshot upsert — `ON CONFLICT DO UPDATE` for idempotent daily accumulation
Called on every visit to `/reports/cfd`. Safe to call multiple times per day — subsequent calls update stale counts rather than inserting duplicates.

```sql
INSERT INTO board_snapshots (project_id, column_id, column_name, issue_count, snapped_at)
SELECT c.project_id, c.id, c.name, COUNT(i.id), CURRENT_DATE
FROM columns c
LEFT JOIN issues i ON i.column_id = c.id AND i.closed_at IS NULL
WHERE c.project_id = @ProjectId
GROUP BY c.project_id, c.id, c.name
ON CONFLICT (project_id, column_id, snapped_at)
DO UPDATE SET issue_count = EXCLUDED.issue_count, column_name = EXCLUDED.column_name;
```

The full annotated set is in [docs/queries.md](docs/queries.md).

---

## Indexing decisions

| Index | Why |
|---|---|
| `idx_issues_project_id` | Every project-scoped query filters here |
| `idx_issues_column_id` | Board view groups by column |
| `idx_issues_sprint_id` | Sprint detail and velocity queries |
| `idx_issues_epic_id` | Epic progress aggregations |
| `idx_issues_closed_at` | Velocity / completed-issue filters |
| `idx_columns_board_id` | Board fetch by board id |
| `idx_sprints_project_id` | Sprint list + velocity |
| `idx_epics_project_id` | Epic list |
| `idx_board_snapshots_project_date` | CFD historical data lookup |
| `idx_issue_dependencies_depends_on` | Reverse-direction (incoming) edge lookups for the IssueModal "blocked by" panel |

---

## Local setup

```powershell
# 1. Database
psql -U postgres -d flowboard -f schema.sql

# 2. API (http://localhost:8080)
# dotnet is not on PATH — use full path:
& "C:\Program Files\dotnet\dotnet.exe" run --project src/FlowBoard.Api

# 3. Web (http://localhost:5173)
cd client
npm install
npm run dev
```

Required env vars:

```env
# API — set in appsettings.Development.json locally, Railway in production
ConnectionStrings__Neon=postgresql://...
Jwt__Secret=<64-char random>
Jwt__RefreshSecret=<different 64-char random>

# Web — .env.local locally, Vercel in production
VITE_API_URL=http://localhost:8080/api
```

### Running tests

```powershell
# Backend (unit + integration — integration needs real Postgres):
$env:FLOWBOARD_TEST_DB = 'Host=localhost;Port=5432;Username=postgres;Password=...;Database=postgres'
& "C:\Program Files\dotnet\dotnet.exe" test FlowBoard.sln --nologo

# Playwright e2e (requires both servers running):
cd client
npx playwright test
```

---

## Roadmap

- GitHub Issues sync (GitHub OAuth + REST)
- Email notifications (assigned, mentioned, sprint starts)
- Saved board filters
- Per-column WIP limits
- Workflow rules (auto-close on column move, auto-assign on status change)
- Public project sharing (read-only link)
