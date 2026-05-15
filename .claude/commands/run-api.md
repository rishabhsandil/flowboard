---
description: Start the FlowBoard API in Development mode on http://localhost:8080.
argument-hint: (no arguments)
---

Run the API in the foreground. The user expects this to keep running until they Ctrl+C.

```powershell
Get-Process FlowBoard.Api -ErrorAction SilentlyContinue | Stop-Process -Force
$env:ASPNETCORE_ENVIRONMENT='Development'
& "C:\Program Files\dotnet\dotnet.exe" run --project src/FlowBoard.Api --no-build
```

If the build is stale, run `/build` first.

When the API is up you should see `Now listening on: http://localhost:8080`. Swagger is at `http://localhost:8080/swagger`.
