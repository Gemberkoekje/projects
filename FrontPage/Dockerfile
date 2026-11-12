# Runtime only: CI publishes the app into publish/ and this image just carries the output. No SDK,
# no ARG, no secret baked in; settings arrive as environment variables.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

WORKDIR /app
COPY publish/ .

# The aspnet image ships a non-root account as $APP_UID. The app writes nothing: the page is static
# and the cluster summary lives in memory.
USER $APP_UID

ENTRYPOINT ["dotnet", "FrontPage.WebApp.dll"]
