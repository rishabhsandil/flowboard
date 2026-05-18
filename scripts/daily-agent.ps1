# FlowBoard Daily Agent
# Runs at 2 AM via Windows Task Scheduler.
# Logs into FlowBoard, picks up In Progress items, implements them,
# runs the full post-implementation checklist, then moves each completed
# item to In Review for the owner to review the next morning.
#
# Credentials live in scripts/.env.local (gitignored) — never in this file.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Paths ────────────────────────────────────────────────────────────────────
$repoRoot  = "C:\Users\Rish\Desktop\Personal GIT\FlowBoard"
$logDir    = Join-Path $repoRoot "scripts\logs"
$envFile   = Join-Path $repoRoot "scripts\.env.local"
$claudeExe = "C:\Users\Rish\.local\bin\claude.exe"

New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$logFile = Join-Path $logDir "agent-$(Get-Date -Format 'yyyy-MM-dd').log"

function Log { param([string]$msg) $ts = Get-Date -Format 'HH:mm:ss'; "$ts  $msg" | Tee-Object -FilePath $logFile -Append }

Log "=== FlowBoard daily agent starting ==="

# ── Load credentials from .env.local (gitignored) ────────────────────────────
if (-not (Test-Path $envFile)) {
    Log "ERROR: $envFile not found. Create it with FLOWBOARD_EMAIL, FLOWBOARD_PASSWORD, FLOWBOARD_TEST_DB."
    exit 1
}
foreach ($line in Get-Content $envFile) {
    if ($line -match '^\s*#' -or $line -match '^\s*$') { continue }
    $key, $val = $line -split '=', 2
    [System.Environment]::SetEnvironmentVariable($key.Trim(), $val.Trim(), 'Process')
}

$fbEmail    = $env:FLOWBOARD_EMAIL
$fbPassword = $env:FLOWBOARD_PASSWORD
$env:FLOWBOARD_TEST_DB = $env:FLOWBOARD_TEST_DB

if (-not $fbEmail -or -not $fbPassword) {
    Log "ERROR: FLOWBOARD_EMAIL or FLOWBOARD_PASSWORD missing in .env.local"
    exit 1
}

# ── Self-contained prompt for Claude Code ─────────────────────────────────────
$prompt = @"
You are running FULLY UNATTENDED at 2 AM as an automated agent. There is
nobody at the keyboard. You must NEVER ask a question, NEVER pause for input,
and NEVER say "I need clarification before proceeding." If you face any
ambiguity, make the most reasonable decision yourself, document your choice
in the log output and on the ticket itself as a comment, and keep going. If
something fails unrecoverably, log the error clearly and move on to the next
issue — do not stop the entire run.

You are working autonomously on the FlowBoard project — a ZenHub/Jira clone
built with ASP.NET Core 10 + C# 14 (raw SQL via Dapper + Npgsql, no EF) and
React 18 + TypeScript + Vite + Tailwind + Zustand + TanStack Query.

Working directory: C:\Users\Rish\Desktop\Personal GIT\FlowBoard
Read CLAUDE.md in that directory first — it contains mandatory coding
conventions, Dapper gotchas, auth setup, and the full post-implementation
checklist that you MUST follow.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
STEP 1 — Log into FlowBoard
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
POST http://localhost:8080/api/auth/login
Content-Type: application/json
Body: {"email":"$fbEmail","password":"$fbPassword"}

Save the accessToken from the response — you need it for all subsequent calls.
If the server is not running, start it first:
  Get-Process FlowBoard.Api -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Process -FilePath "C:\Program Files\dotnet\dotnet.exe" ``
    -ArgumentList "run","--project","src/FlowBoard.Api","--no-build" ``
    -WorkingDirectory "C:\Users\Rish\Desktop\Personal GIT\FlowBoard" ``
    -NoNewWindow -RedirectStandardOutput "scripts\logs\api.log"
  Start-Sleep -Seconds 5

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
STEP 2 — Get In Progress issues
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
GET http://localhost:8080/api/projects/b79e49d3-0497-4c5d-b205-f39d0684374e/board
Authorization: Bearer <accessToken>

