---
description: Drop and recreate the local flowboard database from schema.sql.
argument-hint: (no arguments — destructive!)
---

> ⚠️ **Destructive.** Wipes all local FlowBoard data. Confirm with the user before running.

```powershell
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
& $psql -U postgres -c "DROP DATABASE IF EXISTS flowboard;"
& $psql -U postgres -c "CREATE DATABASE flowboard;"
& $psql -U postgres -d flowboard -f schema.sql
```

After completion, run `/build` then `/run-api` and `/test-endpoints` to verify a clean state.
