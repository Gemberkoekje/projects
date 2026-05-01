# Dockerfile for AiUsageMonitor (multi-stage)
# Builds the project using the .NET 10 SDK and produces a small runtime image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The build context is AiUsageMonitoring (repo root for this module)
COPY Directory.Packages.props ./
COPY src/AiUsageMonitor/AiUsageMonitor.csproj src/AiUsageMonitor/
RUN dotnet restore src/AiUsageMonitor/AiUsageMonitor.csproj

COPY . .
RUN dotnet publish src/AiUsageMonitor/AiUsageMonitor.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Run as non-root if desired (optional)
# RUN useradd -m appuser && chown -R appuser /app
# USER appuser

ENTRYPOINT ["dotnet", "AiUsageMonitor.dll"]
