FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["TheCuratool/src/TheCuratool.Web/TheCuratool.Web.csproj", "src/TheCuratool.Web/"]
COPY ["TheCuratool/src/TheCuratool.Application/TheCuratool.Application.csproj", "src/TheCuratool.Application/"]
COPY ["TheCuratool/src/TheCuratool.Domain/TheCuratool.Domain.csproj", "src/TheCuratool.Domain/"]
COPY ["TheCuratool/src/TheCuratool.Infrastructure/TheCuratool.Infrastructure.csproj", "src/TheCuratool.Infrastructure/"]
COPY ["TheCuratool/tests/TheCuratool.UnitTests/TheCuratool.UnitTests.csproj", "tests/TheCuratool.UnitTests/"]
COPY ["TheCuratool/Directory.Build.props", "."]
COPY ["TheCuratool/TheCuratool.slnx", "."]
RUN dotnet restore "src/TheCuratool.Web/TheCuratool.Web.csproj"

COPY TheCuratool/ .
WORKDIR /src/src/TheCuratool.Web
RUN dotnet publish "TheCuratool.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TheCuratool.Web.dll"]
