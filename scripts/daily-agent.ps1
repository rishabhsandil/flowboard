# FlowBoard Daily Agent
# Runs at 2 AM via Windows Task Scheduler.
# Picks up "In Progress" items, implements them, runs the post-implementation
# checklist, then moves each completed item to "In Review".
#
# Credentials live in scripts/.env.local (gitignored) — never in this file.
#
# Run -DryRun to preview what would happen (logs the In Progress items and the
# prompt that would be sent) without invoking Claude.

[CmdletBinding()]
param(
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Paths ────────────────────────────────────────────────────────────────────
$repoRoot   = 'C:\Users\Rish\Desktop\Personal GIT\FlowBoard'
$logDir     = Join-Path $repoRoot 'scripts\logs'
$envFile    = Join-Path $repoRoot 'scripts\.env.local'
$claudeExe  = 'C:\Users\Rish\.local\bin\claude.exe'
$dotnetExe  = 'C:\Program Files\dotnet\dotnet.exe'
$apiBase    = 'http://localhost:8080'
$projectId  = 'b79e49d3-0497-4c5d-b205-f39d0684374e'   # FlowBoard project
$inProgress = 'a66f8119-c876-442c-9e32-d24ca9954870'   # In Progress column
$inReview   = '1d06c31d-1d07-4850-8f8c-74654319ad7f'   # In Review column

New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$today     = Get-Date -Format 'yyyy-MM-dd'
$logFile   = Join-Path $logDir "agent-$today.log"
$debugFile = Join-Path $logDir "claude-debug-$today.log"
$apiLog    = Join-Path $logDir "api-$today.log"

function Log { param([string]$msg)
    $ts = Get-Date -Format 'HH:mm:ss'
    $line = "$ts  $msg"
    Add-Content -Path $logFile -Value $line -Encoding utf8
    Write-Host $line
}

Log '=== FlowBoard daily agent starting ==='
if ($DryRun) { Log 'MODE: dry-run (will not invoke Claude)' }

# ── Load credentials ────────────────────────────────────────────────────────
if (-not (Test-Path $envFile)) {
    Log "ERROR: $envFile not found. Need FLOWBOARD_EMAIL, FLOWBOARD_PASSWORD, FLOWBOARD_TEST_DB."
    exit 1
}
foreach ($line in Get-Content $envFile) {
    if ($line -match '^\s*#' -or $line -match '^\s*$') { continue }
    $key, $val = $line -split '=', 2
    [System.Environment]::SetEnvironmentVariable($key.Trim(), $val.Trim(), 'Process')
}
$fbEmail    = $env:FLOWBOARD_EMAIL
$fbPassword = $env:FLOWBOARD_PASSWORD
if (-not $fbEmail -or -not $fbPassword) {
    Log 'ERROR: FLOWBOARD_EMAIL or FLOWBOARD_PASSWORD missing in .env.local'
    exit 1
}

# ── Ensure FlowBoard.Api is up ──────────────────────────────────────────────
function Test-ApiUp {
    try { (Invoke-WebRequest -Uri "$apiBase/api/health" -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop).StatusCode -eq 200 }
    catch { $false }
}

if (-not (Test-ApiUp)) {
    Log 'API not reachable — starting FlowBoard.Api…'
    Get-Process FlowBoard.Api -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Process -FilePath $dotnetExe `
        -ArgumentList 'run','--project','src/FlowBoard.Api','--no-build' `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $apiLog `
        -RedirectStandardError "$apiLog.err"

    $deadline = (Get-Date).AddSeconds(45)
    while (-not (Test-ApiUp)) {
        if ((Get-Date) -gt $deadline) {
            Log 'ERROR: API did not come up within 45s. Aborting.'
            exit 2
        }
        Start-Sleep -Seconds 2
    }
    Log 'API is up.'
} else {
    Log 'API already running.'
}

# ── Log in directly from PowerShell so the agent gets a ready token ────────
$loginBody = @{ email = $fbEmail; password = $fbPassword } | ConvertTo-Json -Compress
try {
    $loginResp = Invoke-RestMethod -Uri "$apiBase/api/auth/login" -Method Post `
        -Body $loginBody -ContentType 'application/json' `
        -Headers @{ 'X-Requested-With' = 'XMLHttpRequest' } -ErrorAction Stop
} catch {
    Log "ERROR: login failed: $($_.Exception.Message)"
    exit 3
}
$token = $loginResp.accessToken
if (-not $token) {
    Log 'ERROR: login succeeded but no accessToken in response.'
    exit 3
}
Log 'Login OK.'

# ── Pre-flight: how many In Progress items? ─────────────────────────────────
$auth = @{ Authorization = "Bearer $token" }
$board = Invoke-RestMethod -Uri "$apiBase/api/projects/$projectId/board" -Headers $auth -ErrorAction Stop
$inProgressIssues = @(($board.columns | Where-Object { $_.id -eq $inProgress }).issues)

Log "In Progress count: $($inProgressIssues.Count)"
if ($inProgressIssues.Count -eq 0) {
    Log 'No In Progress items — nothing to do. Exiting cleanly.'
    Log '=== Agent finished ==='
    exit 0
}

foreach ($i in $inProgressIssues) {
    Log "  • $($i.title)  [id=$($i.id)]"
}

# ── Build prompt ────────────────────────────────────────────────────────────
$issueList = ($inProgressIssues | ForEach-Object { "- $($_.title)  (id: $($_.id))" }) -join "`n"

# Single-quoted heredoc — no PS interpolation, no escaping headaches.
# Placeholders below are substituted with .Replace() after the close.
$promptTemplate = @'
You are running FULLY UNATTENDED at 2 AM as an automated agent. There is
nobody at the keyboard. NEVER ask a question, NEVER pause for input. If
you face ambiguity, make the most reasonable decision, document it on the
ticket as a comment, and keep going. If something fails unrecoverably,
log it clearly and move to the next issue.

You are working on FlowBoard — a ZenHub/Jira clone built with ASP.NET
Core 10 + C# 14 (raw SQL via Dapper + Npgsql, no EF) and React 18 + TS +
Vite + Tailwind + Zustand + TanStack Query.

Working directory: {{REPO_ROOT}}
Read .claude/skills/coding-standards/SKILL.md FIRST — mandatory rules
for authorization, validation, Dapper type mapping, error handling,
query keys, and the empty-Guid sentinel. Also read CLAUDE.md.

PRE-FLIGHT (already done by the wrapper)
- FlowBoard.Api is running on {{API_BASE}}
- You are already logged in. Use this token on every API call:
    Authorization: Bearer {{TOKEN}}
- Every mutating call also needs: X-Requested-With: XMLHttpRequest

STEP 1 — Issues to implement
Project id:      {{PROJECT_ID}}
In Progress col: {{IN_PROGRESS_COL}}
In Review col:   {{IN_REVIEW_COL}}

There are {{ISSUE_COUNT}} issue(s) currently In Progress:

{{ISSUE_LIST}}

For each, fetch detail:
  GET {{API_BASE}}/api/issues/{issueId}

Treat title + description as your spec. If the issue has sub-issues
(children), implement each child first, then close the parent only
after every child is closed. Children:
  GET {{API_BASE}}/api/issues/{issueId}/children

STEP 2 — Implement
Implement the issue end-to-end following the standards. After each
issue, run the post-implementation checklist in this order:

  2a. Build (release):
      & "{{DOTNET_EXE}}" build FlowBoard.sln -nologo -c Release

  2b. Backend tests (FLOWBOARD_TEST_DB is already in env):
      & "{{DOTNET_EXE}}" test FlowBoard.sln --nologo

  2c. Frontend lint:
      Set-Location "{{REPO_ROOT}}\client"
      npm run lint

  2d. Playwright e2e (only if Vite is reachable on :5173):
      try { Invoke-WebRequest http://localhost:5173 -UseBasicParsing -TimeoutSec 3 } catch { return }
      If reachable: npx playwright test

  2e. Update only docs that actually changed:
      README.md, docs/FEATURES.md, docs/queries.md, CLAUDE.md.

If any step fails after 5 fix attempts, leave the issue In Progress,
add a comment summarising what was done vs. what remains, and continue.
Do not run git commit or git push — the human commits themselves.

STEP 3 — Move completed issues to In Review
For each issue whose checklist all passed:

  PATCH {{API_BASE}}/api/issues/{issueId}
  Headers: Authorization: Bearer {{TOKEN}}
           Content-Type: application/json
           X-Requested-With: XMLHttpRequest
  Body:    { "columnId": "{{IN_REVIEW_COL}}" }

END-OF-RUN SUMMARY
At the very end, print a single block to stdout with this exact format
(the wrapper scrapes for it):

[DAILY-AGENT-SUMMARY]
moved_to_review: <comma-separated issue ids, or none>
left_in_progress: <comma-separated issue ids, or none>
errors: <one-line summary, or none>
[/DAILY-AGENT-SUMMARY]
'@

$prompt = $promptTemplate
$prompt = $prompt.Replace('{{REPO_ROOT}}',       $repoRoot)
$prompt = $prompt.Replace('{{API_BASE}}',        $apiBase)
$prompt = $prompt.Replace('{{TOKEN}}',           $token)
$prompt = $prompt.Replace('{{PROJECT_ID}}',      $projectId)
$prompt = $prompt.Replace('{{IN_PROGRESS_COL}}', $inProgress)
$prompt = $prompt.Replace('{{IN_REVIEW_COL}}',   $inReview)
$prompt = $prompt.Replace('{{ISSUE_COUNT}}',     [string]$inProgressIssues.Count)
$prompt = $prompt.Replace('{{ISSUE_LIST}}',      $issueList)
$prompt = $prompt.Replace('{{DOTNET_EXE}}',      $dotnetExe)

Log ("Prompt built: {0} chars covering {1} issue(s)." -f $prompt.Length, $inProgressIssues.Count)

if ($DryRun) {
    $promptPreview = Join-Path $logDir "dryrun-prompt-$today.txt"
    Set-Content -Path $promptPreview -Value $prompt -Encoding utf8
    Log "DRY RUN: prompt written to $promptPreview - not invoking Claude."
    Log '=== Agent finished ==='
    exit 0
}

# ── Invoke Claude Code via stdin (more reliable than long -p arg) ──────────
Log 'Launching Claude Code agent (debug log: claude-debug log)…'
Set-Location $repoRoot

# Pipe the prompt via stdin; this avoids the "no stdin data received in 3s"
# warning and any quoting weirdness with long multi-line CLI arguments.
$stdout = $prompt | & $claudeExe `
    --print `
    --dangerously-skip-permissions `
    --debug-file $debugFile `
    --output-format text 2>&1
$exit = $LASTEXITCODE

"---claude stdout---"           | Add-Content -Path $logFile -Encoding utf8
$stdout                          | Out-File   -FilePath $logFile -Append -Encoding utf8
"---claude exit: $exit ---"     | Add-Content -Path $logFile -Encoding utf8

Log "Claude exited: $exit"
Log '=== Agent finished ==='
exit $exit
