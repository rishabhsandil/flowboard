---
description: Smoke-test the core FlowBoard endpoints (auth → project → epic → sprint → velocity).
---

Assumes the API is running on `http://localhost:8080`. Registers a fresh user, creates a project, an epic, a sprint, lists each, and fetches velocity. Any 500 response should be diagnosed with the `dapper-doctor` subagent.

```powershell
$email = 'smoke.' + [guid]::NewGuid().ToString('N').Substring(0,8) + '@example.com'
$reg = Invoke-RestMethod -Uri 'http://localhost:8080/api/auth/register' -Method Post -ContentType 'application/json' `
  -Body (@{ name='Smoke'; email=$email; password='Passw0rd!234' } | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($reg.accessToken)" }

$proj = Invoke-RestMethod -Uri 'http://localhost:8080/api/projects' -Method Post -ContentType 'application/json' `
  -Headers $headers -Body (@{ name='Smoke Test'; description='x' } | ConvertTo-Json)
$pid_ = $proj.project.id

$epic = Invoke-RestMethod -Uri "http://localhost:8080/api/projects/$pid_/epics" -Method Post -ContentType 'application/json' `
  -Headers $headers -Body (@{ title='E1'; color='#ff0000'; startDate='2026-05-01'; dueDate='2026-06-01' } | ConvertTo-Json)

$sprint = Invoke-RestMethod -Uri "http://localhost:8080/api/projects/$pid_/sprints" -Method Post -ContentType 'application/json' `
  -Headers $headers -Body (@{ name='S1'; startDate='2026-05-01'; endDate='2026-05-15' } | ConvertTo-Json)

$epicsList   = Invoke-RestMethod -Uri "http://localhost:8080/api/projects/$pid_/epics"   -Headers $headers
$sprintsList = Invoke-RestMethod -Uri "http://localhost:8080/api/projects/$pid_/sprints" -Headers $headers
$velocity    = Invoke-RestMethod -Uri "http://localhost:8080/api/sprints/$($sprint.sprint.id)/velocity" -Headers $headers

@{
  epicCreate=$epic; sprintCreate=$sprint;
  epicsList=$epicsList; sprintsList=$sprintsList; velocity=$velocity
} | ConvertTo-Json -Depth 8
```

Expected: each call returns 200 and the final hashtable serializes cleanly. If any call throws, capture `$_.Exception.Response.StatusCode.value__` and `$_.ErrorDetails.Message`, plus the API console for the stack trace, and invoke the `dapper-doctor` subagent.
