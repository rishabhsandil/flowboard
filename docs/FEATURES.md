# FlowBoard — Feature Checklist

Tracks how close FlowBoard is to a ZenHub-like feature set. Items are grouped by capability; check them off as they ship.

## ✅ Already built

- [x] Email/password auth with JWT access + refresh tokens
- [x] Multi-project workspace with role (`owner` / `member`)
- [x] Board view with columns + drag-and-drop issue reorder (`@dnd-kit`)
- [x] Issue create / edit / delete / close
- [x] Issue priority (low / medium / high / critical), story points, description
- [x] Epics list with progress aggregation (issues closed, points completed, % complete)
- [x] Sprints list with start/end dates and status (`planned` / `active` / `completed`)
- [x] Velocity chart (per-sprint bar + cumulative line, only `completed` sprints)
- [x] Sprint detail page (velocity + sprint issue list)
- [x] Swagger UI in production
- [x] Raw SQL via Dapper + Npgsql (no EF), parameterized

## 🚧 This iteration

### Epic management (frontend)
- [x] **Create epic** — modal with title, description, color picker, start/due dates
- [x] **Edit epic** — same modal in edit mode
- [x] **Delete epic** — confirm + DELETE `/api/epics/:id`
- [x] **Click epic row** to open editor

### Sprint management (frontend)
- [x] **Create sprint** — modal with name, goal, start/end dates, status
- [x] **Edit sprint** — same modal in edit mode
- [x] **Change sprint status** — planned → active → completed (drives velocity chart)
- [x] **Delete sprint**
- [x] **Click sprint row** to open editor

### Issue ↔ Epic / Sprint linking
- [x] **Backend**: allow clearing `epic_id` / `sprint_id` (sentinel-based update)
- [x] **CreateIssueModal**: epic + sprint dropdowns at creation
- [x] **IssueModal**: epic + sprint dropdowns on the side panel (edit live)

### Sprint detail
- [x] Header shows sprint name, dates, goal, status pill
- [x] Inline status changer (planned ↔ active ↔ completed)
- [x] Issue list (already there)

### Security
- [x] **CSRF protection middleware** — requires `X-Requested-With: XMLHttpRequest` on all mutating requests; `/api/auth/` paths exempt
- [x] **Security headers middleware** — `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy` on every response

### Sprint planning view
- [x] `/p/:slug/plan` — backlog (left) ↔ sprint (right) drag-and-drop assignment
- [x] Auto-selects active sprint; falls back to first planned sprint
- [x] Drag from backlog → sprint column: `PATCH /api/issues/{id}` sets `sprintId`
- [x] Drag from sprint → backlog: `PATCH /api/issues/{id}` with empty-Guid sentinel clears `sprintId`
- [x] Sprint selector dropdown showing all project sprints

### Cumulative flow diagram
- [x] `GET /api/projects/{id}/reports/cfd?days=N` — auto-upserts today's snapshot; returns `{ cfd: CfdPoint[] }`
- [x] `board_snapshots` table — one row per project×column×day, `ON CONFLICT DO UPDATE` for idempotency
- [x] `/p/:slug/reports/cfd` — stacked AreaChart (Recharts), day-range selector (7 / 14 / 30 / 60 / 90 days)
- [x] Empty state ("no data yet") until first snapshot accumulates

### Board enhancements
- [x] **Filter by epic** (dropdown; "all" / specific epic / "none")
- [x] **Filter by sprint** (`active sprint` quick filter, "all", "none", or specific sprint)

### Project-wide issue list
- [x] `/p/:slug/issues` page: paginated table with status / priority / epic / sprint / assignee / label / title-search filters
- [x] `GET /api/projects/{id}/issues` with empty-Guid sentinels for "no epic / sprint / assignee"
- [x] `GET /api/projects/{id}/members` for the assignee picker

### Settings
- [x] `/p/:slug/settings` shell with tab nav; **Labels** moved here from the top-level sidebar (legacy `/labels` URL redirects)
- [x] Placeholder **General** tab

### Error UX
- [x] Global toast host (`client/src/lib/toast.tsx`) wired into the axios interceptor — every non-401 backend error surfaces a humanized message; forms that render errors inline opt out with `silent: true`
- [x] `humanizeApiError(code)` maps known error codes (`label_name_exists`, `invalid_credentials`, …) to readable copy
- [x] Reusable `confirmDialog()` modal replaces every `window.confirm` (epic delete, sprint delete, issue delete, comment delete, label delete, member remove, bulk delete)

### Member management
- [x] Settings → **Members** tab: invite by email, change role (owner/member), remove, self-leave
- [x] Backend: `POST/PATCH/DELETE /api/projects/{id}/members[/{userId}]` with last-owner protection (`last_owner` error)
- [x] Activity feed records `member_added` / `member_role_changed` / `member_removed` / `member_left`

### Project management
- [x] Settings → **General** tab "danger zone": owner-only **delete project** with a type-the-word-`delete` confirmation modal
- [x] Backend: `DELETE /api/projects/{id}` (owner-only) cascades to the board, columns, issues, epics, sprints, labels, comments, mentions, activities, and board snapshots

