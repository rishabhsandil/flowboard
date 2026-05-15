---
description: Kill running API, then build the FlowBoard solution.
allowed-tools: Bash(Get-Process*), Bash(Stop-Process*), Bash(C:\\Program Files\\dotnet\\dotnet.exe build*)
---

Stop any running API, then build:

!`Get-Process FlowBoard.Api -ErrorAction SilentlyContinue | Stop-Process -Force`

!`& "C:\Program Files\dotnet\dotnet.exe" build FlowBoard.sln -nologo`

Report build success/failure and any warnings.
