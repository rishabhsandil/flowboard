-- FlowBoard backlog seed for rishabhsandil@gmail.com.
-- Creates the "FlowBoard" project + board + columns + labels + epics + issues
-- mirroring the unchecked items in docs/FEATURES.md and the one outstanding
-- AUDIT-2026-05 follow-up. Idempotent: re-running is a no-op (slug uniqueness
-- + ON CONFLICT guards). Run once with:
--   psql -U postgres -d flowboard -f seed-backlog.sql

BEGIN;

-- ---------- 1. PROJECT (skip if already seeded) ----------
INSERT INTO projects (name, slug, description, owner_id)
SELECT 'FlowBoard',
       'flowboard',
       'Self-hosted backlog for the FlowBoard project itself.',
       u.id
FROM users u
WHERE u.email = 'rishabhsandil@gmail.com'
ON CONFLICT (slug) DO NOTHING;

-- Snapshot the IDs we will reuse below.
DO $seed$
DECLARE
  v_project   UUID;
  v_owner     UUID;
  v_board     UUID;
  v_backlog   UUID;
  v_inprog    UUID;
  v_review    UUID;
  v_done      UUID;
BEGIN
  SELECT id INTO v_owner   FROM users    WHERE email = 'rishabhsandil@gmail.com';
  SELECT id INTO v_project FROM projects WHERE slug  = 'flowboard';

  -- 2. Project membership (owner)
  INSERT INTO project_members (project_id, user_id, role)
  VALUES (v_project, v_owner, 'owner')
  ON CONFLICT DO NOTHING;

  -- 3. Board + default columns (only if missing)
  SELECT id INTO v_board FROM boards WHERE project_id = v_project LIMIT 1;
  IF v_board IS NULL THEN
    INSERT INTO boards (project_id) VALUES (v_project) RETURNING id INTO v_board;
    INSERT INTO columns (board_id, name, position) VALUES
      (v_board, 'Backlog',     0),
      (v_board, 'In Progress', 1),
      (v_board, 'In Review',   2),
      (v_board, 'Done',        3);
  END IF;
  SELECT id INTO v_backlog FROM columns WHERE board_id = v_board AND position = 0;
  SELECT id INTO v_inprog  FROM columns WHERE board_id = v_board AND position = 1;
  SELECT id INTO v_review  FROM columns WHERE board_id = v_board AND position = 2;
  SELECT id INTO v_done    FROM columns WHERE board_id = v_board AND position = 3;

  -- 4. LABELS (case-insensitive unique on (project_id, name))
  --    Two axes: `type:*` (kind of work) and `area:*` (subsystem),
  --    plus a few cross-cutting flags.
  INSERT INTO labels (project_id, name, color) VALUES
    (v_project, 'type:feature',     '#22d3ee'),
    (v_project, 'type:bug',         '#ef4444'),
    (v_project, 'type:chore',       '#64748b'),
    (v_project, 'type:tech-debt',   '#a855f7'),
    (v_project, 'area:backend',     '#0ea5e9'),
    (v_project, 'area:frontend',    '#10b981'),
    (v_project, 'area:db',          '#f59e0b'),
    (v_project, 'area:docs',        '#94a3b8'),
    (v_project, 'area:devx',        '#ec4899'),
    (v_project, 'good-first-issue', '#84cc16'),
    (v_project, 'needs-design',     '#fbbf24')
  ON CONFLICT DO NOTHING;

  -- 5. EPICS — one per FEATURES.md "Future / stretch" section.
  INSERT INTO epics (project_id, title, description, color) VALUES
    (v_project, 'Workflow',
       'Drag-driven planning: assign issues to sprints/epics by dropping, plan sprints side-by-side, and model issue relationships.',
       '#22d3ee'),
    (v_project, 'Collaboration',
       'Comments, mentions, notifications, member invitations, and per-project audit log.',
       '#10b981'),
    (v_project, 'Reporting',
       'Burndown, cumulative flow, lead/cycle time, per-assignee throughput, epic burnup, releases.',
       '#a855f7'),
    (v_project, 'UX polish',
       'Keyboard palette, saved filters, theme toggle, markdown rendering, attachments, custom confirm dialog.',
       '#f59e0b'),
    (v_project, 'Integrations',
       'GitHub, Slack, outgoing webhooks, iCal feed.',
       '#ec4899'),
    (v_project, 'Platform',
       'Custom columns, multi-board, WIP limits, workflow rules, time tracking, public read-only sharing.',
       '#0ea5e9')
  ON CONFLICT DO NOTHING;

  -- 6. ISSUES — one row per unchecked FEATURES.md / AUDIT item.
  --    Use a CTE-style staging insert via a temp table so we can batch-link
  --    epic + labels in one place.
  CREATE TEMP TABLE _seed_issues (
    epic_title  TEXT,
    title       TEXT,
    description TEXT,
    priority    TEXT,
    points      INTEGER,
    labels      TEXT[]
  ) ON COMMIT DROP;

  INSERT INTO _seed_issues VALUES
    -- Workflow
    ('Workflow', 'Drag issue onto a sprint card to assign it',
       'Sprint sidebar accepts dnd-kit drops from any board card. Drop = PATCH /api/issues/:id with sprint_id.',
       'medium', 5, ARRAY['type:feature','area:frontend']),
    ('Workflow', 'Drag issue onto an epic to assign it',
       'Epic chips on the board accept drops; mirrors the sprint-drag UX.',
       'medium', 5, ARRAY['type:feature','area:frontend']),
    ('Workflow', 'Bulk move issues between sprints',
       'Issues page: multi-select rows + "Move to sprint…" action that issues a single PATCH per id (parallel) and shows a toast on completion.',
       'medium', 5, ARRAY['type:feature','area:frontend']),
    ('Workflow', 'Sprint planning view: backlog ↔ sprint drag handoff',
       'New /p/:slug/plan route. Two columns: unscoped backlog on left, target sprint on right. Drop assigns sprint_id; drop back removes it.',
       'high', 8, ARRAY['type:feature','area:frontend','needs-design']),
    ('Workflow', 'Issue dependencies (blocked-by / blocking) graph',
       'New issue_dependencies table (issue_id, depends_on_id, kind). Backend CRUD + a small graph view in IssueModal showing both directions.',
       'medium', 8, ARRAY['type:feature','area:backend','area:db','area:frontend']),
    ('Workflow', 'Sub-issues / parent issue',
       'Add issues.parent_id self-FK; backend list/expand endpoints; frontend nesting in lists and on the board.',
       'medium', 8, ARRAY['type:feature','area:backend','area:db','area:frontend']),
    ('Workflow', 'Estimate poker (multi-vote on points)',
       'Per-issue voting session — members submit a points estimate; reveal aggregates median + range and writes it to story_points.',
       'low', 13, ARRAY['type:feature','area:backend','area:frontend','needs-design']),

    -- Collaboration
    ('Collaboration', 'Comments on issues (UI)',
       'Backend tables already exist (comments, mentions). Build CommentsController + IssueModal thread UI with edit/delete, optimistic updates.',
       'high', 8, ARRAY['type:feature','area:backend','area:frontend']),
    ('Collaboration', '@mentions + per-user activity feed',
       'Parse @handles on comment write, populate mentions table, surface "mentioned me" feed at /me/activity.',
       'high', 8, ARRAY['type:feature','area:backend','area:frontend']),
    ('Collaboration', 'Email notifications (assigned, mentioned, sprint starts)',
       'Pluggable INotificationSender (start with logging sink). Trigger on assignee change, new mention, sprint status → active.',
       'medium', 8, ARRAY['type:feature','area:backend']),
    ('Collaboration', 'Project member invitations + role UI',
       'Settings → Members tab. Invite by email (creates pending user or links existing), role dropdown (owner/member), remove member.',
       'high', 5, ARRAY['type:feature','area:backend','area:frontend']),
    ('Collaboration', 'Audit log per project',
       'Read-only feed view of activities table with filters (actor, kind, date range). The table already exists; this is the UI + a list endpoint.',
       'medium', 5, ARRAY['type:feature','area:frontend']),

    -- Reporting
    ('Reporting', 'Burndown chart (per active sprint)',
       'Daily snapshot of remaining points: SQL window over issue close events. Recharts area chart on SprintDetailPage.',
       'high', 8, ARRAY['type:feature','area:backend','area:frontend']),
    ('Reporting', 'Cumulative flow diagram',
       'Per-column counts over time, stacked area chart on a new /p/:slug/reports/cfd page.',
       'medium', 8, ARRAY['type:feature','area:backend','area:frontend']),
    ('Reporting', 'Lead time / cycle time histograms',
       'created_at → closed_at (lead) and first move-to-In-Progress → closed_at (cycle). Histogram + p50/p90 callouts.',
       'medium', 5, ARRAY['type:feature','area:backend','area:frontend']),
    ('Reporting', 'Per-assignee throughput',
       'Issues closed per user per sprint. Stacked bar chart on the team-velocity page.',
       'low', 5, ARRAY['type:feature','area:backend','area:frontend']),
    ('Reporting', 'Epic burnup',
       'Total scope vs completed scope over time, per epic. Reuses EpicWithProgress shape with a daily snapshot.',
       'medium', 5, ARRAY['type:feature','area:backend','area:frontend']),
    ('Reporting', 'Release planning (group sprints into releases)',
       'New releases table + many-to-one sprints.release_id. Release page aggregates velocity + scope across child sprints.',
       'low', 8, ARRAY['type:feature','area:backend','area:db','area:frontend']),

    -- UX polish
    ('UX polish', 'Keyboard shortcut palette (? to open)',
       'Cmd-palette style overlay listing all shortcuts; "?" opens it. Uses existing useEscapeKey for dismiss.',
       'medium', 3, ARRAY['type:feature','area:frontend','good-first-issue']),
    ('UX polish', 'Issue search: full-text upgrade',
       'Replace ILIKE with a tsvector + GIN index on (title, description). Keep ILIKE fallback for partial matches under 3 chars.',
       'medium', 5, ARRAY['type:tech-debt','area:backend','area:db']),
    ('UX polish', 'Saved board filters',
       'Persist {epic,sprint,assignee,label,search} combos to a per-user table; quick-pick row above the board.',
       'low', 5, ARRAY['type:feature','area:backend','area:frontend']),
    ('UX polish', 'Dark/light theme toggle',
       'CSS vars + Tailwind class strategy switch; remember choice in localStorage; respect prefers-color-scheme on first load.',
       'low', 5, ARRAY['type:feature','area:frontend','needs-design']),
    ('UX polish', 'Markdown rendering in issue descriptions',
       'Render with react-markdown + rehype-sanitize. Edit mode stays plain textarea; preview tab toggles render.',
       'high', 3, ARRAY['type:feature','area:frontend']),
    ('UX polish', 'Drag-and-drop file/image attachments',
       'New attachments table + object storage adapter (start with on-disk). Drop zone in IssueModal, inline previews for images.',
       'medium', 8, ARRAY['type:feature','area:backend','area:frontend','area:db']),
    ('UX polish', 'Replace confirm() with custom dialog',
       'Outstanding AUDIT-2026-05 item. Reusable <ConfirmDialog/> component; replace every window.confirm call site.',
       'medium', 3, ARRAY['type:tech-debt','area:frontend','good-first-issue']),

    -- Integrations
    ('Integrations', 'GitHub: link issue ↔ PR, auto-close on merge',
       'Webhook receiver at /webhooks/github. PR body / commit message keywords (Fixes #ID) link to issues; on merged PR, close linked issues.',
       'medium', 8, ARRAY['type:feature','area:backend']),
    ('Integrations', 'GitHub: status updates from PR labels',
       'PR labels map to column moves (e.g. "in review" → In Review column). Configurable per project.',
       'low', 5, ARRAY['type:feature','area:backend']),
    ('Integrations', 'Slack: sprint events to a channel',
       'Per-project Slack webhook URL. Post on sprint start / end with summary + velocity.',
       'low', 5, ARRAY['type:feature','area:backend']),
    ('Integrations', 'Webhooks (outgoing)',
       'Per-project subscription on event types (issue.created, issue.closed, sprint.completed, …). Signed payloads with HMAC.',
       'low', 8, ARRAY['type:feature','area:backend']),
    ('Integrations', 'iCal feed of sprint dates',
       'Public per-project ICS feed (signed token in URL). Each sprint = one VEVENT spanning start_date → end_date.',
       'low', 3, ARRAY['type:feature','area:backend','good-first-issue']),

    -- Platform
    ('Platform', 'Custom column definitions per board',
       'Settings → Columns tab: rename, reorder, add, archive. Default columns become just the seed.',
       'medium', 5, ARRAY['type:feature','area:frontend']),
    ('Platform', 'Multiple boards per project',
       'Schema already supports it (boards.project_id) — needs a board switcher and per-board issue scoping.',
       'low', 8, ARRAY['type:feature','area:backend','area:frontend']),
    ('Platform', 'Per-column WIP limits',
       'columns.wip_limit INTEGER NULL. Visual warning + block on drop when exceeded (configurable strict/soft).',
       'medium', 5, ARRAY['type:feature','area:db','area:backend','area:frontend']),
    ('Platform', 'Workflow rules',
       'Per-project rule engine: trigger (column move, status change) → action (close, assign, label). Stored as JSON, evaluated server-side.',
       'low', 13, ARRAY['type:feature','area:backend','needs-design']),
    ('Platform', 'Time tracking (hours logged per issue)',
       'New time_entries table (issue_id, user_id, minutes, note, logged_at). UI panel in IssueModal + per-sprint totals.',
       'low', 8, ARRAY['type:feature','area:backend','area:db','area:frontend']),
    ('Platform', 'Public project sharing (read-only link)',
       'Signed token in URL grants read-only board view (no auth required, no edit endpoints exposed).',
       'low', 5, ARRAY['type:feature','area:backend','area:frontend']);

  -- Now materialize: insert each row into issues, then attach labels.
  INSERT INTO issues (project_id, column_id, epic_id, title, description, priority, story_points, position)
  SELECT v_project,
         v_backlog,
         e.id,
         s.title,
         s.description,
         s.priority,
         s.points,
         ROW_NUMBER() OVER (PARTITION BY s.epic_title ORDER BY s.title) - 1
  FROM _seed_issues s
  JOIN epics e ON e.project_id = v_project AND e.title = s.epic_title
  -- Skip rows that already exist (by title within this project) so reruns are safe.
  WHERE NOT EXISTS (
    SELECT 1 FROM issues i
    WHERE i.project_id = v_project AND i.title = s.title
  );

  -- Link labels for the issues we (just) created.
  INSERT INTO issue_labels (issue_id, label_id)
  SELECT i.id, l.id
  FROM _seed_issues s
  JOIN issues i ON i.project_id = v_project AND i.title = s.title
  CROSS JOIN LATERAL unnest(s.labels) AS lbl(name)
  JOIN labels l ON l.project_id = v_project AND LOWER(l.name) = LOWER(lbl.name)
  ON CONFLICT DO NOTHING;
END
$seed$;

COMMIT;

-- Quick verification.
SELECT 'project'  AS kind, COUNT(*) FROM projects     WHERE slug = 'flowboard'
UNION ALL SELECT 'epics',  COUNT(*) FROM epics  WHERE project_id = (SELECT id FROM projects WHERE slug='flowboard')
UNION ALL SELECT 'issues', COUNT(*) FROM issues WHERE project_id = (SELECT id FROM projects WHERE slug='flowboard')
UNION ALL SELECT 'labels', COUNT(*) FROM labels WHERE project_id = (SELECT id FROM projects WHERE slug='flowboard')
UNION ALL SELECT 'links',  COUNT(*) FROM issue_labels il JOIN issues i ON i.id = il.issue_id WHERE i.project_id = (SELECT id FROM projects WHERE slug='flowboard');
