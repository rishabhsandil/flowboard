# Annotated SQL queries

These are FlowBoard's load-bearing queries. All are hand-written and run via Dapper + Npgsql from the .NET API. Parameter style is `@name` (Dapper).

---

## 1. Board view — issues grouped by column

**Where it's used:** `GET /api/projects/{id}/board`. The single most-hit endpoint.

**Why this shape:**
- One round-trip returns the entire board: columns + their ordered issues + each issue's epic color/title for the card pill.
- `json_agg(... ORDER BY i.position)` orders issues inside the aggregate so the client doesn't have to sort.
- `FILTER (WHERE i.id IS NOT NULL)` makes empty columns return `NULL`. Without it, a `LEFT JOIN` with no matches produces `[null]`.
- `COALESCE(..., '[]')` then turns `NULL` into a real empty JSON array so the client can iterate without a guard.

```sql
SELECT
  c.id   AS column_id,
  c.name AS column_name,
  c.position AS column_position,
  COALESCE(
    json_agg(
      json_build_object(
        'id',           i.id,
        'title',        i.title,
        'priority',     i.priority,
        'story_points', i.story_points,
        'position',     i.position,
        'assignee_id',  i.assignee_id,
        'epic_id',      i.epic_id,
        'epic_color',   e.color,
        'epic_title',   e.title
      ) ORDER BY i.position
    ) FILTER (WHERE i.id IS NOT NULL),
    '[]'
  ) AS issues
FROM columns c
LEFT JOIN issues i ON i.column_id = c.id
LEFT JOIN epics  e ON e.id = i.epic_id
WHERE c.board_id = @boardId
GROUP BY c.id, c.name, c.position
ORDER BY c.position;
```

---

## 2. Sprint velocity with running cumulative total

**Where it's used:** `GET /api/sprints/{id}/velocity` and the project-wide velocity chart.

**Why this shape:**
- A `LEFT JOIN` (not `INNER`) so sprints with zero completed points still appear at zero — important for an honest chart.
- The `closed_at <= s.end_date + INTERVAL '1 day'` clause guards against issues that were closed in a later sprint but still tagged here.
- A **window function over an aggregate** (`SUM(...) OVER (ORDER BY start_date ROWS UNBOUNDED PRECEDING)`) computes the cumulative total in the same pass as the per-sprint sum. The naive alternative is a self-join or a correlated subquery — both scan `sprints` twice.

```sql
SELECT
  s.id,
  s.name,
  s.start_date,
  s.end_date,
  COALESCE(SUM(i.story_points), 0) AS points_completed,
  SUM(COALESCE(SUM(i.story_points), 0))
    OVER (ORDER BY s.start_date ROWS UNBOUNDED PRECEDING) AS cumulative_points
FROM sprints s
LEFT JOIN issues i
  ON i.sprint_id = s.id
  AND i.closed_at IS NOT NULL
  AND i.closed_at <= s.end_date + INTERVAL '1 day'
WHERE s.project_id = @projectId
  AND s.status = 'completed'
GROUP BY s.id, s.name, s.start_date, s.end_date
ORDER BY s.start_date;
```

---

## 3. Epic progress — completion percentage and points breakdown

**Where it's used:** `GET /api/projects/{id}/epics`.

**Why this shape:**
- Single aggregation pass returns count + completion ratio + points totals — the alternative is one query per epic (N+1).
- `NULLIF(COUNT(i.id), 0)` prevents division-by-zero on epics with no issues; the result becomes `NULL` and the UI renders "—".
- `COUNT(i.closed_at)` counts non-null `closed_at` values, exactly the closed-issue count without a `FILTER` clause.
- `CASE WHEN ... THEN i.story_points ELSE 0 END` inside `SUM` avoids a second join just to get completed-only points.

```sql
SELECT
  e.id,
  e.title,
  e.color,
  e.due_date,
  COUNT(i.id) AS total_issues,
  COUNT(i.closed_at) AS closed_issues,
  ROUND(
    COUNT(i.closed_at)::NUMERIC / NULLIF(COUNT(i.id), 0) * 100, 1
  ) AS percent_complete,
  COALESCE(SUM(i.story_points), 0) AS total_points,
  COALESCE(
    SUM(CASE WHEN i.closed_at IS NOT NULL THEN i.story_points ELSE 0 END),
    0
  ) AS completed_points
FROM epics e
LEFT JOIN issues i ON i.epic_id = e.id
WHERE e.project_id = @projectId
GROUP BY e.id, e.title, e.color, e.due_date
ORDER BY e.created_at;
```

---

## 4. Bulk reorder after drag-and-drop

**Where it's used:** `PATCH /api/projects/{id}/issues/reorder`.

**Why this shape:**
- A single `UPDATE ... FROM unnest(...)` rewrites every moved issue's `column_id` and `position` in one statement instead of N round-trips.
- `unnest` accepts arrays from Dapper/Npgsql cleanly without dynamic SQL string-building.

