-- FlowBoard schema (Postgres 14+, tested on Neon)
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- ---------- USERS ----------
CREATE TABLE IF NOT EXISTS users (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  email TEXT UNIQUE NOT NULL,
  name TEXT NOT NULL,
  avatar_url TEXT,
  password_hash TEXT NOT NULL,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- ---------- PROJECTS ----------
CREATE TABLE IF NOT EXISTS projects (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name TEXT NOT NULL,
  slug TEXT UNIQUE NOT NULL,
  description TEXT,
  owner_id UUID REFERENCES users(id) ON DELETE SET NULL,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_members (
  project_id UUID REFERENCES projects(id) ON DELETE CASCADE,
  user_id UUID REFERENCES users(id) ON DELETE CASCADE,
  role TEXT NOT NULL DEFAULT 'member',
  joined_at TIMESTAMPTZ DEFAULT NOW(),
  PRIMARY KEY (project_id, user_id)
);

-- ---------- BOARDS / COLUMNS ----------
CREATE TABLE IF NOT EXISTS boards (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id UUID REFERENCES projects(id) ON DELETE CASCADE,
  name TEXT NOT NULL DEFAULT 'Main Board',
  created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS columns (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  board_id UUID REFERENCES boards(id) ON DELETE CASCADE,
  name TEXT NOT NULL,
  position INTEGER NOT NULL,
  is_done BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- ---------- EPICS ----------
CREATE TABLE IF NOT EXISTS epics (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id UUID REFERENCES projects(id) ON DELETE CASCADE,
  title TEXT NOT NULL,
  description TEXT,
  color TEXT DEFAULT '#22d3ee',
  start_date DATE,
  due_date DATE,
  created_at TIMESTAMPTZ DEFAULT NOW()
);

-- ---------- SPRINTS ----------
CREATE TABLE IF NOT EXISTS sprints (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id UUID REFERENCES projects(id) ON DELETE CASCADE,
  name TEXT NOT NULL,
  goal TEXT,
  start_date DATE NOT NULL,
  end_date DATE NOT NULL,
  status TEXT DEFAULT 'planned',
  created_at TIMESTAMPTZ DEFAULT NOW(),
  CHECK (end_date >= start_date)
);

-- ---------- ISSUES ----------
CREATE TABLE IF NOT EXISTS issues (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id UUID REFERENCES projects(id) ON DELETE CASCADE,
  column_id UUID REFERENCES columns(id) ON DELETE SET NULL,
  epic_id UUID REFERENCES epics(id) ON DELETE SET NULL,
  sprint_id UUID REFERENCES sprints(id) ON DELETE SET NULL,
  assignee_id UUID REFERENCES users(id) ON DELETE SET NULL,
  title TEXT NOT NULL,
  description TEXT,
  priority TEXT DEFAULT 'medium' CHECK (priority IN ('low','medium','high','critical')),
  story_points INTEGER DEFAULT 0 CHECK (story_points >= 0),
  position INTEGER NOT NULL DEFAULT 0,
  parent_id UUID REFERENCES issues(id) ON DELETE SET NULL,
  created_at TIMESTAMPTZ DEFAULT NOW(),
  updated_at TIMESTAMPTZ DEFAULT NOW(),
  closed_at TIMESTAMPTZ
);

-- ---------- INDEXES ----------
CREATE INDEX IF NOT EXISTS idx_issues_project_id     ON issues(project_id);
CREATE INDEX IF NOT EXISTS idx_issues_column_id      ON issues(column_id);
CREATE INDEX IF NOT EXISTS idx_issues_sprint_id      ON issues(sprint_id);
CREATE INDEX IF NOT EXISTS idx_issues_epic_id        ON issues(epic_id);
CREATE INDEX IF NOT EXISTS idx_issues_assignee_id    ON issues(assignee_id);  -- "issues assigned to me"
CREATE INDEX IF NOT EXISTS idx_issues_closed_at      ON issues(closed_at);
CREATE INDEX IF NOT EXISTS idx_issues_parent_id      ON issues(parent_id);
CREATE INDEX IF NOT EXISTS idx_columns_board_id      ON columns(board_id);
CREATE INDEX IF NOT EXISTS idx_boards_project_id     ON boards(project_id);
CREATE INDEX IF NOT EXISTS idx_sprints_project_id    ON sprints(project_id);
CREATE INDEX IF NOT EXISTS idx_epics_project_id      ON epics(project_id);
CREATE INDEX IF NOT EXISTS idx_project_members_user  ON project_members(user_id); -- ListForUser join

-- ---------- ADDITIONAL INTEGRITY ----------
-- Sprint status enum (CHECK is idempotent: re-runs are no-op if it already exists)
DO $$ BEGIN
  ALTER TABLE sprints
    ADD CONSTRAINT sprints_status_check
    CHECK (status IN ('planned','active','completed'));
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

-- Keep issues.updated_at fresh on UPDATE
CREATE OR REPLACE FUNCTION touch_updated_at() RETURNS trigger AS $$
BEGIN
  NEW.updated_at = NOW();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_issues_updated_at ON issues;
CREATE TRIGGER trg_issues_updated_at
BEFORE UPDATE ON issues
FOR EACH ROW EXECUTE FUNCTION touch_updated_at();

-- Automatically close/reopen issues when moved to/from a "Done" column
CREATE OR REPLACE FUNCTION sync_issue_closed_at() RETURNS trigger AS $$
DECLARE
  v_is_done BOOLEAN;
BEGIN
  IF (NEW.column_id IS DISTINCT FROM OLD.column_id) THEN
    SELECT is_done INTO v_is_done FROM columns WHERE id = NEW.column_id;
    IF v_is_done THEN
      NEW.closed_at = COALESCE(NEW.closed_at, NOW());
    ELSE
      NEW.closed_at = NULL;
    END IF;
  END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_sync_issue_closed_at ON issues;
CREATE TRIGGER trg_sync_issue_closed_at
BEFORE UPDATE ON issues
FOR EACH ROW EXECUTE FUNCTION sync_issue_closed_at();

-- Automatically rollup story points from children to parents.
-- Note: This is recursive; updating a grandchild updates the child, which updates the parent.
CREATE OR REPLACE FUNCTION rollup_issue_points() RETURNS trigger AS $$
DECLARE
  v_parent_id UUID;
BEGIN
  -- Which parent(s) need updating?
  IF (TG_OP = 'DELETE') THEN
    v_parent_id = OLD.parent_id;
  ELSIF (TG_OP = 'UPDATE') THEN
    -- If parent changed, we might need to update TWO parents (old and new).
    -- But for simplicity and to avoid multiple updates, we handle the NEW one
    -- and then the OLD one if it's different.
    IF (NEW.parent_id IS DISTINCT FROM OLD.parent_id OR NEW.story_points IS DISTINCT FROM OLD.story_points) THEN
      IF (OLD.parent_id IS NOT NULL) THEN
        UPDATE issues SET story_points = (SELECT COALESCE(SUM(story_points), 0) FROM issues WHERE parent_id = OLD.parent_id)
        WHERE id = OLD.parent_id;
      END IF;
      v_parent_id = NEW.parent_id;
    END IF;
  ELSE -- INSERT
    v_parent_id = NEW.parent_id;
  END IF;

  IF (v_parent_id IS NOT NULL) THEN
    UPDATE issues SET story_points = (SELECT COALESCE(SUM(story_points), 0) FROM issues WHERE parent_id = v_parent_id)
    WHERE id = v_parent_id;
  END IF;

  IF (TG_OP = 'DELETE') THEN RETURN OLD; END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_issues_rollup_points ON issues;
CREATE TRIGGER trg_issues_rollup_points
AFTER INSERT OR UPDATE OR DELETE ON issues
FOR EACH ROW EXECUTE FUNCTION rollup_issue_points();

-- Sync parent status to children (Close parent -> Close children).
CREATE OR REPLACE FUNCTION sync_parent_to_children_status() RETURNS trigger AS $$
BEGIN
  IF (NEW.closed_at IS DISTINCT FROM OLD.closed_at) THEN
    UPDATE issues
    SET closed_at = NEW.closed_at
    WHERE parent_id = NEW.id;
  END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_sync_parent_to_children_status ON issues;
CREATE TRIGGER trg_sync_parent_to_children_status
AFTER UPDATE ON issues
FOR EACH ROW EXECUTE FUNCTION sync_parent_to_children_status();

-- ---------- REFRESH TOKENS ----------
-- Server-side state for JWT refresh tokens. We still issue signed JWTs, but
-- the `jti` claim is also a row here. Revoke = set `revoked_at`. Rotation =
-- on refresh, mark the old row revoked + replaced_by, insert a new row.
-- That gives us logout + theft detection (if a revoked jti is presented,
-- treat the entire chain as compromised).
CREATE TABLE IF NOT EXISTS refresh_tokens (
  id           UUID PRIMARY KEY,                       -- = JWT jti
  user_id      UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  issued_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  expires_at   TIMESTAMPTZ NOT NULL,
  revoked_at   TIMESTAMPTZ,                            -- NULL = active
  replaced_by  UUID REFERENCES refresh_tokens(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_id ON refresh_tokens(user_id);

-- ---------- COMMENTS ----------
-- Free-form discussion threaded on an issue. `body` is plain text; @mentions
-- are extracted at write-time into the `mentions` table so we can index them
-- and notify users without re-parsing every comment on read.
CREATE TABLE IF NOT EXISTS comments (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  issue_id    UUID NOT NULL REFERENCES issues(id) ON DELETE CASCADE,
  author_id   UUID REFERENCES users(id) ON DELETE SET NULL,
  body        TEXT NOT NULL,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  edited      BOOLEAN NOT NULL DEFAULT FALSE
);
CREATE INDEX IF NOT EXISTS idx_comments_issue_id  ON comments(issue_id);
CREATE INDEX IF NOT EXISTS idx_comments_author_id ON comments(author_id);

DROP TRIGGER IF EXISTS trg_comments_updated_at ON comments;
CREATE TRIGGER trg_comments_updated_at
BEFORE UPDATE ON comments
FOR EACH ROW EXECUTE FUNCTION touch_updated_at();

-- ---------- LABELS ----------
-- Labels are project-scoped. The (project_id, name_lower) unique index enforces
-- case-insensitive uniqueness without forcing the displayed casing.
CREATE TABLE IF NOT EXISTS labels (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id  UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  name        TEXT NOT NULL,
  color       TEXT NOT NULL DEFAULT '#64748b',
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CHECK (color ~ '^#[0-9a-fA-F]{6}$')
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_labels_project_name_ci
  ON labels(project_id, LOWER(name));
CREATE INDEX IF NOT EXISTS idx_labels_project_id ON labels(project_id);

CREATE TABLE IF NOT EXISTS issue_labels (
  issue_id    UUID NOT NULL REFERENCES issues(id) ON DELETE CASCADE,
  label_id    UUID NOT NULL REFERENCES labels(id) ON DELETE CASCADE,
  added_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY (issue_id, label_id)
);
CREATE INDEX IF NOT EXISTS idx_issue_labels_label_id ON issue_labels(label_id);

-- ---------- MENTIONS ----------
-- One row per @user reference inside a comment. Lets us answer
-- "issues where I was mentioned" without scanning every comment body.
CREATE TABLE IF NOT EXISTS mentions (
  id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  comment_id         UUID NOT NULL REFERENCES comments(id) ON DELETE CASCADE,
  mentioned_user_id  UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (comment_id, mentioned_user_id)
);
CREATE INDEX IF NOT EXISTS idx_mentions_user_id ON mentions(mentioned_user_id);

-- ---------- ACTIVITY LOG ----------
-- Append-only audit feed for issue lifecycle events. `payload` is JSONB so we
-- can attach event-specific context (changed fields, added label, snippet of
-- a comment, etc.) without schema churn each time we add a new event type.
CREATE TABLE IF NOT EXISTS activities (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id   UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  issue_id     UUID REFERENCES issues(id) ON DELETE CASCADE,
  actor_id     UUID REFERENCES users(id) ON DELETE SET NULL,
  type         TEXT NOT NULL,
  payload      JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_activities_issue_id   ON activities(issue_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_activities_project_id ON activities(project_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_activities_actor_id   ON activities(actor_id);

-- ---------- BOARD SNAPSHOTS (Cumulative Flow Diagram) ----------
-- One row per (project, column, calendar day). Upserted each time the CFD
-- endpoint is called, so the chart accumulates data automatically over time.
-- The unique constraint makes the INSERT ... ON CONFLICT idempotent.
CREATE TABLE IF NOT EXISTS board_snapshots (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id   UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  column_id    UUID NOT NULL REFERENCES columns(id)  ON DELETE CASCADE,
  column_name  TEXT NOT NULL,
  issue_count  INT  NOT NULL DEFAULT 0,
  snapped_at   DATE NOT NULL DEFAULT CURRENT_DATE,
  UNIQUE (project_id, column_id, snapped_at)
);
CREATE INDEX IF NOT EXISTS idx_board_snapshots_project_date
  ON board_snapshots(project_id, snapped_at DESC);
