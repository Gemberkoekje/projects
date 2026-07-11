# Contributing

## Prerequisites

- .NET 10 SDK
- Windows environment for running the WinForms app (`RayTracer.Gpu`); the GPU backend needs a DXR 1.1 GPU

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
dotnet run --project RayTracer.Gpu --configuration Release
```

The default launch opens the config screen, where you pick the **GPU** or **CPU**
renderer and its options, then Start. Headless self-tests (dev box with a GPU):

```powershell
dotnet run --project RayTracer.Gpu -c Release -- --phase6-selftest
dotnet run --project RayTracer.Gpu -c Release -- --setup-selftest
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
