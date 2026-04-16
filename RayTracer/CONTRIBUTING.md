# Contributing

## Prerequisites

- .NET 10 SDK
- Windows environment for running the WinForms app (`RayTracer`)

## Build

```powershell
dotnet restore .net.csproj
dotnet build .net.csproj --configuration Release
```

## Test

```powershell
dotnet test RayTracer.Tests/RayTracer.Tests.csproj --configuration Release
```

## Run app

```powershell
dotnet run --project RayTracer/RayTracer.App.csproj --configuration Release
```

## Run benchmarks (optional)

```powershell
dotnet run --project Benchmark/Benchmarks.csproj --configuration Release
```

## Coding conventions

- Follow `.editorconfig` defaults.
- Keep changes small and behavior-preserving unless explicitly changing behavior.
- Add/adjust tests with feature or refactor changes.
- Prefer explicit option records/config models over broad parameter lists.

## Pull request checklist

- Build succeeds in Release configuration.
- Relevant tests pass locally.
- No new warnings introduced in touched code/projects.
- Formatting/style matches repository conventions.
- Docs are updated when architecture or workflow changes.