```sql
UPDATE issues AS i
SET
  column_id = v.column_id,
  position  = v.position
FROM unnest(@ids::uuid[], @columnIds::uuid[], @positions::int[])
  AS v(id, column_id, position)
WHERE i.id = v.id;
```

---

## 5. Project-wide issue list with optional filters

**Where it's used:** `GET /api/projects/{id}/issues` (the Issues page).

**Why this shape:**
- One static SQL string handles every combination of filters — no string concatenation, no dynamic SQL. Each predicate is `(@X IS NULL OR col = @X)`, so a NULL parameter disables that filter.
- Nullable FK filters (`epic_id`, `sprint_id`, `assignee_id`) accept the empty-Guid sentinel `'00000000-…'` to mean "rows where this FK is NULL", mirroring the `IssueQueries.Update` contract for clearing the same FKs.
- A lateral subquery bundles each issue's labels as JSON, matching how the board view ships labels — the API forwards the JSON string and the client `JSON.parse`s it once per row.
- The `ORDER BY` puts open issues first, then a `CASE` ranks priority (`critical` → `low`), then most-recently-updated. That's the order users actually want when scanning.

```sql
SELECT i.*, c.name AS column_name, e.title AS epic_title, e.color AS epic_color,
       s.name AS sprint_name, u.name AS assignee_name,
       COALESCE(lb.labels, '[]'::json)::text AS labels_json
FROM issues i
LEFT JOIN columns c ON c.id = i.column_id
LEFT JOIN epics   e ON e.id = i.epic_id
LEFT JOIN sprints s ON s.id = i.sprint_id
LEFT JOIN users   u ON u.id = i.assignee_id
LEFT JOIN LATERAL (
  SELECT json_agg(json_build_object('id', l.id, 'name', l.name, 'color', l.color)
                  ORDER BY LOWER(l.name)) AS labels
  FROM issue_labels il JOIN labels l ON l.id = il.label_id
  WHERE il.issue_id = i.id
) lb ON TRUE
WHERE i.project_id = @projectId
  AND (@status IS NULL
       OR (@status = 'open'   AND i.closed_at IS NULL)
       OR (@status = 'closed' AND i.closed_at IS NOT NULL))
  AND (@priority IS NULL OR i.priority = @priority)
  AND (@epicId   IS NULL
       OR (@epicId = '00000000-0000-0000-0000-000000000000' AND i.epic_id IS NULL)
       OR i.epic_id = @epicId)
  -- ...same shape for @sprintId, @assigneeId
  AND (@labelId IS NULL
       OR EXISTS (SELECT 1 FROM issue_labels il2
                  WHERE il2.issue_id = i.id AND il2.label_id = @labelId))
  AND (@search IS NULL OR i.title ILIKE '%' || @search || '%')
ORDER BY i.closed_at IS NULL DESC,
         CASE i.priority WHEN 'critical' THEN 0 WHEN 'high' THEN 1
                         WHEN 'medium'   THEN 2 WHEN 'low'  THEN 3 END,
         i.updated_at DESC
OFFSET @skip LIMIT @take;
```

---

## 6. CFD snapshot upsert (idempotent daily accumulation)

**Where it's used:** `GET /api/projects/{id}/reports/cfd` — called once per visit, auto-upserts today's counts before returning data.

**Why this shape:**
- `INSERT … ON CONFLICT (project_id, column_id, snapped_at) DO UPDATE` makes every call idempotent: re-visiting the page on the same day simply overwrites stale counts rather than inserting a duplicate.
- The inner `SELECT` counts open issues per column in one pass; no temp table needed.
- Data accumulates naturally — each calendar day the page is visited adds one row per column, giving the CFD its historical depth over time.

```sql
INSERT INTO board_snapshots (project_id, column_id, column_name, issue_count, snapped_at)
SELECT
  c.project_id,
  c.id        AS column_id,
  c.name      AS column_name,
  COUNT(i.id) AS issue_count,
  CURRENT_DATE AS snapped_at
FROM columns c
LEFT JOIN issues i
  ON i.column_id = c.id
  AND i.closed_at IS NULL
WHERE c.project_id = @ProjectId
GROUP BY c.project_id, c.id, c.name
ON CONFLICT (project_id, column_id, snapped_at)
DO UPDATE SET
  issue_count = EXCLUDED.issue_count,
  column_name = EXCLUDED.column_name;
```

---

## 7. CFD historical data retrieval

**Where it's used:** `GET /api/projects/{id}/reports/cfd?days=N` — fetches the last N days of snapshots for the stacked AreaChart.

**Why this shape:**
- `CURRENT_DATE - (@Days::int - 1)` anchors the window so "30 days" includes today (day 30) back through day 1, not 30 days *before* today.
- Ordering by `(snapped_at, column_name)` is important: the React client pivots rows into `{ date, [colName]: count }` objects; consistent ordering means no extra sorting on the frontend.
- The client derives the set of unique column names from the returned rows and passes them to Recharts as `<Area>` keys.

