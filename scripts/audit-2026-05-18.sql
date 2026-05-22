-- FlowBoard backlog audit — 2026-05-18
-- Adds Jira/Zenhub-parity feature gaps as new epics/issues/sub-issues on the
-- "flowboard" project, closes stale tickets already shipped, and re-prioritises
-- a handful of high-impact open items.
--
-- Idempotent: re-runnable. Uses NOT EXISTS guards on (project_id, title) so
-- duplicate runs are no-ops.
--
-- Run with:
--   psql -U postgres -d flowboard -f scripts/audit-2026-05-18.sql

BEGIN;

DO $audit$
DECLARE
  v_project UUID;
  v_backlog UUID;

  -- Existing epic IDs
  v_workflow      UUID;
  v_collab        UUID;
  v_reporting     UUID;
  v_uxpolish      UUID;
  v_integrations  UUID;
  v_platform      UUID;
  v_secdevops     UUID;

  -- New epic IDs (resolved after insert)
  v_auth          UUID;
  v_realtime      UUID;
  v_observability UUID;
  v_a11y          UUID;
  v_portability   UUID;
  v_roadmap       UUID;

  -- Parent issue IDs for sub-issue linkage
  v_parent UUID;
BEGIN
  SELECT id INTO v_project FROM projects WHERE slug = 'flowboard';

  SELECT c.id INTO v_backlog
  FROM columns c
  JOIN boards b ON b.id = c.board_id
  WHERE b.project_id = v_project AND c.position = 0;

  -- ============================================================
  -- 1. Close stale tickets that are already shipped
  -- ============================================================
  -- These three Activity API filter sub-issues are live in the API
  -- (actorId, type, startDate, endDate params on
  -- GET /api/projects/{projectId}/activity). Marking them closed.
  UPDATE issues
     SET closed_at = NOW()
   WHERE project_id = v_project
     AND closed_at IS NULL
     AND title IN (
       'Add actor filter to Project Activity API',
       'Add date range filter to Project Activity API',
       'Add event type filter to Project Activity API'
     );

  -- ============================================================
  -- 2. Re-prioritise high-impact open items
  -- ============================================================
  -- These were "medium" but materially block parity with Jira/Zenhub.
  UPDATE issues SET priority = 'high'
   WHERE project_id = v_project AND closed_at IS NULL
     AND title IN (
       'Email notifications (assigned, mentioned, sprint starts)',
       'Issue dependencies (blocked-by / blocking) graph',
       'Issue search: full-text upgrade',
       'Custom column definitions per board',
       'GitHub: link issue ↔ PR, auto-close on merge'
     );

  -- Lower priority on long-tail nice-to-haves.
  UPDATE issues SET priority = 'low'
   WHERE project_id = v_project AND closed_at IS NULL
     AND title IN (
       'Estimate poker (multi-vote on points)',
       'Per-assignee throughput',
       'Release planning (group sprints into releases)'
     );

  -- ============================================================
  -- 3. New epics
  -- ============================================================
  INSERT INTO epics (project_id, title, description, color)
  SELECT v_project, t.title, t.description, t.color
  FROM (VALUES
    ('Auth & Account Security',
     'Password reset, email verification, OAuth/SSO logins, 2FA, and active-session management.',
     '#dc2626'),
    ('Real-time & Notifications',
     'SignalR hub for live board/issue updates, plus in-app notification center and per-event preferences.',
     '#8b5cf6'),
    ('Observability & Production-readiness',
     'Rate limiting, structured logging, error monitoring, health checks, and backup automation for a production-grade deploy.',
     '#f97316'),
    ('Accessibility & Mobile',
     'WCAG 2.1 AA audit, keyboard navigation polish, mobile-responsive layouts, and PWA shell.',
     '#06b6d4'),
    ('Data Portability',
     'CSV/JSON export and importers from GitHub Issues and Jira to lower switching costs.',
     '#65a30d'),
    ('Roadmap & Planning',
     'Gantt/timeline view, custom fields, issue templates, archiving, recycle bin, burnup, and velocity forecasting.',
     '#e11d48')
  ) AS t(title, description, color)
  WHERE NOT EXISTS (
    SELECT 1 FROM epics e
    WHERE e.project_id = v_project AND e.title = t.title
  );

  -- Snapshot all epic IDs (existing + new)
  SELECT id INTO v_workflow      FROM epics WHERE project_id = v_project AND title = 'Workflow';
  SELECT id INTO v_collab        FROM epics WHERE project_id = v_project AND title = 'Collaboration';
  SELECT id INTO v_reporting     FROM epics WHERE project_id = v_project AND title = 'Reporting';
  SELECT id INTO v_uxpolish      FROM epics WHERE project_id = v_project AND title = 'UX polish';
  SELECT id INTO v_integrations  FROM epics WHERE project_id = v_project AND title = 'Integrations';
  SELECT id INTO v_platform      FROM epics WHERE project_id = v_project AND title = 'Platform';
  SELECT id INTO v_secdevops     FROM epics WHERE project_id = v_project AND title = 'Security & DevOps';
  SELECT id INTO v_auth          FROM epics WHERE project_id = v_project AND title = 'Auth & Account Security';
  SELECT id INTO v_realtime      FROM epics WHERE project_id = v_project AND title = 'Real-time & Notifications';
  SELECT id INTO v_observability FROM epics WHERE project_id = v_project AND title = 'Observability & Production-readiness';
  SELECT id INTO v_a11y          FROM epics WHERE project_id = v_project AND title = 'Accessibility & Mobile';
  SELECT id INTO v_portability   FROM epics WHERE project_id = v_project AND title = 'Data Portability';
  SELECT id INTO v_roadmap       FROM epics WHERE project_id = v_project AND title = 'Roadmap & Planning';

  -- ============================================================
  -- 4. New top-level issues
  -- ============================================================
  CREATE TEMP TABLE _new_issues (
    epic_id     UUID,
    title       TEXT,
    description TEXT,
    priority    TEXT,
    points      INTEGER,
    labels      TEXT[]
  ) ON COMMIT DROP;

  INSERT INTO _new_issues VALUES
    -- Auth & Account Security
    (v_auth, 'Forgot / reset password flow',
     'Email-token reset: POST /api/auth/forgot creates a single-use signed token, emails a reset link. POST /api/auth/reset accepts {token, newPassword} and revokes existing refresh tokens. Frontend: /forgot and /reset routes.',
     'critical', 8, ARRAY['type:feature','area:backend','area:frontend']),
    (v_auth, 'Email verification on signup',
     'On register, create unverified user + send verification email. Unverified users can browse but cannot create projects. /api/auth/verify?token=... endpoint.',
     'high', 5, ARRAY['type:feature','area:backend']),
    (v_auth, 'OAuth login via Google',
     'OAuth2 authorisation-code flow. Link to existing email-matched users or create new ones. /api/auth/oauth/google/{start,callback}.',
     'medium', 8, ARRAY['type:feature','area:backend','area:frontend']),
    (v_auth, 'OAuth login via GitHub',
     'Mirror Google flow against GitHub OAuth Apps. Also lays groundwork for the GitHub integrations epic (shared token storage).',
     'medium', 5, ARRAY['type:feature','area:backend','area:frontend']),
    (v_auth, 'Two-factor authentication (TOTP)',
     'TOTP enrolment in profile settings (QR + manual code), recovery codes, enforced on login when enabled. Stored as encrypted secret.',
     'low', 8, ARRAY['type:feature','area:backend','area:frontend']),
    (v_auth, 'Active session management UI',
     'Profile → Sessions: list active refresh tokens (issued_at, user_agent, ip), revoke individually or "sign out everywhere".',
     'medium', 5, ARRAY['type:feature','area:backend','area:frontend']),

    -- Real-time & Notifications
    (v_realtime, 'SignalR hub for real-time board updates',
     'Add Microsoft.AspNetCore.SignalR; create BoardHub with per-project group. Publish on issue/column mutations. README roadmap item — currently no ticket existed.',
     'critical', 13, ARRAY['type:feature','area:backend','area:frontend','needs-design']),
    (v_realtime, 'In-app notification center',
     'Bell icon in topbar with unread badge. Drawer lists notifications (assigned, mentioned, sprint started, comment reply). /api/notifications GET + PATCH mark-read.',
     'high', 8, ARRAY['type:feature','area:backend','area:frontend']),
    (v_realtime, 'Notification preferences',
     'Per-user toggles for each event type and channel (in-app / email). Stored on a user_notification_prefs table.',
     'medium', 3, ARRAY['type:feature','area:backend','area:frontend']),
    (v_realtime, 'Live activity feed updates',
     'Reuse SignalR group: push new activity rows to /p/:slug/activity subscribers. Optimistic prepend + dedup on ack.',
     'medium', 5, ARRAY['type:feature','area:backend','area:frontend']),

    -- Observability & Production-readiness
    (v_observability, 'API rate limiting (per-IP + per-user)',
     'AspNetCoreRateLimit middleware. Stricter limits on /api/auth/login and /api/auth/forgot to slow brute-force.',
     'critical', 5, ARRAY['type:feature','area:backend']),
    (v_observability, 'Structured logging with Serilog + request correlation IDs',
     'Swap built-in logger for Serilog (console + JSON sink). Middleware to attach a request-scoped correlation ID; surface it in error responses.',
     'high', 5, ARRAY['type:tech-debt','area:backend']),
    (v_observability, 'Sentry / error monitoring integration',
     'Sentry SDK on both API (Sentry.AspNetCore) and client (Sentry.React). Source maps uploaded from Vite build.',
     'high', 5, ARRAY['type:feature','area:backend','area:frontend']),
    (v_observability, 'Enrich /api/health with DB + dependency checks',
     'Return per-dependency status (Postgres ping, JWT key loaded, etc.) under /api/health and /api/health/ready for k8s-style probes.',
     'medium', 3, ARRAY['type:chore','area:backend']),
    (v_observability, 'Automated daily DB backups',
     'Scheduled pg_dump to object storage with 14-day retention. Neon has snapshots but we want a portable artifact + restore runbook.',
     'medium', 5, ARRAY['type:chore','area:db','area:devx']),
    (v_observability, 'OpenTelemetry traces for API requests',
     'Wire OTel SDK with HTTP + Npgsql instrumentation. Export to console in dev, OTLP in prod.',
     'low', 8, ARRAY['type:tech-debt','area:backend']),

    -- Accessibility & Mobile
    (v_a11y, 'WCAG 2.1 AA audit pass',
     'Run axe-core in Playwright across every page. Fix contrast, focus rings, ARIA labels on icon buttons, landmark roles, and skip links.',
     'high', 8, ARRAY['type:chore','area:frontend']),
    (v_a11y, 'Keyboard navigation polish across modals + tables',
     'Trap focus in modals, restore on close, arrow-key navigation in board columns and issue tables, Esc to cancel drag.',
     'high', 5, ARRAY['type:feature','area:frontend']),
    (v_a11y, 'Mobile-responsive board view',
     'Below md breakpoint: stack columns vertically, sticky column headers, collapse epic chips. Touch-friendly drag handles.',
     'high', 8, ARRAY['type:feature','area:frontend','needs-design']),
    (v_a11y, 'Mobile-responsive issues table + filters',
     'Issues page table → card list on mobile; filters collapse into a bottom sheet.',
     'medium', 5, ARRAY['type:feature','area:frontend','needs-design']),
    (v_a11y, 'PWA manifest + service worker shell',
     'manifest.webmanifest, app icons, offline shell with workbox. Read-only board view works offline from last cache.',
     'low', 5, ARRAY['type:feature','area:frontend']),

    -- Data Portability
    (v_portability, 'Export project issues to CSV',
     'GET /api/projects/{id}/export.csv with current filters. Columns: id, title, status, priority, points, assignee, epic, sprint, labels, created_at, closed_at.',
     'medium', 3, ARRAY['type:feature','area:backend']),
    (v_portability, 'Export full project to JSON',
     'Single-call dump of project, epics, sprints, issues, comments, labels for backup / migration. Re-importable by the JSON importer.',
     'medium', 5, ARRAY['type:feature','area:backend']),
    (v_portability, 'Import from GitHub Issues',
     'OAuth-authed importer: pick repo, map labels → labels, milestones → sprints, assignees → members (by GitHub login).',
     'medium', 13, ARRAY['type:feature','area:backend','area:frontend']),
    (v_portability, 'Import from Jira (CSV)',
     'Upload Jira CSV export. Field mapper UI lets the user match Jira fields → FlowBoard fields before commit.',
     'low', 8, ARRAY['type:feature','area:backend','area:frontend']),

    -- Roadmap & Planning
    (v_roadmap, 'Roadmap / timeline (Gantt) view',
     'New /p/:slug/roadmap page. Horizontal time axis, swimlanes per epic, bars per sprint. Drag to retime, resize to change duration.',
     'critical', 13, ARRAY['type:feature','area:frontend','needs-design']),
    (v_roadmap, 'Custom fields on issues',
     'Per-project custom_field_defs (name, type: text/number/select/date). Values in issue_custom_field_values keyed by (issue_id, field_id).',
     'high', 13, ARRAY['type:feature','area:backend','area:db','area:frontend']),
    (v_roadmap, 'Issue templates per project',
     'Per-project templates with default title prefix, description body, labels, priority, points. Picker on CreateIssueModal.',
     'medium', 5, ARRAY['type:feature','area:backend','area:frontend']),
    (v_roadmap, 'Issue archiving (soft delete)',
     'Replace hard-delete with archived_at timestamp. Archived issues hidden by default; "Show archived" filter exposes them.',
     'high', 5, ARRAY['type:feature','area:backend','area:frontend']),
    (v_roadmap, 'Recycle bin / trash view',
     'Settings → Trash lists archived issues with restore + permanent-delete actions. 30-day auto-purge job.',
     'medium', 5, ARRAY['type:feature','area:backend','area:frontend']),
    (v_roadmap, 'Burnup chart',
     'Per-sprint burnup (scope vs completed) alongside the existing burndown. Recharts area chart.',
     'medium', 5, ARRAY['type:feature','area:backend','area:frontend']),
    (v_roadmap, 'Sprint velocity forecast',
     'Use trailing-3-sprint average velocity to project completion date for current backlog at current scope.',
     'low', 5, ARRAY['type:feature','area:backend','area:frontend']),
    (v_roadmap, 'Onboarding tour + empty states',
     'First-time project: guided tour highlighting board, epics, sprints, planning, reports. Every list has a helpful empty state with a CTA.',
     'medium', 5, ARRAY['type:feature','area:frontend','needs-design']);

  -- Insert new top-level issues (skip duplicates by title).
  INSERT INTO issues (project_id, column_id, epic_id, title, description, priority, story_points, position)
  SELECT v_project,
         v_backlog,
         s.epic_id,
         s.title,
         s.description,
         s.priority,
         s.points,
         100 + ROW_NUMBER() OVER (PARTITION BY s.epic_id ORDER BY s.title)
  FROM _new_issues s
  WHERE NOT EXISTS (
    SELECT 1 FROM issues i WHERE i.project_id = v_project AND i.title = s.title
  );

  -- Link labels for newly-created top-level issues.
  INSERT INTO issue_labels (issue_id, label_id)
  SELECT i.id, l.id
  FROM _new_issues s
  JOIN issues i ON i.project_id = v_project AND i.title = s.title
  CROSS JOIN LATERAL unnest(s.labels) AS lbl(name)
  JOIN labels l ON l.project_id = v_project AND LOWER(l.name) = LOWER(lbl.name)
  ON CONFLICT DO NOTHING;

  -- ============================================================
  -- 5. Sub-issues (parent_id linked) under existing + new parents
  -- ============================================================
  CREATE TEMP TABLE _new_subs (
    parent_title TEXT,
    title        TEXT,
    description  TEXT,
    priority     TEXT,
    points       INTEGER,
    labels       TEXT[]
  ) ON COMMIT DROP;

  INSERT INTO _new_subs VALUES
    -- Under "Issue dependencies (blocked-by / blocking) graph"
    ('Issue dependencies (blocked-by / blocking) graph',
     'Schema: issue_dependencies table',
     'CREATE TABLE issue_dependencies (issue_id UUID, depends_on_id UUID, kind TEXT CHECK (kind IN (''blocks'',''relates'')), PRIMARY KEY (issue_id, depends_on_id)). Add CASCADE on issue delete.',
     'high', 2, ARRAY['type:feature','area:db']),
    ('Issue dependencies (blocked-by / blocking) graph',
     'API: CRUD endpoints for issue dependencies',
     'POST/DELETE /api/issues/{id}/dependencies. GET /api/issues/{id}/dependencies returns both directions (blocks + blocked_by).',
     'high', 3, ARRAY['type:feature','area:backend']),
    ('Issue dependencies (blocked-by / blocking) graph',
     'UI: dependencies panel in IssueModal',
     'Add "Blocks / Blocked by" section in IssueModal sidebar. Picker (project issue search) + chip list with remove button.',
     'high', 3, ARRAY['type:feature','area:frontend']),

    -- Under "Release planning (group sprints into releases)"
    ('Release planning (group sprints into releases)',
     'Schema: releases table + sprints.release_id FK',
     'CREATE TABLE releases (id, project_id, name, target_date, status). ALTER TABLE sprints ADD COLUMN release_id UUID REFERENCES releases(id) ON DELETE SET NULL.',
     'low', 3, ARRAY['type:feature','area:db']),
    ('Release planning (group sprints into releases)',
     'API: release CRUD + sprint-link endpoints',
     '/api/projects/{id}/releases. PATCH /api/sprints/{id} accepts releaseId (empty-Guid sentinel clears).',
     'low', 3, ARRAY['type:feature','area:backend']),
    ('Release planning (group sprints into releases)',
     'UI: release detail page + burnup',
     '/p/:slug/releases/:id page: lists member sprints, aggregated velocity/scope, release burnup chart.',
     'low', 5, ARRAY['type:feature','area:frontend']),

    -- Under "GitHub: link issue ↔ PR, auto-close on merge"
    ('GitHub: link issue ↔ PR, auto-close on merge',
     'Webhook receiver endpoint with HMAC validation',
     'POST /api/webhooks/github. Validate X-Hub-Signature-256 against per-project secret. Route by event type.',
     'high', 3, ARRAY['type:feature','area:backend']),
    ('GitHub: link issue ↔ PR, auto-close on merge',
     'Parse Fixes/Closes keywords in PR body and commits',
     'Regex match (?i)(fix|close|resolve)(es|ed|d)?\\s+#\\d+ → link PR to issue. Store in issue_pr_links table.',
     'high', 3, ARRAY['type:feature','area:backend']),
    ('GitHub: link issue ↔ PR, auto-close on merge',
     'Auto-close linked issues on PR merge',
     'On pull_request closed+merged event: move linked issues to Done column + closed_at = NOW(), with actor = the PR author (or system).',
     'high', 3, ARRAY['type:feature','area:backend']),

    -- Under "Custom column definitions per board"
    ('Custom column definitions per board',
     'Settings → Columns tab UI',
     '/p/:slug/settings/columns: drag-reorder list, rename inline, add new, archive (with confirm if non-empty).',
     'high', 5, ARRAY['type:feature','area:frontend']),
    ('Custom column definitions per board',
     'Backend: column CRUD + reorder + archive',
     'POST/PATCH/DELETE /api/projects/{id}/columns. Soft-archive instead of delete when issues remain (move them to a default column).',
     'high', 3, ARRAY['type:feature','area:backend']),

    -- Under "Webhooks (outgoing)"
    ('Webhooks (outgoing)',
     'Schema: webhook subscriptions table',
     'CREATE TABLE webhook_subscriptions (id, project_id, url, secret, events TEXT[], active BOOL, created_at). Per-project list with toggle.',
     'low', 2, ARRAY['type:feature','area:db']),
    ('Webhooks (outgoing)',
     'HMAC-signed payload sender + retry queue',
     'BackgroundService that dequeues outgoing events and POSTs with X-FlowBoard-Signature header. Exponential backoff, 5 retries, dead-letter table.',
     'low', 5, ARRAY['type:feature','area:backend']),
    ('Webhooks (outgoing)',
     'Settings → Webhooks tab UI',
     '/p/:slug/settings/webhooks: create/edit/disable subscriptions, multi-select event types, test-fire button.',
     'low', 5, ARRAY['type:feature','area:frontend']),

    -- Under "SignalR hub for real-time board updates" (new parent)
    ('SignalR hub for real-time board updates',
     'BoardHub: per-project group + auth handshake',
     'Add Microsoft.AspNetCore.SignalR. BoardHub joins/leaves group "project:{id}" after verifying ProjectAuthorizer.IsMemberAsync on connect.',
     'high', 5, ARRAY['type:feature','area:backend']),
    ('SignalR hub for real-time board updates',
     'Server-side publish on issue/column mutations',
     'Every IssueController/ColumnsController write publishes {event, payload} to its project group. Activities are the canonical event log.',
     'high', 5, ARRAY['type:feature','area:backend']),
    ('SignalR hub for real-time board updates',
     'React client: useBoardSocket hook + cache invalidation',
     '@microsoft/signalr connection lifecycle hook keyed by projectId. On message: targeted TanStack Query cache updates (no full refetch).',
     'high', 5, ARRAY['type:feature','area:frontend']),

    -- Under "Custom fields on issues" (new parent)
    ('Custom fields on issues',
     'Schema: custom_field_defs + values tables',
     'custom_field_defs (id, project_id, name, type, options JSONB, position). issue_custom_field_values (issue_id, field_id, value JSONB). Type validated server-side.',
     'medium', 3, ARRAY['type:feature','area:db']),
    ('Custom fields on issues',
     'Backend: field def CRUD + value read/write',
     'CRUD under /api/projects/{id}/custom-fields. PATCH /api/issues/{id} accepts a customFields dict; values upserted in a single statement.',
     'medium', 5, ARRAY['type:feature','area:backend']),
    ('Custom fields on issues',
     'UI: Settings → Custom Fields + IssueModal renderer',
     'Settings tab to define fields. IssueModal renders inputs by type (text/number/select/date). Issues filter UI gains a "where customField X = …" predicate.',
     'medium', 8, ARRAY['type:feature','area:frontend']),

    -- Under "Roadmap / timeline (Gantt) view" (new parent)
    ('Roadmap / timeline (Gantt) view',
     'Backend: timeline data aggregation endpoint',
     'GET /api/projects/{id}/roadmap returns epics + sprints with start/end ranges in a flat array suitable for swimlane rendering.',
     'critical', 3, ARRAY['type:feature','area:backend']),
    ('Roadmap / timeline (Gantt) view',
     'UI: Gantt component with drag-to-retime',
     'Custom SVG/Canvas timeline (avoid heavy 3p deps). Drag bar = update start_date/end_date via PATCH. Resize handles. Zoom (week/month/quarter).',
     'critical', 13, ARRAY['type:feature','area:frontend','needs-design']),

    -- Under "Forgot / reset password flow" (new parent)
    ('Forgot / reset password flow',
     'Schema: password_reset_tokens table',
     'password_reset_tokens (token_hash, user_id, expires_at, used_at). Single-use, 1 h expiry, store sha256 of token (never plaintext).',
     'critical', 2, ARRAY['type:feature','area:db']),
    ('Forgot / reset password flow',
     'API: /auth/forgot + /auth/reset endpoints',
     'POST /api/auth/forgot {email} always returns 200 (avoid user enumeration). POST /api/auth/reset {token, newPassword} validates + revokes refresh tokens.',
     'critical', 5, ARRAY['type:feature','area:backend']),
    ('Forgot / reset password flow',
     'UI: /forgot and /reset pages',
     'Two pages plus inline-error forms. Reset page disables submit on weak passwords (reuse register-page strength rules).',
     'critical', 3, ARRAY['type:feature','area:frontend']);

  -- Insert each sub-issue, linking parent_id and epic_id from its parent.
  -- Guard against re-runs by (project_id, title).
  INSERT INTO issues (project_id, column_id, parent_id, epic_id, title, description, priority, story_points, position)
  SELECT v_project,
         v_backlog,
         p.id,
         p.epic_id,
         s.title,
         s.description,
         s.priority,
         s.points,
         200 + ROW_NUMBER() OVER (PARTITION BY p.id ORDER BY s.title)
  FROM _new_subs s
  JOIN issues p ON p.project_id = v_project AND p.title = s.parent_title
  WHERE NOT EXISTS (
    SELECT 1 FROM issues i WHERE i.project_id = v_project AND i.title = s.title
  );

  -- Labels on new sub-issues.
  INSERT INTO issue_labels (issue_id, label_id)
  SELECT i.id, l.id
  FROM _new_subs s
  JOIN issues i ON i.project_id = v_project AND i.title = s.title
  CROSS JOIN LATERAL unnest(s.labels) AS lbl(name)
  JOIN labels l ON l.project_id = v_project AND LOWER(l.name) = LOWER(lbl.name)
  ON CONFLICT DO NOTHING;

