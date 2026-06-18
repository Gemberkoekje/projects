# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/HackerMinigames.Api/HackerMinigames.Api.csproj src/HackerMinigames.Api/
RUN dotnet restore src/HackerMinigames.Api/HackerMinigames.Api.csproj
COPY . .
RUN dotnet publish src/HackerMinigames.Api/HackerMinigames.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "HackerMinigames.Api.dll"]