### Activity & profile
- [x] Per-issue activity feed (in IssueModal) — already shipped
- [x] Project-wide activity page at `/p/:slug/activity` with type filter + pagination + click-through to issues
- [x] Profile page at `/profile` with name + avatar URL editing and a current-password-gated password change (revokes other refresh tokens)
- [x] **Forgot / reset password flow** — `POST /api/auth/forgot {email}` always returns 200 (no enumeration). The real work (token mint, DB write, email dispatch) runs in a background task so the response time is identical for known and unknown emails (timing-channel defence). On a known email, prior outstanding tokens are invalidated and a 32-byte URL-safe token is minted (SHA-256 hash stored in `password_reset_tokens`, 1 h TTL). A per-email cooldown (`Forgot:PerEmailCooldownSeconds`, default 60s) silently drops repeated requests so the user's inbox can't be flooded. The plaintext link is delivered via a pluggable `IEmailSender` — `LogEmailSender` in dev/tests, `ResendEmailSender` in prod (set `Email:Provider=resend` + `Email:ResendApiKey`). A periodic `PasswordResetCleanupService` deletes used/expired rows older than the retention window. `POST /api/auth/reset {token, newPassword}` runs the rotation inside a transaction (validates token, marks it used, invalidates sibling tokens, updates the password, revokes every refresh token for the user), logs distinct WARN lines per rejection reason for token-fishing telemetry, then fires a post-reset notification email so the account holder is alerted if a takeover just occurred. Pages: `/forgot`, `/reset` with 8-char minimum-password rule + confirm field; `/reset` strips the token from the URL on mount and installs a `<meta name="referrer" content="no-referrer">` so the secret can't leak via Referer.

### Issue dependencies
- [x] `issue_dependencies` table — directed `(issue_id, depends_on_id, kind)` with `kind IN ('blocks','relates')`, `(issue_id, depends_on_id)` PK, self-link `CHECK`, `ON DELETE CASCADE` on either FK; reverse-lookup index on `depends_on_id`
- [x] `GET /api/issues/{id}/dependencies` returns both directions (`outgoing` + `incoming`) in a single round-trip with the other issue's title / column / closed-at joined in
- [x] `POST /api/issues/{id}/dependencies` with `{ dependsOnId, kind }` — guards self-link (400 `dependency_self`), cross-project (400 `dependency_project_mismatch`), and 1-hop reverse-blocks cycles (409 `dependency_cycle`); idempotent on duplicate add
- [x] `DELETE /api/issues/{id}/dependencies/{dependsOnId}` — 204; activity row `dependency_removed`
- [x] `GET /api/issues/{id}/dependencies/search?q=…` — title `ILIKE` over the same project, excludes the source issue
- [x] **Jira-style UI panel** in IssueModal's main column below sub-issues: single "+ Add link" button opens a picker with a link-type dropdown (`blocks` / `is blocked by` / `relates to`) and debounced issue search; linked issues render as a flat list grouped by relationship label with one-click remove

### Comments + collaboration
- [x] Comments tab on IssueModal (create / edit / delete / @mention)

### Issues page polish
- [x] Bulk select with bulk actions: move to sprint, apply label, close, reopen, delete
- [x] Click a label chip on a row (or a board card) to deep-link into Issues filtered by that label
- [x] URL `?issue=:id` deep-links open the IssueModal directly (used by the activity feed)

### Epic detail
- [x] `/p/:slug/epics/:id` page with progress stats, attached issues, edit/delete + new-issue-in-epic shortcut

### Keyboard
- [x] Global command palette (`Cmd/Ctrl+K` or `?`) with project nav + project switcher

## 🔭 Future / stretch

### Workflow
- [ ] Drag issue onto a sprint card to assign it (sprint sidebar)
- [ ] Drag issue onto an epic to assign it
- [ ] Bulk move issues between sprints
- [x] Sprint planning view: backlog (left) ↔ sprint (right) drag handoff
- [x] Issue dependencies (blocked-by / blocking) graph
- [ ] Sub-issues / parent issue
- [ ] Estimate poker (multi-vote on points)

### Collaboration
- [x] Comments on issues
- [x] @mentions + per-user activity feed
- [ ] Email notifications (assigned, mentioned, sprint starts)
- [x] Project member invitations + role management UI
- [ ] Audit log per project

### Reporting
- [x] Burndown chart (per active sprint)
- [x] Cumulative flow diagram
- [ ] Lead time / cycle time histograms
- [ ] Per-assignee throughput
- [ ] Epic burnup
- [ ] Release planning (group sprints into releases)

### UX polish
- [x] Keyboard shortcut palette (`?` to open)
- [x] Issue search across project (title `ILIKE`; full-text upgrade still open)
- [ ] Saved board filters
- [ ] Dark/light theme toggle (currently dark only)
- [x] Markdown rendering in issue descriptions
- [ ] Drag-and-drop file/image attachments

### Integrations
- [ ] GitHub: link issue ↔ PR, auto-close on merge
- [ ] GitHub: status updates from PR labels
- [ ] Slack: sprint events to a channel
- [ ] Webhooks (outgoing) for external automations
- [ ] iCal feed of sprint dates

### Platform
- [ ] Custom column definitions per board (currently fixed defaults)
- [ ] Multiple boards per project
- [ ] Per-column WIP limits
- [ ] Workflow rules (auto-close on column move, auto-assign on status change)
- [ ] Time tracking (hours logged per issue)
- [ ] Public project sharing (read-only link)
