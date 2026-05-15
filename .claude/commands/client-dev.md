---
description: Start the React client (Vite) on http://localhost:5173.
---

```powershell
Set-Location client
npm install
npm run dev
```

Reads `VITE_API_URL` from `client/.env.local` (default `http://localhost:8080/api`). The API must already be running — use `/run-api` in another terminal first.
