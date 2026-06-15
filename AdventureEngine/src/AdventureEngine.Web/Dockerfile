FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["AdventureEngine/Directory.Build.props", "./"]
COPY ["AdventureEngine/AdventureEngine.slnx", "./"]
COPY ["AdventureEngine/src/AdventureEngine.Domain/", "src/AdventureEngine.Domain/"]
COPY ["AdventureEngine/src/AdventureEngine.Application/", "src/AdventureEngine.Application/"]
COPY ["AdventureEngine/src/AdventureEngine.Infrastructure/", "src/AdventureEngine.Infrastructure/"]
COPY ["AdventureEngine/src/AdventureEngine.Web/", "src/AdventureEngine.Web/"]
RUN dotnet restore ./src/AdventureEngine.Web/AdventureEngine.Web.csproj

COPY . .
RUN dotnet publish ./src/AdventureEngine.Web/AdventureEngine.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AdventureEngine.Web.dll"]
