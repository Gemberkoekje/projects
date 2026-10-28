# Runtime only: CI publishes the app into publish/ and this image just carries the output. No SDK,
# no ARG, no secret baked in; every setting arrives as an environment variable.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

WORKDIR /app
COPY publish/ .

# The aspnet image ships a non-root account as $APP_UID. Nothing here touches the filesystem: the
# data, the sessie and even the data-protection key ring live in PostgreSQL.
USER $APP_UID

ENTRYPOINT ["dotnet", "Kluskat.WebApp.dll"]