Find the column named "In Progress" (ID: a66f8119-c876-442c-9e32-d24ca9954870).
Collect all issues in that column. If the column is empty, log "No In Progress
items — nothing to do" and stop.

For each issue you need more detail (description, story points), call:
GET http://localhost:8080/api/projects/b79e49d3-0497-4c5d-b205-f39d0684374e/issues/{issueId}
Authorization: Bearer <accessToken>

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
STEP 3 — Implement each issue
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
For each In Progress issue, implement it fully using the title and description
as your spec. Follow CLAUDE.md conventions strictly. Then run the mandatory
post-implementation checklist (in this exact order):

  3a. Build to confirm it compiles:
      & "C:\Program Files\dotnet\dotnet.exe" build FlowBoard.sln --nologo -c Release

  3b. Backend unit + integration tests (set env var first):
      `$env:FLOWBOARD_TEST_DB = '$($env:FLOWBOARD_TEST_DB)'`
      & "C:\Program Files\dotnet\dotnet.exe" test FlowBoard.sln --nologo
      All tests must be green. Fix failures before continuing.

  3c. Frontend lint:
      Set-Location "C:\Users\Rish\Desktop\Personal GIT\FlowBoard\client"
      npm run lint
      Fix every ESLint error before continuing.

  3d. Playwright e2e tests (only if both API and Vite dev server are reachable):
      Check: Invoke-WebRequest http://localhost:5173 -UseBasicParsing -ErrorAction SilentlyContinue
      If reachable:
        Set-Location "C:\Users\Rish\Desktop\Personal GIT\FlowBoard\client"
        npx playwright test
        All tests must pass.
      If not reachable: log "Vite not running — skipping Playwright" and continue.

  3e. Update docs (only what actually changed):
      - README.md (new routes, env vars, user-visible features)
      - docs/FEATURES.md (mark the feature [x])
      - docs/queries.md (new or changed SQL with annotations)
      - CLAUDE.md (new conventions or Dapper gotchas discovered)

If the issue is too large to complete in one session, leave it In Progress,
note what was done vs. what remains in a comment on the issue, and move on
to the next one.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
STEP 4 — Move completed issues to In Review
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
For each successfully implemented issue (all checklist steps passed):

PATCH http://localhost:8080/api/issues/{issueId}
Authorization: Bearer <accessToken>
Content-Type: application/json
X-Requested-With: XMLHttpRequest
Body: {"columnId":"1d06c31d-1d07-4850-8f8c-74654319ad7f"}

In Review column ID: 1d06c31d-1d07-4850-8f8c-74654319ad7f

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
IMPORTANT REMINDERS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
- Every mutating API call needs header: X-Requested-With: XMLHttpRequest
- Never use EF Core. Always raw SQL via Dapper in src/FlowBoard.Core/Data/Queries/*.cs
- Use DateTime (not DateOnly) in result records for DATE/TIMESTAMPTZ columns
- Use long (not int) for COUNT/SUM aggregates
- Empty-Guid sentinel 00000000-0000-0000-0000-000000000000 clears nullable FKs
- Authorization for every project-scoped endpoint: ProjectAuthorizer.IsMemberAsync
- Read the issue description carefully before coding — it is your spec
- If unsure about scope, implement the minimal working version that satisfies the title/description
- NEVER ask a question. NEVER wait for input. Make a decision and log it.
- NEVER run git commit or git push — the user always commits themselves
- If a test fails after 5 fix attempts, log the failure, leave the issue In Progress, and continue
- If the API is unreachable and won't start, log it and exit cleanly
"@

# ── Invoke Claude Code in headless print mode ─────────────────────────────────
Log "Launching Claude Code agent..."

Set-Location $repoRoot

$output = & $claudeExe --print --dangerously-skip-permissions -p $prompt 2>&1

$output | Out-File -FilePath $logFile -Append -Encoding utf8

Log "=== Agent finished ==="
