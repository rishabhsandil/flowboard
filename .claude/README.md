# `.claude/` — Claude Code config for FlowBoard

This folder configures Claude Code's behavior for this repo. Everything here is committed except `settings.local.json` (per-developer overrides).

## Layout

```
.claude/
├── settings.json           Shared permissions + env (committed)
├── settings.local.json     Per-dev overrides (gitignored)
├── skills/                 Reusable knowledge packs Claude reads on demand
│   ├── coding-standards/SKILL.md   ← MANDATORY rules; read before coding
│   ├── dapper-records/SKILL.md
│   └── new-endpoint/SKILL.md
├── agents/                 Specialized subagents Claude can delegate to
│   ├── sql-reviewer.md
│   └── dapper-doctor.md
└── commands/               Slash commands runnable from the chat
    ├── build.md
    ├── run-api.md
    ├── test-endpoints.md
    ├── client-dev.md
    └── db-reset.md
```

The repo-level memory file is `../CLAUDE.md` at the project root — it's loaded automatically on session start.

## Slash commands

| Command | Purpose |
|---|---|
| `/build` | Kill running API and rebuild the solution |
| `/run-api` | Start the API on `:8080` (Development) |
| `/client-dev` | Start the Vite client on `:5173` |
| `/test-endpoints` | Smoke-test register → project → epic → sprint → velocity |
| `/db-reset` | Drop and recreate local Postgres from `schema.sql` (destructive) |

## Subagents

| Agent | When to use |
|---|---|
| `sql-reviewer` | Proactively after any `Data/Queries/*.cs` change |
| `dapper-doctor` | When an endpoint 500s with a Dapper materialization stack trace |

Invoke a subagent with `> use the sql-reviewer subagent to review my latest changes`.

## Skills

Skills are loaded on demand by Claude based on their `description` frontmatter. You don't invoke them explicitly — describe the problem and the matching skill is pulled in.

- `coding-standards` — **mandatory** project-wide rules (auth, validation, Dapper, modals, query keys). Always consult before editing code.
- `dapper-records` — fix Dapper 2.1.66 / Npgsql 9 record-materialization errors.
- `new-endpoint` — full end-to-end checklist for adding a new REST endpoint.

## Adding more

- **A new slash command:** drop a `name.md` into `commands/` with optional YAML frontmatter (`description`, `argument-hint`, `allowed-tools`).
- **A new subagent:** drop a `name.md` into `agents/` with required frontmatter (`name`, `description`, `tools`, optional `model`).
- **A new skill:** create `skills/<kebab-name>/SKILL.md` with frontmatter (`name`, `description`). The description should describe both *what* the skill does and *when* to use it — that's how Claude decides to load it.
