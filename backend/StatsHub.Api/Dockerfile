# syntax=docker/dockerfile:1
#
# Built with the repo root as the Docker context (see railway.json's
# dockerfilePath) so Railway's GitHub-connected auto-deploys work from this
# monorepo without a separate "root directory" setting.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/StatsHub.Api/StatsHub.Api.csproj backend/StatsHub.Api/
RUN dotnet restore backend/StatsHub.Api/StatsHub.Api.csproj

COPY backend/StatsHub.Api/ backend/StatsHub.Api/
RUN dotnet publish backend/StatsHub.Api/StatsHub.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Railway (and similar hosts) inject $PORT at runtime; bind to it if set,
# otherwise fall back to 8080 for a plain `docker run`.
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet StatsHub.Api.dll"]
