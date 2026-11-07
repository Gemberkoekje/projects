# syntax=docker/dockerfile:1
# The image that hosts the game (gembernodes, apps/bygalacticaccord). The client runs the whole simulation
# in the browser (design §18), so the site is static: nginx serving the published wwwroot. No server code,
# no data, no settings. Build from this folder: docker build -t bygalacticaccord .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore on its own layer, so a code change doesn't fetch the packages again.
COPY Directory.Build.props ./
COPY src/ByGalacticAccord.Engine/ByGalacticAccord.Engine.csproj src/ByGalacticAccord.Engine/
COPY src/ByGalacticAccord.Presentation/ByGalacticAccord.Presentation.csproj src/ByGalacticAccord.Presentation/
COPY src/ByGalacticAccord.Client/ByGalacticAccord.Client.csproj src/ByGalacticAccord.Client/
RUN dotnet restore src/ByGalacticAccord.Client/ByGalacticAccord.Client.csproj
COPY src/ src/
# Stock nginx serves the .gz files the publish writes next to each asset, but has no Brotli: drop the .br files.
RUN dotnet publish src/ByGalacticAccord.Client/ByGalacticAccord.Client.csproj -c Release --no-restore -o /publish \
 && find /publish/wwwroot -name '*.br' -delete

# The unprivileged image runs as uid 101 and listens on 8080.
FROM nginxinc/nginx-unprivileged:1.30-alpine
# One worker per CPU of the pod's limit, not per core of the node.
ENV NGINX_ENTRYPOINT_WORKER_PROCESSES_AUTOTUNE=1
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /publish/wwwroot /usr/share/nginx/html
EXPOSE 8080
