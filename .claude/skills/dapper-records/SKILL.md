---
name: dapper-records
description: Diagnose and fix Dapper 2.1.66 + Npgsql 9 record-materialization errors in FlowBoard. USE WHEN you see `A parameterless default constructor or one matching signature (...) is required for ... materialization` or `The member X of type System.DateOnly cannot be used as a parameter value` or any 500 from a Dapper QueryAsync/QuerySingleAsync against Postgres. Covers DateOnly handling, COUNT/SUM type widening to long, window-SUM returning decimal, and forwarding constructors for queries that return a column subset.
---

# Skill: Dapper records ↔ Npgsql column types

## When this applies

- A 500 from any controller method that calls `QueryAsync<T>`, `QuerySingleAsync<T>`, or `QueryFirstOrDefaultAsync<T>` against Postgres.
- Stack trace contains `Dapper.SqlMapper.GenerateDeserializerFromMap` or `LookupDbType`.
- Error mentions `parameterless default constructor or one matching signature (...types...)` — **the listed types are the real reader types**, not what the record currently declares.

## Root cause

Dapper 2.1.66 record materialization is strict: the constructor's parameter types must equal the Npgsql reader column types exactly. There is no implicit `int↔long` or `DateTime↔DateOnly` conversion.

## The mapping table

| Postgres expression | Npgsql CLR type | Use in record |
|---|---|---|
| `DATE` (column) | `DateTime` | `DateTime` / `DateTime?` |
| `TIMESTAMPTZ`, `TIMESTAMP` | `DateTime` | `DateTime` / `DateTime?` |
| `INTEGER`, `INT4` (column) | `int` | `int` |
| `BIGINT`, `INT8` | `long` | `long` |
| `COUNT(*)` | `long` | `long` |
| `COUNT(expr) FILTER (...)` | `long` | `long` |
| `COALESCE(SUM(int4_col), 0)` | `long` | `long` |
| `SUM(SUM(int4)) OVER (...)` window | `decimal` | `decimal` |
| `ROUND(numeric_expr, n)` | `decimal` | `decimal` / `decimal?` |
| `NUMERIC` | `decimal` | `decimal` |
| `BOOLEAN` | `bool` | `bool` |
| `UUID` | `Guid` | `Guid` |
| `TEXT`, `VARCHAR` | `string` | `string` (or `string?` if NULLable) |

## Fix algorithm

1. **Read the error's type signature.** It is the reader's truth. Example: `(System.Guid id, System.String title, System.DateTime due_date, System.Int64 total_issues, System.Decimal percent_complete)`.
2. **Open the record** (likely in `src/FlowBoard.Core/Models/Domain.cs`).
3. **Match each parameter's type and order** to the error's signature.
4. **Rebuild** with `& "C:\Program Files\dotnet\dotnet.exe" build FlowBoard.sln -nologo` (kill the running API first).
5. **Re-run the failing endpoint** and verify 200.

## Special cases

### DateOnly on input (request DTOs)

Frontend sends `"2026-05-01"` for date fields. Keep `DateOnly` / `DateOnly?` on `CreateEpicRequest` / `UpdateEpicRequest` / `CreateSprintRequest` / `UpdateSprintRequest`. This works only because `DapperSetup.Initialize` registers two `SqlMapper.TypeHandler` instances:

```csharp
SqlMapper.AddTypeHandler(new DateOnlyHandler());
SqlMapper.AddTypeHandler(new NullableDateOnlyHandler());
```

If you ever drop those handlers, parameter binding will throw `The member StartDate of type System.DateOnly cannot be used as a parameter value`.

### Output records use DateTime

Even though the input DTO uses `DateOnly`, the **result** record (`Epic`, `Sprint`, `EpicWithProgress`, `SprintVelocityRow`) must use `DateTime` for `DATE` columns because Npgsql returns `DateTime` on the read side. JSON serializes as `"2026-05-01T00:00:00"` which `Date.parse` and `dayjs` accept fine.

### Column-subset queries

If query A returns 6 columns and query B returns 7 (with an extra `role`), keep one canonical record and add a forwarding ctor:

```csharp
public record Project(Guid Id, string Name, string Slug, string? Description,
                      Guid OwnerId, DateTime CreatedAt, string? Role = null)
{
    // Used by queries that don't include the role column.
    public Project(Guid id, string name, string slug, string? description,
                   Guid ownerId, DateTime createdAt)
        : this(id, name, slug, description, ownerId, createdAt, null) {}
}
```

### Window aggregates are decimal, not long

`SUM(SUM(x)) OVER (ORDER BY ...)` returns `numeric` even when `x` is `int`. Use `decimal` in the record. Plain (non-window) `COALESCE(SUM(int4),0)` is `long`.

## Smoke test after fixing

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build FlowBoard.sln -nologo
Get-Process FlowBoard.Api -ErrorAction SilentlyContinue | Stop-Process -Force
$env:ASPNETCORE_ENVIRONMENT='Development'
& "C:\Program Files\dotnet\dotnet.exe" run --project src/FlowBoard.Api --no-build
```

Then run `/test-endpoints` (see `.claude/commands/test-endpoints.md`).

## Don't

- Don't change SQL column names to "fix" mapping — `MatchNamesWithUnderscores = true` already handles snake_case.
- Don't add `DateOnly` to result records.
- Don't widen request DTO types to `DateTime` — the frontend posts plain date strings; `DateOnly` + the type handlers are correct.
- Don't `[Column]`-attribute records to fix this; types, not names, are the issue here.