END
$audit$;

COMMIT;

-- Verification: counts + priority distribution
SELECT 'epics'        AS kind, COUNT(*) FROM epics  WHERE project_id = (SELECT id FROM projects WHERE slug='flowboard')
UNION ALL SELECT 'issues_total',  COUNT(*) FROM issues WHERE project_id = (SELECT id FROM projects WHERE slug='flowboard')
UNION ALL SELECT 'issues_open',   COUNT(*) FROM issues WHERE project_id = (SELECT id FROM projects WHERE slug='flowboard') AND closed_at IS NULL
UNION ALL SELECT 'issues_closed', COUNT(*) FROM issues WHERE project_id = (SELECT id FROM projects WHERE slug='flowboard') AND closed_at IS NOT NULL
UNION ALL SELECT 'sub_issues',    COUNT(*) FROM issues WHERE project_id = (SELECT id FROM projects WHERE slug='flowboard') AND parent_id IS NOT NULL;

SELECT priority, COUNT(*)
FROM issues
WHERE project_id = (SELECT id FROM projects WHERE slug='flowboard') AND closed_at IS NULL
GROUP BY priority
ORDER BY CASE priority WHEN 'critical' THEN 1 WHEN 'high' THEN 2 WHEN 'medium' THEN 3 WHEN 'low' THEN 4 END;
