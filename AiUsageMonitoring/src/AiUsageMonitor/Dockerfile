# Dockerfile for AiUsageMonitor (multi-stage)
# Builds the project using the .NET 10 SDK and produces a small runtime image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and restore (copying everything for simplicity)
COPY . .
WORKDIR /src/src/AiUsageMonitor

RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Run as non-root if desired (optional)
# RUN useradd -m appuser && chown -R appuser /app
# USER appuser

ENTRYPOINT ["dotnet", "AiUsageMonitor.dll"]
