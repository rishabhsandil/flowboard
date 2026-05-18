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

### Activity & profile
- [x] Per-issue activity feed (in IssueModal) — already shipped
- [x] Project-wide activity page at `/p/:slug/activity` with type filter + pagination + click-through to issues
- [x] Profile page at `/profile` with name + avatar URL editing and a current-password-gated password change (revokes other refresh tokens)

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
- [ ] Sprint planning view: backlog (left) ↔ sprint (right) drag handoff
- [ ] Issue dependencies (blocked-by / blocking) graph
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
- [ ] Cumulative flow diagram
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
