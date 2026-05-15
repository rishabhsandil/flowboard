---
name: new-endpoint
description: Add a new project-scoped REST endpoint to FlowBoard end-to-end (SQL → record → DTO → controller → client API → store/query). USE WHEN the user asks to add, expose, or wire up a new API endpoint, list view, or mutation that touches the FlowBoard backend and React client.
---

# Skill: Add a new endpoint end-to-end

Follow this order. Skipping a step usually means a runtime crash or a frontend that can't call the route.

## 1. SQL

Add a `const string` to the appropriate `src/FlowBoard.Core/Data/Queries/<Entity>Queries.cs`. Use `RETURNING *` (or explicit columns) so writes hydrate the record. Snake_case columns map to PascalCase record props automatically.

## 2. Domain record

In `src/FlowBoard.Core/Models/Domain.cs`, add or extend the record. **Match Npgsql reader types exactly** — see the `dapper-records` skill. Common pitfalls:

- `DATE` → `DateTime` (NOT `DateOnly`)
- `COUNT(*)`, `COALESCE(SUM(int),0)` → `long`
- `SUM(...) OVER (...)` window → `decimal`

## 3. Request DTO

In `src/FlowBoard.Api/Models/Dtos.cs`, add a `CreateXRequest` / `UpdateXRequest` record with `[Required]` / `[StringLength]` attributes. Date inputs may use `DateOnly` (handled by registered `SqlMapper.TypeHandler`).

## 4. Controller

Add the action to the matching `*Controller.cs` under `src/FlowBoard.Api/Controllers/`. Always:

```csharp
if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
using var c = _db.Create();
var result = await c.QueryAsync<MyRecord>(MyQueries.Foo, new { ProjectId = projectId, ... });
return Ok(new { items = result });   // wrap in named property
```

Patterns:
- `[HttpGet("projects/{projectId:guid}/things")]` for project-scoped lists.
- `[HttpPatch("things/{id:guid}")]` for updates; authorize via `Authorize<Entity>` helper that resolves projectId from the entity id.
- Return `Created($"/api/things/{x.Id}", new { thing = x })` from POST.

## 5. Client API layer

In `client/src/api/<entity>.ts`, add a typed function:

```ts
export async function listThings(projectId: string): Promise<Thing[]> {
  const { data } = await api.get<{ things: Thing[] }>(`/projects/${projectId}/things`);
  return data.things;
}
```

Mirror request DTO field names exactly (camelCase).

## 6. Store / query hook

- Server state → TanStack Query: `useQuery({ queryKey: ['things', projectId], queryFn: () => listThings(projectId) })`.
- Mutations → `useMutation` with `queryClient.invalidateQueries({ queryKey: ['things', projectId] })` in `onSuccess`.
- Local UI state only → Zustand store under `client/src/stores/`.

## 7. UI

Add the component under `client/src/features/<area>/` and wire it into the relevant page in `client/src/pages/`.

## 8. Verify

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build FlowBoard.sln -nologo
```

Restart the API and exercise the new route with `Invoke-RestMethod` (see `.claude/commands/test-endpoints.md` for the auth flow), then check the client.

## Checklist

- [ ] SQL added to `Data/Queries/<Entity>Queries.cs`
- [ ] Domain record types match Npgsql reader types
- [ ] DTO added to `Dtos.cs` with validation attributes
- [ ] Controller action authorizes via `ProjectAuthorizer`
- [ ] Client API function added
- [ ] React Query hook added with invalidation
- [ ] Build passes; endpoint returns 200 with sample request
