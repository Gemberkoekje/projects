# STS2 Annotator - Quick Commands

This file lists the most important commands for PostgreSQL sync workflows.

## Prerequisites

Set a PostgreSQL connection string (either option):

```powershell
$env:STS2_POSTGRES_CONNECTION_STRING="Host=localhost;Port=5432;Database=sts2;Username=postgres;Password=postgres"
```

Or pass it inline with `--connection-string` on commands.

---

## 1) Full DB sync (rebuild everything)

Use this when you want a clean re-sync of extracted data and regenerated derived tables.

```powershell
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --all-characters
```

### Full DB sync + annotation

```powershell
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --all-characters --annotate
```

---

## 2) Resume DB sync

Use this when a previous sync was interrupted and you want to continue without reloading already-present base data.

```powershell
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --all-characters --resume
```

### Resume DB sync + annotation enabled

```powershell
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --all-characters --annotate --resume
```

---

## 3) Single-character sync (faster iteration)

```powershell
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --character silent
```

Resume single-character:

```powershell
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --character silent --resume
```

---

## 4) Initialize schema only

Use this once for fresh databases.

```powershell
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- init-database --root .\sts2
```

---

## Notes

- `sync-postgres` without `--resume` performs a full refresh pass.
- `--resume` skips already-loaded phases when possible, then continues generation stages.
- `--all-characters` includes: `ironclad`, `silent`, `defect`, `regent`, `necrobinder`, plus shared/colorless data.
