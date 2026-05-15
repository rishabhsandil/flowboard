# FlowBoard

A developer-native project management tool — Kanban boards, epics, sprints, and velocity analytics. Built by **Rishabh Sandil** as a portfolio piece to publicly showcase the .NET Core / C# / SQL skills used in his day job.

> **Live demo:** https://flowboard.vercel.app  _(coming soon)_
> **API:** https://flowboard-api.up.railway.app/swagger

![FlowBoard board view](docs/screenshot.png)

---

## What it is

FlowBoard is a ZenHub-inspired project management tool: a fast Kanban board with epics, sprint planning, and a velocity chart. The frontend is a React SPA on Vercel; the backend is an ASP.NET Core 10 Web API on Railway; the database is Neon Postgres. The API uses **raw SQL** via Dapper + Npgsql — there is no Entity Framework, intentionally, so the SQL is the showcase.

---

## Features

- Kanban board with drag-and-drop (`@dnd-kit`) and optimistic UI
- Project-wide issue list with status / priority / epic / sprint / assignee / label / title-search filters, plus bulk actions (move-to-sprint, add-label, close, reopen, delete)
- Epics with color-coded pills, progress bars, and a dedicated detail page
- Sprint planning + a velocity chart (bar + line, Recharts)
- Issue comments with `@name` mentions resolved against project members
- Per-issue and per-project activity log (append-only, JSONB payload), with a filterable activity feed page
- Project-scoped labels (managed under **Settings → Labels**) attached to issues; click any label chip to deep-link into Issues filtered by that label
- Project member invitations + role management (owner / member) with last-owner protection
- User profile page (name + avatar) with current-password-gated password change that revokes other refresh tokens
- Global keyboard command palette (`Cmd/Ctrl+K` or `?`) for project navigation
- JWT auth (access + refresh tokens), BCrypt password hashing
- Global toast notifications surface backend errors with humanized copy
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
│   ├── FlowBoard.Api/        # ASP.NET Core 10 Web API
│   └── FlowBoard.Core/       # DB access + domain models + raw SQL
├── client/                   # React + Vite + Tailwind
├── schema.sql                # Full DB schema
├── docs/queries.md           # Annotated SQL queries
├── railway.json              # Railway deployment config
└── README.md
```

---

## Tech Stack

| Layer | Tech |
|---|---|
| Frontend | React 18, TypeScript, Vite, Tailwind, React Router 6, Zustand, TanStack Query, `@dnd-kit`, Recharts, Framer Motion |
| Backend | ASP.NET Core 10 Web API, C# 14, Dapper, Npgsql, BCrypt.Net-Next |
| Database | PostgreSQL (Neon free tier) |
| Hosting | Vercel (web), Railway (API) — total cost: $0 |

---

## SQL showcase

The API is intentionally written without an ORM. Three queries that do the heavy lifting:

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

The full annotated set is in [docs/queries.md](docs/queries.md).

---

## Indexing decisions

| Index | Why |
|---|---|
| `idx_issues_project_id` | Every project-scoped query filters here |
| `idx_issues_column_id` | Board view groups by column |
| `idx_issues_sprint_id` | Sprint detail and velocity queries |
| `idx_issues_epic_id` | Epic progress aggregations |
| `idx_issues_closed_at` | Velocity / completed-issue filters; partial-index candidate in v2 |
| `idx_columns_board_id` | Board fetch by board id |
| `idx_sprints_project_id` | Sprint list + velocity |
| `idx_epics_project_id` | Epic list |

---

## Local setup

```bash
# 1. Database
psql "$DATABASE_URL" -f schema.sql

# 2. API (http://localhost:8080)
cd src/FlowBoard.Api
dotnet restore
dotnet run

# 3. Web (http://localhost:5173)
cd client
npm install
npm run dev
```

Required env vars (see `.env.example`):

```env
# API (set in Railway in production)
ConnectionStrings__Neon=postgresql://...
Jwt__Secret=<64-char random>
Jwt__RefreshSecret=<different 64-char random>

# Web (set in Vercel in production)
VITE_API_URL=http://localhost:8080/api
```

---

## Roadmap

- GitHub Issues sync (GitHub OAuth + REST)
- Real-time board updates via SignalR
- Burndown chart (remaining points per day in sprint)
- Email invites for project members
- Gravatar-based avatars