```sql
SELECT
  snapped_at  AS day,
  column_name,
  issue_count
FROM board_snapshots
WHERE project_id = @ProjectId
  AND snapped_at >= CURRENT_DATE - (@Days::int - 1)
ORDER BY snapped_at, column_name;
```

---

## 8. Password reset — issue + redeem

**Where they're used:**
`POST /api/auth/forgot` inserts a hash; `POST /api/auth/reset` redeems it
(inside a transaction with `users.UPDATE` and
`RefreshTokenQueries.RevokeAllForUser`).

**Why this shape:**
- The PK is `token_hash`, not the raw token — only the SHA-256 hex of the
  random 32-byte token ever lands in the DB. A DB read alone cannot let an
  attacker redeem outstanding links.
- `MarkUsed` includes `used_at IS NULL` in the WHERE so a concurrent
  second redemption affects zero rows; the controller branches on the row
  count to detect the race and return `invalid_reset_token`.
- `InvalidateAllForUser` runs in two places: (1) **before** issuing a new
  token in `/auth/forgot` so a resend invalidates any prior outstanding
  links — only the most recent link in the user's inbox is redeemable;
  (2) **after** a successful reset so a parallel leaked link can't be
  redeemed once the password has changed.
- `MostRecentCreatedAtForUser` powers the per-email cooldown in
  `/auth/forgot` — if the most recent issuance is within
  `Forgot:PerEmailCooldownSeconds`, the controller silently drops the new
  request (still 200, no enumeration leak).
- `DeleteStaleRows` is run on a schedule by `PasswordResetCleanupService`
  with `@Cutoff = NOW() - retention`. Bounded retention prevents the
  table from growing forever while keeping recently-redeemed rows around
  briefly for log correlation.

```sql
-- Insert
INSERT INTO password_reset_tokens (token_hash, user_id, expires_at)
VALUES (@TokenHash, @UserId, @ExpiresAt);

-- Mark used (idempotent guard)
UPDATE password_reset_tokens
SET used_at = NOW()
WHERE token_hash = @TokenHash AND used_at IS NULL;

-- Invalidate every outstanding reset link for the user
UPDATE password_reset_tokens
SET used_at = NOW()
WHERE user_id = @UserId AND used_at IS NULL;

-- Per-email cooldown lookup
SELECT MAX(created_at)
FROM password_reset_tokens
WHERE user_id = @UserId;

-- Periodic cleanup
DELETE FROM password_reset_tokens
WHERE (used_at IS NOT NULL OR expires_at < NOW())
  AND created_at < @Cutoff;
```

---

## N. Issue dependencies — list both directions in one round-trip

**Where it's used:** `GET /api/issues/{id}/dependencies`. Renders the "Linked issues" panel in the IssueModal.

**Why this shape:**
- A single `UNION ALL` returns every edge that touches the focus issue regardless of which side it sits on; the synthetic `direction` column ('outgoing' / 'incoming') lets the controller split outgoing vs incoming without a second query.
- Joining `issues` and `columns` inline gives the chip its title and column name, so the UI never needs a per-link follow-up fetch.
- Index on `depends_on_id` keeps the incoming half a seek instead of a scan.

```sql
SELECT
  d.issue_id          AS IssueId,
  d.depends_on_id     AS DependsOnId,
  d.kind              AS Kind,
  d.created_at        AS CreatedAt,
  'outgoing'          AS Direction,
  other.id            AS OtherId,
  other.title         AS OtherTitle,
  other.closed_at     AS OtherClosedAt,
  col.name            AS OtherColumnName
FROM issue_dependencies d
JOIN issues  other ON other.id = d.depends_on_id
LEFT JOIN columns col ON col.id = other.column_id
WHERE d.issue_id = @IssueId
UNION ALL
SELECT
  d.issue_id, d.depends_on_id, d.kind, d.created_at,
  'incoming',
  other.id, other.title, other.closed_at, col.name
FROM issue_dependencies d
JOIN issues  other ON other.id = d.issue_id
LEFT JOIN columns col ON col.id = other.column_id
WHERE d.depends_on_id = @IssueId
ORDER BY Kind, CreatedAt ASC;
```

**Same-project guard** (run by the `POST` handler before insert — ensures a member can't link across projects even if they're a member of both):

```sql
SELECT 1
FROM issues a
JOIN issues b ON b.project_id = a.project_id
WHERE a.id = @IssueId AND b.id = @DependsOnId
LIMIT 1;
```

**1-hop reverse-blocks cycle guard** — catches the immediate A↔B case. Deeper cycles are tolerated (would require a recursive CTE walk to detect).

```sql
SELECT 1
FROM issue_dependencies
WHERE issue_id      = @DependsOnId
  AND depends_on_id = @IssueId
  AND kind          = 'blocks'
LIMIT 1;
```
